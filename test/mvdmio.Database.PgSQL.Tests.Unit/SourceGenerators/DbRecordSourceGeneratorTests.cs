using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using mvdmio.Database.PgSQL.SourceGenerators;
using mvdmio.Database.PgSQL.SourceGenerators.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Unit.SourceGenerators;

public class DbRecordSourceGeneratorTests
{
   private readonly VerifySettings _settings;

   public DbRecordSourceGeneratorTests()
   {
      if(!VerifySourceGenerators.Initialized)
         VerifySourceGenerators.Initialize();
      
      _settings = new VerifySettings();
      _settings.UseDirectory("_verify_snapshots");
   }
   
   [Fact]
   public async Task Test()
   {
      var testDbTableSourceCode = await File.ReadAllTextAsync("TestDbTable.cs", TestContext.Current.CancellationToken);
      var syntaxTree = CSharpSyntaxTree.ParseText(testDbTableSourceCode, cancellationToken: TestContext.Current.CancellationToken);
      var compilation = CSharpCompilation.Create(
         assemblyName: "TestProject",
         syntaxTrees: [
            syntaxTree
         ],
         references: [
            ..ReferenceAssemblies.NetStandard20,
            MetadataReference.CreateFromFile(typeof(ColumnAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(DbTable<>).Assembly.Location)
         ]
      );

      var generator = new DbRecordSourceGenerator();
      GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
      
      // Run the generator
      driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

      await Verify(driver, _settings);
   }   
}