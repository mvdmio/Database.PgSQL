using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Packaging.Fixture;
using System.Xml.Linq;

namespace mvdmio.Database.PgSQL.Tests.Packaging;

public class PackagedGeneratorTests
{
   private const string COMMENT_RULE_PROJECT = "mvdmio.Database.PgSQL.Lint";

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

   [Fact]
   public void Package_CarriesTheAnalyzerWhereTheCompilerLooksForIt()
   {
      _fixture.PackageContents.Should().Contain("analyzers/dotnet/cs/mvdmio.Database.PgSQL.Analyzers.dll");
   }

   [Fact]
   public void Package_CarriesTheAnalyzerExactlyOnce()
   {
      _fixture.PackageContents.Count(x => x.EndsWith("mvdmio.Database.PgSQL.Analyzers.dll", StringComparison.Ordinal)).Should().Be(1);
   }

   [Fact]
   public void Package_CarriesNoFileOfTheCommentRule()
   {
      _fixture.PackageContents.Should().Contain("analyzers/dotnet/cs/mvdmio.Database.PgSQL.Analyzers.dll");

      var leaked = _fixture.PackageContents.Where(x => x.Contains(COMMENT_RULE_PROJECT, StringComparison.Ordinal)).ToList();

      leaked.Should().BeEmpty($"the comment rule MVDM0053 must never reach a consumer, and these package entries carry {COMMENT_RULE_PROJECT}");
   }

   [Fact]
   public void Package_DeclaresNoDependencyOnTheCommentRule()
   {
      _fixture.PackageContents.Should().Contain("analyzers/dotnet/cs/mvdmio.Database.PgSQL.Analyzers.dll");

      var leaked = XDocument.Parse(_fixture.PackageNuspec)
         .Descendants()
         .Where(x => x.Name.LocalName == "dependency")
         .Select(x => (string?)x.Attribute("id"))
         .Where(x => x is not null && x.Contains(COMMENT_RULE_PROJECT, StringComparison.Ordinal))
         .ToList();

      leaked.Should().BeEmpty($"the comment rule MVDM0053 must never reach a consumer, and the nuspec lists these dependencies on {COMMENT_RULE_PROJECT}");
   }

   [Theory]
   [InlineData("net10.0")]
   [InlineData("net11.0")]
   public void Package_ResolvesALibraryForEveryTargetFramework(string targetFramework)
   {
      _fixture.PackageContents.Should().Contain($"lib/{targetFramework}/mvdmio.Database.PgSQL.dll");
   }

   [Fact]
   public void Consumer_CompilesAgainstTheInstalledPackage()
   {
      _fixture.Build.Succeeded.Should().BeTrue(_fixture.Build.Report("dotnet build of the scaffolded consumer"));
   }

   [Theory]
   [InlineData("net10.0")]
   [InlineData("net11.0")]
   public void Consumer_RunsAGeneratedRepositoryAgainstARealDatabase(string targetFramework)
   {
      var run = _fixture.Runs.Should().ContainKey(targetFramework).WhoseValue;

      run.Succeeded.Should().BeTrue(run.Report($"the scaffolded consumer on {targetFramework}"));

      run.Output.Should().Contain("generatedKey=True", "a [Generated] primary key with a private setter has to materialize");
      run.Output.Should().Contain("generatedTimestamp=True", "a [Generated] timestamp with a private setter has to materialize");
      run.Output.Should().Contain("storedState=Closed", "an unclaimed enum column holds the text of its member name");
      run.Output.Should().Contain("""document={"kind": "invoice"}""", "a string claimed as jsonb round-trips, reformatted by PostgreSQL because it really is JSON");
      run.Output.Should().Contain("queryMatched=1", "Query() filters the enum column the same way CreateAsync wrote it");
   }

   [Theory]
   [InlineData("net10.0")]
   [InlineData("net11.0")]
   public void Consumer_RunsTheReadmesOwnRepositorySample(string targetFramework)
   {
      var run = _fixture.Runs.Should().ContainKey(targetFramework).WhoseValue;

      run.Output.Should().Contain("readmeSample=True", run.Report($"the scaffolded consumer on {targetFramework}"));
   }
}
