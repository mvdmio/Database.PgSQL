using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

public class MigrationPartsAnalyzerTests
{
   private const string IDBMIGRATION_STUB = """
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
            Task UpAsync(mvdmio.Database.PgSQL.DatabaseConnection db) => throw new System.InvalidOperationException();
            Task PreDeployAsync(mvdmio.Database.PgSQL.DatabaseConnection db) => UpAsync(db);
            Task PostDeployAsync(mvdmio.Database.PgSQL.DatabaseConnection db) => Task.CompletedTask;
         }
      }
      """;

   private const string USINGS = """
      using System.Threading.Tasks;
      using mvdmio.Database.PgSQL;
      using mvdmio.Database.PgSQL.Migrations.Interfaces;

      """;

   [Fact]
   public async Task UpAsyncAlone_NoDiagnostic()
   {
      var source = USINGS + """
         public class _202310191050_AddUsersTable : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task PreDeployAsyncAlone_NoDiagnostic()
   {
      var source = USINGS + """
         public class _202310191050_AddUsersTable : IDbMigration
         {
            public Task PreDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task PreDeployAsyncWithPostDeployAsync_NoDiagnostic()
   {
      var source = USINGS + """
         public class _202310191050_AddUsersTable : IDbMigration
         {
            public Task PreDeployAsync(DatabaseConnection db) => Task.CompletedTask;
            public Task PostDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task UpAsyncDefinedOnBaseClass_NoDiagnostic()
   {
      var source = USINGS + """
         public abstract class MigrationBase : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
         }

         public class _202310191050_AddUsersTable : MigrationBase
         {
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task AbstractClassWithoutParts_NoDiagnostic()
   {
      var source = USINGS + """
         public abstract class MigrationBase : IDbMigration
         {
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task ClassNotImplementingIDbMigration_NoDiagnostic()
   {
      var source = """
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;

         public class NotAMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
            public Task PostDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task PostDeployAsyncWithUpAsync_ProducesPostDeployWithUpDiagnostic()
   {
      var source = USINGS + """
         public class {|#0:_202310191050_AddUsersTable|} : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
            public Task PostDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source, ExpectedDiagnostic(MigrationPartsAnalyzer.PostDeployWithUpDiagnosticId, "_202310191050_AddUsersTable"));
   }

   [Fact]
   public async Task PostDeployAsyncWithUpAsyncAndPreDeployAsync_ProducesOnlyPostDeployWithUpDiagnostic()
   {
      var source = USINGS + """
         public class {|#0:_202310191050_AddUsersTable|} : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
            public Task PreDeployAsync(DatabaseConnection db) => Task.CompletedTask;
            public Task PostDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source, ExpectedDiagnostic(MigrationPartsAnalyzer.PostDeployWithUpDiagnosticId, "_202310191050_AddUsersTable"));
   }

   [Fact]
   public async Task ExplicitUpAsyncWithPostDeployAsync_ProducesPostDeployWithUpDiagnostic()
   {
      var source = USINGS + """
         public class {|#0:_202310191050_AddUsersTable|} : IDbMigration
         {
            Task IDbMigration.UpAsync(DatabaseConnection db) => Task.CompletedTask;
            public Task PostDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source, ExpectedDiagnostic(MigrationPartsAnalyzer.PostDeployWithUpDiagnosticId, "_202310191050_AddUsersTable"));
   }

   [Fact]
   public async Task ExplicitPostDeployAsyncWithUpAsync_ProducesPostDeployWithUpDiagnostic()
   {
      var source = USINGS + """
         public class {|#0:_202310191050_AddUsersTable|} : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
            Task IDbMigration.PostDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source, ExpectedDiagnostic(MigrationPartsAnalyzer.PostDeployWithUpDiagnosticId, "_202310191050_AddUsersTable"));
   }

   [Fact]
   public async Task UpAsyncOnBaseClassWithPostDeployAsync_ProducesPostDeployWithUpDiagnostic()
   {
      var source = USINGS + """
         public abstract class MigrationBase : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
         }

         public class {|#0:_202310191050_AddUsersTable|} : MigrationBase
         {
            public Task PostDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source, ExpectedDiagnostic(MigrationPartsAnalyzer.PostDeployWithUpDiagnosticId, "_202310191050_AddUsersTable"));
   }

   [Fact]
   public async Task UpAsyncWithPreDeployAsync_ProducesUpWithPreDeployDiagnostic()
   {
      var source = USINGS + """
         public class {|#0:_202310191050_AddUsersTable|} : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
            public Task PreDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source, ExpectedDiagnostic(MigrationPartsAnalyzer.UpWithPreDeployDiagnosticId, "_202310191050_AddUsersTable"));
   }

   [Fact]
   public async Task ExplicitUpAsyncWithExplicitPreDeployAsync_ProducesUpWithPreDeployDiagnostic()
   {
      var source = USINGS + """
         public class {|#0:_202310191050_AddUsersTable|} : IDbMigration
         {
            Task IDbMigration.UpAsync(DatabaseConnection db) => Task.CompletedTask;
            Task IDbMigration.PreDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source, ExpectedDiagnostic(MigrationPartsAnalyzer.UpWithPreDeployDiagnosticId, "_202310191050_AddUsersTable"));
   }

   [Fact]
   public async Task PreDeployAsyncOnBaseClassWithUpAsync_ProducesUpWithPreDeployDiagnostic()
   {
      var source = USINGS + """
         public abstract class MigrationBase : IDbMigration
         {
            public Task PreDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }

         public class {|#0:_202310191050_AddUsersTable|} : MigrationBase
         {
            public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source, ExpectedDiagnostic(MigrationPartsAnalyzer.UpWithPreDeployDiagnosticId, "_202310191050_AddUsersTable"));
   }

   [Fact]
   public async Task OnlyPostDeployAsync_ProducesNoPreDeployPartDiagnostic()
   {
      var source = USINGS + """
         public class {|#0:_202310191050_AddUsersTable|} : IDbMigration
         {
            public Task PostDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source, ExpectedDiagnostic(MigrationPartsAnalyzer.NoPreDeployPartDiagnosticId, "_202310191050_AddUsersTable"));
   }

   [Fact]
   public async Task NoPartsAtAll_ProducesNoPreDeployPartDiagnostic()
   {
      var source = USINGS + """
         public class {|#0:_202310191050_AddUsersTable|} : IDbMigration
         {
         }
         """;

      await RunAnalyzerAsync(source, ExpectedDiagnostic(MigrationPartsAnalyzer.NoPreDeployPartDiagnosticId, "_202310191050_AddUsersTable"));
   }

   [Fact]
   public async Task OnlyExplicitPostDeployAsync_ProducesNoPreDeployPartDiagnostic()
   {
      var source = USINGS + """
         public class {|#0:_202310191050_AddUsersTable|} : IDbMigration
         {
            Task IDbMigration.PostDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }
         """;

      await RunAnalyzerAsync(source, ExpectedDiagnostic(MigrationPartsAnalyzer.NoPreDeployPartDiagnosticId, "_202310191050_AddUsersTable"));
   }

   [Fact]
   public async Task PostDeployAsyncOnBaseClassOnly_ProducesNoPreDeployPartDiagnostic()
   {
      var source = USINGS + """
         public abstract class MigrationBase : IDbMigration
         {
            public Task PostDeployAsync(DatabaseConnection db) => Task.CompletedTask;
         }

         public class {|#0:_202310191050_AddUsersTable|} : MigrationBase
         {
         }
         """;

      await RunAnalyzerAsync(source, ExpectedDiagnostic(MigrationPartsAnalyzer.NoPreDeployPartDiagnosticId, "_202310191050_AddUsersTable"));
   }

   private static DiagnosticResult ExpectedDiagnostic(string diagnosticId, string className)
   {
      return new DiagnosticResult(diagnosticId, DiagnosticSeverity.Error)
         .WithLocation(0)
         .WithArguments(className);
   }

   private static async Task RunAnalyzerAsync(string source, params DiagnosticResult[] expectedDiagnostics)
   {
      var test = new CSharpAnalyzerTest<MigrationPartsAnalyzer, DefaultVerifier>
      {
         TestState =
         {
            Sources = { source, IDBMIGRATION_STUB },
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
         }
      };

      test.ExpectedDiagnostics.AddRange(expectedDiagnostics);
      await test.RunAsync();
   }
}
