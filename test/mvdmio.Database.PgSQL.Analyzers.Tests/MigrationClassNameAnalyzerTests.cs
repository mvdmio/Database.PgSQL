using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using mvdmio.Database.PgSQL.Analyzers;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

/// <summary>
///    Unit tests for <see cref="MigrationClassNameAnalyzer"/>.
/// </summary>
public class MigrationClassNameAnalyzerTests
{
   // Minimal stub of IDbMigration so the analyzer can resolve the interface
   // without having to pull in the full Npgsql/Dapper dependency graph.
   private const string _IDBMIGRATION_STUB = """
      using System.Threading.Tasks;

      namespace mvdmio.Database.PgSQL
      {
         public class DatabaseConnection { }
      }

      namespace mvdmio.Database.PgSQL.Migrations.Interfaces
      {
         public interface IDbMigration
         {
            long Identifier => 0;
            string Name => string.Empty;
            System.Threading.Tasks.Task UpAsync(mvdmio.Database.PgSQL.DatabaseConnection db);
         }
      }
      """;

   [Fact]
   public async Task ValidClassName_NoDiagnostic()
   {
      var source = """
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;
         using mvdmio.Database.PgSQL.Migrations.Interfaces;

         public class _202310191050_AddUsersTable : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task ValidClassName_WithoutLeadingUnderscore_NoDiagnostic()
   {
      // The regex allows an optional leading underscore
      var source = """
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;
         using mvdmio.Database.PgSQL.Migrations.Interfaces;

         public class _202310191050_AddUsersTable : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task NoIdentifierInClassName_ProducesDiagnostic()
   {
      var source = """
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;
         using mvdmio.Database.PgSQL.Migrations.Interfaces;

         public class {|#0:AddUsersTableMigration|} : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      var expected = new DiagnosticResult(MigrationClassNameAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
         .WithLocation(0)
         .WithArguments("AddUsersTableMigration", "{YYYYMMddHHmm}", "{MigrationName}");

      await RunAnalyzerAsync(source, expected);
   }

   [Fact]
   public async Task IdentifierTooShort_ProducesDiagnostic()
   {
      // 10 digits instead of 12
      var source = """
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;
         using mvdmio.Database.PgSQL.Migrations.Interfaces;

         public class {|#0:_2023101910_AddUsers|} : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      var expected = new DiagnosticResult(MigrationClassNameAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
         .WithLocation(0)
         .WithArguments("_2023101910_AddUsers", "{YYYYMMddHHmm}", "{MigrationName}");

      await RunAnalyzerAsync(source, expected);
   }

   [Fact]
   public async Task MissingNamePart_ProducesDiagnostic()
   {
      // Only the timestamp, no name after the second underscore
      var source = """
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;
         using mvdmio.Database.PgSQL.Migrations.Interfaces;

         public class {|#0:_202310191050_|} : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      var expected = new DiagnosticResult(MigrationClassNameAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
         .WithLocation(0)
         .WithArguments("_202310191050_", "{YYYYMMddHHmm}", "{MigrationName}");

      await RunAnalyzerAsync(source, expected);
   }

   [Fact]
   public async Task AbstractClass_NoDiagnostic()
   {
      // Abstract classes are excluded — the analyzer only targets concrete classes
      var source = """
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;
         using mvdmio.Database.PgSQL.Migrations.Interfaces;

         public abstract class BadlyNamedMigrationBase : IDbMigration
         {
            public abstract Task UpAsync(DatabaseConnection db);
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task ClassNotImplementingIDbMigration_NoDiagnostic()
   {
      var source = """
         public class BadlyNamedPlainClass
         {
            public int Value { get; set; }
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task MultipleClasses_OnlyInvalidOnesDiagnosed()
   {
      var source = """
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;
         using mvdmio.Database.PgSQL.Migrations.Interfaces;

         public class _202310191050_AddUsersTable : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
         }

         public class {|#0:AddRolesTableMigration|} : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      var expected = new DiagnosticResult(MigrationClassNameAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
         .WithLocation(0)
         .WithArguments("AddRolesTableMigration", "{YYYYMMddHHmm}", "{MigrationName}");

      await RunAnalyzerAsync(source, expected);
   }

   // -----------------------------------------------------------------------
   // Helpers
   // -----------------------------------------------------------------------

   private static async Task RunAnalyzerAsync(string source, params DiagnosticResult[] expectedDiagnostics)
   {
      var test = new CSharpAnalyzerTest<MigrationClassNameAnalyzer, DefaultVerifier>
      {
         TestState =
         {
            Sources = { source, _IDBMIGRATION_STUB },
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
         }
      };

      test.ExpectedDiagnostics.AddRange(expectedDiagnostics);
      await test.RunAsync();
   }
}
