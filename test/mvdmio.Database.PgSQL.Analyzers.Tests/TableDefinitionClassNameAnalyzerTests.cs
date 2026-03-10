using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using mvdmio.Database.PgSQL.Analyzers;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

public class TableDefinitionClassNameAnalyzerTests
{
   private const string _TABLE_ATTRIBUTE_STUB = """
      namespace mvdmio.Database.PgSQL.Attributes
      {
         [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
         public sealed class TableAttribute : System.Attribute
         {
            public TableAttribute(string name) { }
         }
      }
      """;

   [Fact]
   public async Task TableClassEndingWithTable_NoDiagnostic()
   {
      var source = """
         using mvdmio.Database.PgSQL.Attributes;

         [Table("public.users")]
         public partial class UserTable
         {
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task TableClassWithoutTableSuffix_ProducesWarning()
   {
      var source = """
         using mvdmio.Database.PgSQL.Attributes;

         [Table("public.users")]
         public partial class {|#0:User|}
         {
         }
         """;

      var expected = new DiagnosticResult("PGSQL0002", DiagnosticSeverity.Warning)
         .WithLocation(0)
         .WithArguments("User");

      await RunAnalyzerAsync(source, expected);
   }

   private static async Task RunAnalyzerAsync(string source, params DiagnosticResult[] expectedDiagnostics)
   {
      var test = new CSharpAnalyzerTest<TableDefinitionClassNameAnalyzer, DefaultVerifier>
      {
         TestState =
         {
            Sources = { source, _TABLE_ATTRIBUTE_STUB },
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
         }
      };

      test.ExpectedDiagnostics.AddRange(expectedDiagnostics);
      await test.RunAsync();
   }
}
