using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Packaging.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Packaging;

/// <summary>
///    That installing the published package gives a consumer a working source generator — asserted against the produced
///    <c>.nupkg</c>, a project scaffolded at test time, and a real database.
/// </summary>
/// <remarks>
///    All the work happens once, in <see cref="PackagingFixture" />; each test reads one part of the result. That keeps the
///    slowest test in the repository to a single pack, a single restore and one build per framework, and it means a
///    packaging failure is reported by the test that describes it rather than by whichever ran first.
/// </remarks>
public class PackagedGeneratorTests
{
   private readonly PackagingFixture _fixture;

   public PackagedGeneratorTests(PackagingFixture fixture)
   {
      _fixture = fixture;
   }

   [Fact]
   public void Pack_ProducesThePackage()
   {
      _fixture.Pack.Succeeded.Should().BeTrue(_fixture.Pack.Report("dotnet pack"));
   }

   /// <summary>
   ///    The generator's location inside the package, asserted directly. A consumer's compiler looks for it at exactly this
   ///    path and silently runs no generator at all when it is not there, which is the defect this suite exists for.
   /// </summary>
   [Fact]
   public void Package_CarriesTheAnalyzerWhereTheCompilerLooksForIt()
   {
      _fixture.PackageContents.Should().Contain("analyzers/dotnet/cs/mvdmio.Database.PgSQL.Analyzers.dll");
   }

   /// <summary>
   ///    Added once rather than per target framework: the package targets three, and an item contributed by each inner
   ///    build would land the same file three times and warn about it.
   /// </summary>
   [Fact]
   public void Package_CarriesTheAnalyzerExactlyOnce()
   {
      _fixture.PackageContents.Count(x => x.EndsWith("mvdmio.Database.PgSQL.Analyzers.dll", StringComparison.Ordinal)).Should().Be(1);
   }

   [Theory]
   [InlineData("net8.0")]
   [InlineData("net9.0")]
   [InlineData("net10.0")]
   public void Package_ResolvesALibraryForEveryTargetFramework(string targetFramework)
   {
      _fixture.PackageContents.Should().Contain($"lib/{targetFramework}/mvdmio.Database.PgSQL.dll");
   }

   /// <summary>
   ///    The whole point: a table definition in another project, compiled against nothing but the installed package,
   ///    produces the repository, the command types and the data type it needs. Warnings are errors in the scaffolded
   ///    project, so a diagnostic the release does not intend fails here too.
   /// </summary>
   [Fact]
   public void Consumer_CompilesAgainstTheInstalledPackage()
   {
      _fixture.Build.Succeeded.Should().BeTrue(_fixture.Build.Report("dotnet build of the scaffolded consumer"));
   }

   [Theory]
   [InlineData("net8.0")]
   [InlineData("net9.0")]
   [InlineData("net10.0")]
   public void Consumer_RunsAGeneratedRepositoryAgainstARealDatabase(string targetFramework)
   {
      var run = _fixture.Runs.Should().ContainKey(targetFramework).WhoseValue;

      run.Succeeded.Should().BeTrue(run.Report($"the scaffolded consumer on {targetFramework}"));

      // One line per thing the release changed, so a partial failure names which part broke rather than only that the
      // process exited non-zero.
      run.Output.Should().Contain("generatedKey=True", "a [Generated] primary key with a private setter has to materialize");
      run.Output.Should().Contain("generatedTimestamp=True", "a [Generated] timestamp with a private setter has to materialize");
      run.Output.Should().Contain("storedState=Closed", "an unclaimed enum column holds the text of its member name");
      run.Output.Should().Contain("""document={"kind": "invoice"}""", "a string claimed as jsonb round-trips, reformatted by PostgreSQL because it really is JSON");
      run.Output.Should().Contain("queryMatched=1", "Query() filters the enum column the same way CreateAsync wrote it");
   }

   /// <summary>
   ///    The README's own repository walkthrough, compiled and run against the published package — so the documentation is
   ///    something a reader can trust rather than something they have to test.
   /// </summary>
   [Theory]
   [InlineData("net8.0")]
   [InlineData("net9.0")]
   [InlineData("net10.0")]
   public void Consumer_RunsTheReadmesOwnRepositorySample(string targetFramework)
   {
      var run = _fixture.Runs.Should().ContainKey(targetFramework).WhoseValue;

      run.Output.Should().Contain("readmeSample=True", run.Report($"the scaffolded consumer on {targetFramework}"));
   }
}
