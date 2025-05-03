using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using mvdmio.Database.PgSQL.SourceGenerators;

namespace mvdmio.Database.PgSQL.Tests.Unit.SourceGenerators;

public class TableColumnsSourceGeneratorTests
{
   private readonly VerifySettings _settings;

   public TableColumnsSourceGeneratorTests()
   {
      if(!VerifySourceGenerators.Initialized)
         VerifySourceGenerators.Initialize();

      _settings = new VerifySettings();
      _settings.UseDirectory(".verify");
   }
   
   [Fact]
   public async Task Test()
   {
      var syntaxTree = CSharpSyntaxTree.ParseText(
         """
         using mvdmio.Database.PgSQL.Attributes;
         
         namespace TestNamespace;
         
         [Table("test")]
         public partial class TestDbTable : DbTable
         {
            [Column("id")]
            public long Id { get; set; }

            [Column("first_name")]
            public required string FirstName { get; set; }

            [Column("last_name")]
            public required string LastName { get; set; }

            [Column("email")]
            public required string Email { get; set; }
         }
         """,
         cancellationToken: TestContext.Current.CancellationToken
      );
      
      var compilation = CSharpCompilation.Create(
         assemblyName: "TestProject",
         syntaxTrees: [syntaxTree]
      );

      var generator = new TableColumnsSourceGenerator();
      GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
      
      // Run the generator
      driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

      await Verify(driver, _settings);
   }
}