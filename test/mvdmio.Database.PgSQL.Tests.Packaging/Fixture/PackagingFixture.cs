using mvdmio.Database.PgSQL.Tests.Packaging.Fixture;
using System.Reflection;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PackagingFixture))]
namespace mvdmio.Database.PgSQL.Tests.Packaging.Fixture;

public sealed class PackagingFixture : IAsyncLifetime
{
   private const string PACKAGE_ID = "mvdmio.Database.PgSQL";

   private readonly PostgreSqlContainer _container;
   private readonly string _workingDirectory;
   private readonly string _repositoryRoot;

   public string PackageVersion { get; }

   public ProcessRun Pack { get; private set; } = ProcessRun.NotAttempted("packing the library");

   public ProcessRun Build { get; private set; } = ProcessRun.NotAttempted("building the scaffolded consumer");

   public IReadOnlyDictionary<string, ProcessRun> Runs { get; private set; } = new Dictionary<string, ProcessRun>();

   public IReadOnlyCollection<string> PackageContents { get; private set; } = [];

   public string PackageNuspec { get; private set; } = string.Empty;

   public PackagingFixture()
   {
      _container = new PostgreSqlBuilder("postgres:18").Build();
      _repositoryRoot = RepositoryRoot();

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
      PackageNuspec = ReadPackageNuspec();
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

   private string ReadPackageNuspec()
   {
      var package = Directory.GetFiles(FeedDirectory, $"{PACKAGE_ID}.{PackageVersion}.nupkg").Single();

      using var archive = System.IO.Compression.ZipFile.OpenRead(package);
      using var reader = new StreamReader(archive.GetEntry($"{PACKAGE_ID}.nuspec")!.Open());

      return reader.ReadToEnd();
   }

   private static string RepositoryRoot()
   {
      var configured = typeof(PackagingFixture).Assembly
         .GetCustomAttributes<AssemblyMetadataAttribute>()
         .Single(x => string.Equals(x.Key, "RepositoryRoot", StringComparison.Ordinal))
         .Value!;

      return Path.GetFullPath(configured);
   }
}
