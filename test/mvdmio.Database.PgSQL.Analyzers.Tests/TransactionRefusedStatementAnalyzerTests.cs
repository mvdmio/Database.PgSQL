using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

public class TransactionRefusedStatementAnalyzerTests
{
   private const string MIGRATION_NAME = "_202611231200_AddOrderIndex";

   private const string IDBMIGRATION_STUB = """
      using System.Threading.Tasks;

      namespace mvdmio.Database.PgSQL
      {
         public class DapperHandle
         {
            public Task<int> ExecuteAsync(string sql) => Task.FromResult(0);
         }

         public class DatabaseConnection
         {
            public DapperHandle Dapper => new DapperHandle();
         }
      }

      namespace mvdmio.Database.PgSQL.Migrations.Interfaces
      {
         public interface IDbMigration
         {
            long Identifier => 0;
            string Name => string.Empty;
            bool RunsInTransaction => true;
            System.Threading.Tasks.Task UpAsync(mvdmio.Database.PgSQL.DatabaseConnection db);
         }
      }
      """;

   private const string REFUSED_CALL = """await db.Dapper.ExecuteAsync({|#0:"VACUUM"|});""";

   [Theory]
   [InlineData("CREATE INDEX CONCURRENTLY idx ON orders (customer_id)", "CREATE INDEX CONCURRENTLY")]
   [InlineData("CREATE UNIQUE INDEX CONCURRENTLY idx ON orders (customer_id)", "CREATE INDEX CONCURRENTLY")]
   [InlineData("DROP INDEX CONCURRENTLY idx", "DROP INDEX CONCURRENTLY")]
   [InlineData("DROP INDEX CONCURRENTLY IF EXISTS public.idx", "DROP INDEX CONCURRENTLY")]
   [InlineData("REINDEX TABLE CONCURRENTLY orders", "REINDEX CONCURRENTLY")]
   [InlineData("REINDEX (CONCURRENTLY) TABLE orders", "REINDEX CONCURRENTLY")]
   [InlineData("REINDEX INDEX CONCURRENTLY idx", "REINDEX CONCURRENTLY")]
   [InlineData("ALTER TABLE orders DETACH PARTITION orders_2025 CONCURRENTLY", "DETACH PARTITION CONCURRENTLY")]
   [InlineData("VACUUM", "VACUUM")]
   [InlineData("VACUUM (ANALYZE) orders", "VACUUM")]
   [InlineData("CREATE DATABASE tenant_a", "CREATE DATABASE")]
   [InlineData("DROP DATABASE tenant_a", "DROP DATABASE")]
   [InlineData("CREATE TABLESPACE fast LOCATION '/data/fast'", "CREATE TABLESPACE")]
   [InlineData("DROP TABLESPACE fast", "DROP TABLESPACE")]
   [InlineData("ALTER SYSTEM SET work_mem = '64MB'", "ALTER SYSTEM")]
   public async Task RefusedStatement_ProducesDiagnosticNamingTheStatement(string sql, string statement)
   {
      var source = MigrationWithUp($$"""await db.Dapper.ExecuteAsync({|#0:@"{{sql}}"|});""");

      await RunAnalyzerAsync(source, Expected(statement));
   }

   [Theory]
   [InlineData("create index\n   concurrently idx on orders (customer_id)", "CREATE INDEX CONCURRENTLY")]
   [InlineData("Create\tUnique\r\nIndex   Concurrently idx on orders (customer_id)", "CREATE INDEX CONCURRENTLY")]
   [InlineData("reindex (concurrently) table orders", "REINDEX CONCURRENTLY")]
   [InlineData("alter table orders\n  detach partition orders_2025\n  concurrently", "DETACH PARTITION CONCURRENTLY")]
   public async Task RefusedStatement_IgnoresCaseAndWhitespace(string sql, string statement)
   {
      var source = MigrationWithUp($$"""await db.Dapper.ExecuteAsync({|#0:@"{{sql}}"|});""");

      await RunAnalyzerAsync(source, Expected(statement));
   }

   [Theory]
   [InlineData("REFRESH MATERIALIZED VIEW CONCURRENTLY order_totals")]
   [InlineData("CREATE INDEX idx ON orders (customer_id)")]
   [InlineData("CREATE UNIQUE INDEX idx ON orders (customer_id)")]
   [InlineData("DROP INDEX idx")]
   [InlineData("REINDEX TABLE orders")]
   [InlineData("ALTER TABLE orders DETACH PARTITION orders_2025")]
   [InlineData("ALTER TABLE orders ADD COLUMN note TEXT")]
   [InlineData("-- CREATE INDEX CONCURRENTLY later\nSELECT 1;")]
   [InlineData("SELECT 1; -- VACUUM\nSELECT 2;")]
   [InlineData("/* VACUUM */ SELECT 1")]
   [InlineData("/* run\nCREATE DATABASE x\nlater */ SELECT 1")]
   [InlineData("COMMENT ON TABLE orders IS 'run VACUUM weekly'")]
   [InlineData("SELECT vacuum_count FROM orders")]
   [InlineData("SELECT 'DROP INDEX CONCURRENTLY idx'")]
   public async Task AllowedOrNonStatementText_NoDiagnostic(string sql)
   {
      var source = MigrationWithUp($$"""await db.Dapper.ExecuteAsync(@"{{sql}}");""");

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task RefusedStatementAfterAnotherStatement_NamesTheRefusedStatement()
   {
      var source = MigrationWithUp("""await db.Dapper.ExecuteAsync({|#0:"SELECT 1; VACUUM"|});""");

      await RunAnalyzerAsync(source, Expected("VACUUM"));
   }

   [Fact]
   public async Task StringWithTwoRefusedStatements_NamesTheFirst()
   {
      var source = MigrationWithUp("""await db.Dapper.ExecuteAsync({|#0:"DROP DATABASE a; VACUUM; CREATE DATABASE b"|});""");

      await RunAnalyzerAsync(source, Expected("DROP DATABASE"));
   }

   [Fact]
   public async Task ClassNotImplementingIDbMigration_NoDiagnostic()
   {
      var source = """
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;

         public class _202611231200_NotAMigration
         {
            public Task UpAsync(DatabaseConnection db) => db.Dapper.ExecuteAsync("VACUUM");
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task AbstractMigrationClass_NoDiagnostic()
   {
      var source = """
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;
         using mvdmio.Database.PgSQL.Migrations.Interfaces;

         public abstract class _202611231200_AddOrderIndex : IDbMigration
         {
            public Task UpAsync(DatabaseConnection db) => db.Dapper.ExecuteAsync("VACUUM");
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task StringInlineInExecuteCall_ReportsOnTheString()
   {
      var source = MigrationWithUp("""await db.Dapper.ExecuteAsync({|#0:"CREATE INDEX CONCURRENTLY idx_orders_customer ON orders (customer_id);"|});""");

      await RunAnalyzerAsync(source, Expected("CREATE INDEX CONCURRENTLY"));
   }

   [Fact]
   public async Task RawStringLiteral_ReportsOnTheWholeLiteral()
   {
      var source = MigrationWithUp(
         """"
         await db.Dapper.ExecuteAsync({|#0:"""
            CREATE INDEX CONCURRENTLY idx_orders_customer
               ON orders (customer_id)
            """|});
         """"
      );

      await RunAnalyzerAsync(source, Expected("CREATE INDEX CONCURRENTLY"));
   }

   [Fact]
   public async Task VerbatimStringLiteral_ReportsOnTheWholeLiteral()
   {
      var source = MigrationWithUp(
         """"
         await db.Dapper.ExecuteAsync({|#0:@"CREATE INDEX CONCURRENTLY idx_orders_customer
            ON orders (customer_id)"|});
         """"
      );

      await RunAnalyzerAsync(source, Expected("CREATE INDEX CONCURRENTLY"));
   }

   [Fact]
   public async Task ConstField_ReportsOnTheInitializerNotTheCall()
   {
      var source = MigrationWithUp(
         "await db.Dapper.ExecuteAsync(SQL);",
         """private const string SQL = {|#0:"CREATE INDEX CONCURRENTLY idx ON orders (customer_id)"|};"""
      );

      await RunAnalyzerAsync(source, Expected("CREATE INDEX CONCURRENTLY"));
   }

   [Fact]
   public async Task LocalVariable_ReportsOnTheInitializerNotTheCall()
   {
      var source = MigrationWithUp(
         """
         var sql = {|#0:"DROP INDEX CONCURRENTLY idx"|};
         await db.Dapper.ExecuteAsync(sql);
         """
      );

      await RunAnalyzerAsync(source, Expected("DROP INDEX CONCURRENTLY"));
   }

   [Fact]
   public async Task HelperMethod_ReportsOnTheStringInTheHelper()
   {
      var source = MigrationWithUp(
         "await db.Dapper.ExecuteAsync(BuildSql());",
         """private static string BuildSql() => {|#0:"REINDEX TABLE CONCURRENTLY orders"|};"""
      );

      await RunAnalyzerAsync(source, Expected("REINDEX CONCURRENTLY"));
   }

   [Fact]
   public async Task Lambda_ReportsOnTheStringInTheLambda()
   {
      var source = MigrationWithUp(
         """
         System.Func<string> build = () => {|#0:"VACUUM orders"|};
         await db.Dapper.ExecuteAsync(build());
         """
      );

      await RunAnalyzerAsync(source, Expected("VACUUM"));
   }

   [Fact]
   public async Task LocalFunction_ReportsOnTheStringInTheLocalFunction()
   {
      var source = MigrationWithUp(
         """
         await db.Dapper.ExecuteAsync(Build());

         static string Build() => {|#0:"ALTER SYSTEM RESET ALL"|};
         """
      );

      await RunAnalyzerAsync(source, Expected("ALTER SYSTEM"));
   }

   [Fact]
   public async Task InterpolatedStringWithHoleInTableName_ReportsOnTheWholeString()
   {
      var source = MigrationWithUp(
         """
         var table = "orders";
         await db.Dapper.ExecuteAsync({|#0:$"CREATE INDEX CONCURRENTLY idx ON {table} (customer_id)"|});
         """
      );

      await RunAnalyzerAsync(source, Expected("CREATE INDEX CONCURRENTLY"));
   }

   [Fact]
   public async Task InterpolatedStringWithHoleInIndexName_ReportsOnTheWholeString()
   {
      var source = MigrationWithUp(
         """
         var suffix = "customer";
         await db.Dapper.ExecuteAsync({|#0:$"DROP INDEX CONCURRENTLY idx_{suffix}"|});
         """
      );

      await RunAnalyzerAsync(source, Expected("DROP INDEX CONCURRENTLY"));
   }

   [Fact]
   public async Task InterpolatedStringWithoutRefusedStatement_NoDiagnostic()
   {
      var source = MigrationWithUp(
         """
         var table = "orders";
         await db.Dapper.ExecuteAsync($"CREATE INDEX idx ON {table} (customer_id)");
         """
      );

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task TwoRefusedStrings_ReportTwoDiagnostics()
   {
      var source = MigrationWithUp(
         """
         await db.Dapper.ExecuteAsync({|#0:"DROP INDEX CONCURRENTLY IF EXISTS idx"|});
         await db.Dapper.ExecuteAsync({|#1:"CREATE INDEX CONCURRENTLY idx ON orders (customer_id)"|});
         """
      );

      await RunAnalyzerAsync(source, Expected("DROP INDEX CONCURRENTLY", 0), Expected("CREATE INDEX CONCURRENTLY", 1));
   }

   [Fact]
   public async Task PartialClassDeclarations_AreAllScanned()
   {
      var first = $$"""
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;
         using mvdmio.Database.PgSQL.Migrations.Interfaces;

         public partial class {{MIGRATION_NAME}} : IDbMigration
         {
            public async Task UpAsync(DatabaseConnection db)
            {
               await db.Dapper.ExecuteAsync({|#0:"VACUUM"|});
            }
         }
         """;

      var second = $$"""
         public partial class {{MIGRATION_NAME}}
         {
            private const string SQL = {|#1:"DROP TABLESPACE fast"|};
         }
         """;

      await RunAnalyzerAsync([first, second], Expected("VACUUM", 0), Expected("DROP TABLESPACE", 1));
   }

   [Fact]
   public async Task ExtraMethodSuchAsPostDeploy_IsScannedLikeAnyOther()
   {
      var source = MigrationWithUp(
         "await db.Dapper.ExecuteAsync(\"SELECT 1\");",
         """
         public async Task PostDeployAsync(DatabaseConnection db)
         {
            await db.Dapper.ExecuteAsync({|#0:"CREATE INDEX CONCURRENTLY idx ON orders (customer_id)"|});
         }
         """
      );

      await RunAnalyzerAsync(source, Expected("CREATE INDEX CONCURRENTLY"));
   }

   [Fact]
   public async Task ConcatenatedOperands_AreReadOnTheirOwn()
   {
      var source = MigrationWithUp("""await db.Dapper.ExecuteAsync({|#0:"VACUUM "|} + "orders");""");

      await RunAnalyzerAsync(source, Expected("VACUUM"));
   }

   [Fact]
   public async Task ConcatenatedOperandsSplitAcrossAStatement_NoDiagnostic()
   {
      var source = MigrationWithUp("""await db.Dapper.ExecuteAsync("CREATE INDEX " + "CONCURRENTLY idx ON orders (customer_id)");""");

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task NoRunsInTransactionOverride_ReportsFromTheInterfaceDefault()
   {
      var source = MigrationWithUp(REFUSED_CALL);

      await RunAnalyzerAsync(source, Expected("VACUUM"));
   }

   [Fact]
   public async Task ExpressionBodiedFalse_NoDiagnostic()
   {
      var source = MigrationWithUp(
         """await db.Dapper.ExecuteAsync("VACUUM");""",
         "public bool RunsInTransaction => false;"
      );

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task GetterReturningFalse_NoDiagnostic()
   {
      var source = MigrationWithUp(
         """await db.Dapper.ExecuteAsync("VACUUM");""",
         """
         public bool RunsInTransaction
         {
            get { return false; }
         }
         """
      );

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task GetOnlyAutoPropertyInitializedToFalse_NoDiagnostic()
   {
      var source = MigrationWithUp(
         """await db.Dapper.ExecuteAsync("VACUUM");""",
         "public bool RunsInTransaction { get; } = false;"
      );

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task ExplicitInterfaceImplementationReturningFalse_NoDiagnostic()
   {
      var source = MigrationWithUp(
         """await db.Dapper.ExecuteAsync("VACUUM");""",
         "bool IDbMigration.RunsInTransaction => false;"
      );

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task ConstantExpressionEvaluatingToFalse_NoDiagnostic()
   {
      var source = MigrationWithUp(
         """await db.Dapper.ExecuteAsync("VACUUM");""",
         """
         private const bool TRANSACTIONAL = false;
         public bool RunsInTransaction => TRANSACTIONAL;
         """
      );

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task ExpressionBodiedTrue_ReportsOnTheString()
   {
      var source = MigrationWithUp(REFUSED_CALL, "public bool RunsInTransaction => true;");

      await RunAnalyzerAsync(source, Expected("VACUUM"));
   }

   [Fact]
   public async Task GetterReturningTrue_ReportsOnTheString()
   {
      var source = MigrationWithUp(
         REFUSED_CALL,
         """
         public bool RunsInTransaction
         {
            get { return true; }
         }
         """
      );

      await RunAnalyzerAsync(source, Expected("VACUUM"));
   }

   [Fact]
   public async Task GetOnlyAutoPropertyInitializedToTrue_ReportsOnTheString()
   {
      var source = MigrationWithUp(REFUSED_CALL, "public bool RunsInTransaction { get; } = true;");

      await RunAnalyzerAsync(source, Expected("VACUUM"));
   }

   [Fact]
   public async Task ExplicitInterfaceImplementationReturningTrue_ReportsOnTheString()
   {
      var source = MigrationWithUp(REFUSED_CALL, "bool IDbMigration.RunsInTransaction => true;");

      await RunAnalyzerAsync(source, Expected("VACUUM"));
   }

   [Fact]
   public async Task FieldBackedValue_NoDiagnostic()
   {
      var source = MigrationWithUp(
         """await db.Dapper.ExecuteAsync("VACUUM");""",
         """
         private readonly bool _transactional = false;
         public bool RunsInTransaction => _transactional;
         """
      );

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task GetterWithSeveralStatements_NoDiagnostic()
   {
      var source = MigrationWithUp(
         """await db.Dapper.ExecuteAsync("VACUUM");""",
         """
         public bool RunsInTransaction
         {
            get
            {
               var transactional = true;
               return transactional;
            }
         }
         """
      );

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task AutoPropertyWithSetter_NoDiagnostic()
   {
      var source = MigrationWithUp(
         """await db.Dapper.ExecuteAsync("VACUUM");""",
         "public bool RunsInTransaction { get; set; } = true;"
      );

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task BaseClassInSameCompilationSettingFalse_NoDiagnostic()
   {
      var source = $$"""
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;
         using mvdmio.Database.PgSQL.Migrations.Interfaces;

         public abstract class NonTransactionalMigration : IDbMigration
         {
            public bool RunsInTransaction => false;
            public abstract Task UpAsync(DatabaseConnection db);
         }

         public class {{MIGRATION_NAME}} : NonTransactionalMigration
         {
            public override async Task UpAsync(DatabaseConnection db)
            {
               await db.Dapper.ExecuteAsync("VACUUM");
            }
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task BaseClassInSameCompilationSettingTrue_ReportsOnTheDerivedClassString()
   {
      var source = $$"""
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;
         using mvdmio.Database.PgSQL.Migrations.Interfaces;

         public abstract class TransactionalMigration : IDbMigration
         {
            public bool RunsInTransaction => true;
            public abstract Task UpAsync(DatabaseConnection db);
         }

         public class {{MIGRATION_NAME}} : TransactionalMigration
         {
            public override async Task UpAsync(DatabaseConnection db)
            {
               await db.Dapper.ExecuteAsync({|#0:"VACUUM"|});
            }
         }
         """;

      await RunAnalyzerAsync(source, Expected("VACUUM"));
   }

   [Fact]
   public async Task RefusedStringInBaseClass_IsNotReadForTheDerivedClass()
   {
      var source = $$"""
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;
         using mvdmio.Database.PgSQL.Migrations.Interfaces;

         public abstract class SharedMigration : IDbMigration
         {
            protected const string SQL = "VACUUM";
            public abstract Task UpAsync(DatabaseConnection db);
         }

         public class {{MIGRATION_NAME}} : SharedMigration
         {
            public override Task UpAsync(DatabaseConnection db) => db.Dapper.ExecuteAsync(SQL);
         }
         """;

      await RunAnalyzerAsync(source);
   }

   [Fact]
   public async Task InterfaceFromMetadata_MigrationWithoutOverride_ProducesDiagnostic()
   {
      var source = MigrationWithUp(REFUSED_CALL);

      await RunAnalyzerWithInterfaceFromMetadataAsync(source, Expected("VACUUM"));
   }

   [Fact]
   public async Task InterfaceFromMetadata_MigrationOverridingRunsInTransactionToFalse_ProducesNoDiagnostic()
   {
      var source = MigrationWithUp(REFUSED_CALL, "public bool RunsInTransaction => false;");

      await RunAnalyzerWithInterfaceFromMetadataAsync(source);
   }

   private static string MigrationWithUp(string upBody, string extraMembers = "")
   {
      return $$"""
         using System.Threading.Tasks;
         using mvdmio.Database.PgSQL;
         using mvdmio.Database.PgSQL.Migrations.Interfaces;

         public class {{MIGRATION_NAME}} : IDbMigration
         {
            public async Task UpAsync(DatabaseConnection db)
            {
               {{upBody}}
            }

            {{extraMembers}}
         }
         """;
   }

   private static DiagnosticResult Expected(string statement, int locationIndex = 0)
   {
      return new DiagnosticResult(TransactionRefusedStatementAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
         .WithLocation(locationIndex)
         .WithArguments(MIGRATION_NAME, statement);
   }

   private static Task RunAnalyzerAsync(string source, params DiagnosticResult[] expectedDiagnostics)
   {
      return RunAnalyzerAsync([source], expectedDiagnostics);
   }

   private static async Task RunAnalyzerAsync(string[] sources, params DiagnosticResult[] expectedDiagnostics)
   {
      var test = BuildTest(sources);
      test.TestState.Sources.Add(IDBMIGRATION_STUB);
      test.ExpectedDiagnostics.AddRange(expectedDiagnostics);
      await test.RunAsync();
   }

   private static async Task RunAnalyzerWithInterfaceFromMetadataAsync(string source, params DiagnosticResult[] expectedDiagnostics)
   {
      var test = BuildTest([source]);
      test.TestState.AdditionalReferences.Add(await CompileStubToMetadataAsync());
      test.ExpectedDiagnostics.AddRange(expectedDiagnostics);
      await test.RunAsync();
   }

   private static CSharpAnalyzerTest<TransactionRefusedStatementAnalyzer, DefaultVerifier> BuildTest(string[] sources)
   {
      var test = new CSharpAnalyzerTest<TransactionRefusedStatementAnalyzer, DefaultVerifier>
      {
         TestState =
         {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
         }
      };

      foreach (var source in sources)
         test.TestState.Sources.Add(source);

      return test;
   }

   private static async Task<MetadataReference> CompileStubToMetadataAsync()
   {
      var references = await ReferenceAssemblies.Net.Net90.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
      var compilation = CSharpCompilation.Create(
         "mvdmio.Database.PgSQL.Stub",
         [CSharpSyntaxTree.ParseText(IDBMIGRATION_STUB)],
         references,
         new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
      );

      using var stream = new MemoryStream();
      var result = compilation.Emit(stream);
      Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

      return MetadataReference.CreateFromImage(stream.ToArray());
   }
}
