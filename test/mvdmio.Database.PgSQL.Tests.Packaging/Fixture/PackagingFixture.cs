using mvdmio.Database.PgSQL.Tests.Packaging.Fixture;
using System.Reflection;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PackagingFixture))]
namespace mvdmio.Database.PgSQL.Tests.Packaging.Fixture;

/// <summary>
///    Packs the library, installs the package into a project scaffolded here and now, builds it for every framework the
///    library targets, and runs it against a real database.
/// </summary>
/// <remarks>
///    This exists because the produced <c>.nupkg</c> has no other entry point in the repository, which is exactly why a
///    package shipping without its source generator went unnoticed: every other test project reaches the analyzer through
///    a project reference and none of them looks at the package.
///    <para>
///       Two isolations, and both matter. The package is versioned uniquely per run, so no stale copy of it can satisfy the
///       consumer's reference; and the consumer restores into a package folder under this run's temporary directory, so
///       nothing is served from the developer's cache and nothing is left in it.
///    </para>
///    <para>
///       Known gap, accepted: this proves the generator loads under the SDK that runs the test, not under the oldest SDK the
///       library claims to support. Testing the minimum SDK would mean installing an older one, so it is documented in the
///       README instead.
///    </para>
/// </remarks>
public sealed class PackagingFixture : IAsyncLifetime
{
   private const string PACKAGE_ID = "mvdmio.Database.PgSQL";

   private readonly PostgreSqlContainer _container;
   private readonly string _workingDirectory;
   private readonly string _repositoryRoot;

   /// <summary>The version this run packs under, which nothing else can have published or cached.</summary>
   public string PackageVersion { get; }

   /// <summary>What packing the library did, so a test can report the SDK's own output when it failed.</summary>
   public ProcessRun Pack { get; private set; } = ProcessRun.NotAttempted("packing the library");

   /// <summary>What building the scaffolded consumer did, across all three frameworks in one invocation.</summary>
   /// <remarks>
   ///    Never left unset, even when packing failed and no build was attempted: a test reading this has to report what
   ///    happened rather than fail with a null reference and leave the pack output unread.
   /// </remarks>
   public ProcessRun Build { get; private set; } = ProcessRun.NotAttempted("building the scaffolded consumer");

   /// <summary>What the consumer wrote when run, per target framework. Empty when nothing was built to run.</summary>
   public IReadOnlyDictionary<string, ProcessRun> Runs { get; private set; } = new Dictionary<string, ProcessRun>();

   /// <summary>Everything the packed <c>.nupkg</c> contains, as relative paths with forward slashes.</summary>
   public IReadOnlyCollection<string> PackageContents { get; private set; } = [];

   public PackagingFixture()
   {
      _container = new PostgreSqlBuilder("postgres:18").Build();
      _repositoryRoot = RepositoryRoot();

      // Unique per run, and prefixed with a letter so the prerelease label cannot be read as a numeric identifier.
      PackageVersion = $"0.0.0-packagingtest{Guid.NewGuid():N}";
      _workingDirectory = Path.Combine(Path.GetTempPath(), "mvdmio-pgsql-packaging", PackageVersion);
   }

   public string FeedDirectory => Path.Combine(_workingDirectory, "feed");
   public string ConsumerDirectory => Path.Combine(_workingDirectory, "consumer");
   private string PackageCacheDirectory => Path.Combine(_workingDirectory, "packages");

   public async ValueTask InitializeAsync()
   {
      await _container.StartAsync();

      Directory.CreateDirectory(FeedDirectory);
      Directory.CreateDirectory(ConsumerDirectory);
      Directory.CreateDirectory(PackageCacheDirectory);

      Pack = PackTheLibrary();
      if (!Pack.Succeeded)
         return;

      PackageContents = ReadPackageContents();
      ScaffoldTheConsumer();

      Build = BuildTheConsumer();
      if (!Build.Succeeded)
         return;

      Runs = RunTheConsumer();
   }

   public async ValueTask DisposeAsync()
   {
      await _container.StopAsync();
      await _container.DisposeAsync();

      // Best effort: a left-behind temporary directory is untidy, while failing the run over one would hide whatever the
      // tests actually found.
      try
      {
         Directory.Delete(_workingDirectory, recursive: true);
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
   }

   /// <remarks>
   ///    <c>GeneratePackageOnBuild</c> is switched off for this invocation, and that is load-bearing rather than tidiness:
   ///    with it on, NuGet's <c>Pack</c> target does not depend on <c>Build</c> — because a build is what normally invokes
   ///    it — so <c>dotnet pack</c> silently packs whatever happens to be in <c>bin/</c>. This test would then assert
   ///    against a stale library and a stale analyzer, which is the one failure mode it cannot afford.
   /// </remarks>
   private ProcessRun PackTheLibrary()
   {
      return Dotnet.Run(
         "dotnet",
         [
            "pack",
            Path.Combine(_repositoryRoot, "src", PACKAGE_ID),
            "-c", "Release",
            "-o", FeedDirectory,
            $"-p:PgSqlVersion={PackageVersion}",
            "-p:GeneratePackageOnBuild=false",
            "--nologo"
         ],
         _repositoryRoot
      );
   }

   private void ScaffoldTheConsumer()
   {
      File.WriteAllText(Path.Combine(ConsumerDirectory, "consumer.csproj"), ConsumerProject.Csproj(PackageVersion));
      File.WriteAllText(Path.Combine(ConsumerDirectory, "NuGet.config"), ConsumerProject.NuGetConfig(FeedDirectory));
      File.WriteAllText(Path.Combine(ConsumerDirectory, "Program.cs"), ConsumerProject.PROGRAM);
   }

   private ProcessRun BuildTheConsumer()
   {
      return Dotnet.Run(
         "dotnet",
         ["build", "-c", "Release", "--nologo"],
         ConsumerDirectory,
         new Dictionary<string, string?> { ["NUGET_PACKAGES"] = PackageCacheDirectory }
      );
   }

   private Dictionary<string, ProcessRun> RunTheConsumer()
   {
      var runs = new Dictionary<string, ProcessRun>(StringComparer.Ordinal);

      foreach (var targetFramework in ConsumerProject.TargetFrameworks)
      {
         var apphost = Path.Combine(ConsumerDirectory, "bin", "Release", targetFramework, ConsumerProject.ASSEMBLY_NAME);

         runs[targetFramework] = Dotnet.Run(apphost, [_container.GetConnectionString()], ConsumerDirectory);
      }

      return runs;
   }

   private IReadOnlyCollection<string> ReadPackageContents()
   {
      var package = Directory.GetFiles(FeedDirectory, $"{PACKAGE_ID}.{PackageVersion}.nupkg").Single();

      using var archive = System.IO.Compression.ZipFile.OpenRead(package);

      return archive.Entries.Select(x => x.FullName.Replace('\\', '/')).ToList();
   }

   /// <remarks>
   ///    Read from an assembly attribute the project sets, rather than searched for by walking up from the test binary: the
   ///    project knows where it sits relative to the root and a search would have to guess what marks it.
   /// </remarks>
   private static string RepositoryRoot()
   {
      var configured = typeof(PackagingFixture).Assembly
         .GetCustomAttributes<AssemblyMetadataAttribute>()
         .Single(x => string.Equals(x.Key, "RepositoryRoot", StringComparison.Ordinal))
         .Value!;

      return Path.GetFullPath(configured);
   }
}
