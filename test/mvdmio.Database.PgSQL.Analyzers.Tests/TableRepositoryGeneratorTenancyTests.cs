using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

/// <summary>
///    What a table definition's <c>[Column(Tenancy = true)]</c> claim buys across the whole generated surface:
///    <c>Query</c>, <c>GetAllAsync</c>, every <c>GetBy{Unique}Async</c> and <c>DeleteBy{Unique}Async</c>,
///    <c>GetByPrimaryKeyAsync</c>/<c>DeleteByPrimaryKeyAsync</c>, and the two command types the write path takes.
///    Pinned alongside the composite-key, nullability and storage classes that cover the other three <c>[Column]</c>
///    claims the same way.
/// </summary>
public class TableRepositoryGeneratorTenancyTests
{
   /// <summary>The tenancy column is part of the primary key, which is the shape a driving multi-tenant schema has.</summary>
   private const string TENANCY_INSIDE_KEY = """
      using mvdmio.Database.PgSQL.Attributes;

      namespace Demo;

      [Table("public.projects")]
      public partial class ProjectTable
      {
         [Column(Tenancy = true)]
         [PrimaryKey]
         public long AccountId { get; set; }

         [PrimaryKey]
         [Generated]
         public long ProjectId { get; set; }

         [Unique]
         public string Code { get; set; } = string.Empty;

         public string Name { get; set; } = string.Empty;
      }
      """;

   /// <summary>The tenancy column sits outside the surrogate key, which is the common shape elsewhere.</summary>
   private const string TENANCY_OUTSIDE_KEY = """
      using mvdmio.Database.PgSQL.Attributes;

      namespace Demo;

      [Table("public.rows")]
      public partial class RowTable
      {
         [PrimaryKey]
         [Generated]
         public long RowId { get; set; }

         [Column(Tenancy = true)]
         public long AccountId { get; set; }

         [Unique]
         public string Code { get; set; } = string.Empty;

         public string Name { get; set; } = string.Empty;
      }
      """;

   /// <summary>Two tenancy columns, declared account-then-workspace, so the two-level-tenancy case is exercised.</summary>
   private const string TWO_TENANCY_COLUMNS = """
      using mvdmio.Database.PgSQL.Attributes;

      namespace Demo;

      [Table("public.rows")]
      public partial class RowTable
      {
         [PrimaryKey]
         [Generated]
         public long RowId { get; set; }

         [Column(Tenancy = true)]
         public long AccountId { get; set; }

         [Column(Tenancy = true)]
         public long WorkspaceId { get; set; }

         [Unique]
         public string Code { get; set; } = string.Empty;

         public string Name { get; set; } = string.Empty;
      }
      """;

   /// <summary>The tenancy column also carries <c>[Unique]</c>, so its own lookup and delete take that value once.</summary>
   private const string TENANCY_COLUMN_IS_UNIQUE = """
      using mvdmio.Database.PgSQL.Attributes;

      namespace Demo;

      [Table("public.rows")]
      public partial class RowTable
      {
         [PrimaryKey]
         [Generated]
         public long RowId { get; set; }

         [Column(Tenancy = true)]
         [Unique]
         public long AccountId { get; set; }

         public string Name { get; set; } = string.Empty;
      }
      """;

   /// <summary>A table whose only assignable column besides the key is its tenancy column, outside the key.</summary>
   private const string TENANCY_ONLY_ASSIGNABLE_COLUMN = """
      using mvdmio.Database.PgSQL.Attributes;

      namespace Demo;

      [Table("public.rows")]
      public partial class RowTable
      {
         [PrimaryKey]
         [Generated]
         public long RowId { get; set; }

         [Column(Tenancy = true)]
         public long AccountId { get; set; }
      }
      """;

   /// <summary>A table declaring no tenancy column at all, for the compatibility assertion.</summary>
   private const string UNTENANTED = """
      using mvdmio.Database.PgSQL.Attributes;

      namespace Demo;

      [Table("public.rows")]
      public partial class RowTable
      {
         [PrimaryKey]
         [Generated]
         public long RowId { get; set; }

         public long AccountId { get; set; }

         public string Name { get; set; } = string.Empty;
      }
      """;

   [Fact]
   public void TenancyColumnInsideTheKey_AddsAParameterToQueryAndGetAll()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_INSIDE_KEY));

      repository.Should().Contain("Task<IEnumerable<ProjectData>> GetAllAsync(long accountId, CancellationToken ct = default);");
      repository.Should().Contain("IQueryable<ProjectData> Query(long accountId, TimeSpan? commandTimeout = null);");

      repository.Should().Contain("public async Task<IEnumerable<ProjectData>> GetAllAsync(long accountId, CancellationToken ct = default)");
      repository.Should().Contain("public IQueryable<ProjectData> Query(long accountId, TimeSpan? commandTimeout = null)");
   }

   [Fact]
   public void TenancyColumnOutsideTheKey_AddsAParameterToQueryAndGetAll()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_OUTSIDE_KEY));

      repository.Should().Contain("Task<IEnumerable<RowData>> GetAllAsync(long accountId, CancellationToken ct = default);");
      repository.Should().Contain("IQueryable<RowData> Query(long accountId, TimeSpan? commandTimeout = null);");
   }

   [Fact]
   public void Query_ReturnsAQueryableAlreadyNarrowedToTheTenant()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_INSIDE_KEY));

      repository.Should().Contain("return _db.Linq.Query<ProjectData>(commandTimeout).Where(x => x.AccountId == accountId);");
   }

   [Fact]
   public void Query_StillTakesCommandTimeoutLastAsAnOptionalParameter()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TWO_TENANCY_COLUMNS));

      repository.Should().Contain("IQueryable<RowData> Query(long accountId, long workspaceId, TimeSpan? commandTimeout = null);");
   }

   [Fact]
   public void GetAllAsync_ConstrainsTheTenancyColumnInItsWhereClause_AndBindsItByParameter()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_OUTSIDE_KEY));

      repository.Should().Contain("""WHERE "account_id" = :accountId""");
      repository.Should().Contain("""["accountId"] = accountId,""");
   }

   [Fact]
   public void TwoTenancyColumns_AreBothConstrainedInDeclarationOrder()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TWO_TENANCY_COLUMNS));

      repository.Should().Contain("""WHERE "account_id" = :accountId AND "workspace_id" = :workspaceId""");
      repository.Should().Contain("return _db.Linq.Query<RowData>(commandTimeout).Where(x => x.AccountId == accountId && x.WorkspaceId == workspaceId);");

      repository.Should().Contain("Task<IEnumerable<RowData>> GetAllAsync(long accountId, long workspaceId, CancellationToken ct = default);");
   }

   [Fact]
   public void TenancyColumn_StaysAnOrdinaryColumnEverywhereElse()
   {
      var result = GeneratorHarness.RunGenerator(TENANCY_INSIDE_KEY);
      var repository = GeneratorHarness.RepositorySource(result);
      var registration = GeneratorHarness.RegistrationSource(result);

      // Still in the select and returning lists.
      repository.Should().Contain("""SELECT "account_id" AS "AccountId", "project_id" AS "ProjectId", "code" AS "Code", "name" AS "Name" """.TrimEnd());
      repository.Should().Contain("""RETURNING "account_id" AS "AccountId", "project_id" AS "ProjectId", "code" AS "Code", "name" AS "Name" """.TrimEnd());

      // Still a property on the generated data type.
      repository.Should().Contain("public long AccountId { get; set; }");

      // Still registered as an ordinary column on the query surface — a key member here, which is what it also claims.
      registration.Should().Contain(""".Column(x => x.AccountId, "account_id", isPrimaryKey: true)""");
   }

   [Fact]
   public void UntenantedTable_GeneratesExactlyWhatItGeneratesToday()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(UNTENANTED));

      repository.Should().Contain("Task<IEnumerable<RowData>> GetAllAsync(CancellationToken ct = default);");
      repository.Should().Contain("IQueryable<RowData> Query(TimeSpan? commandTimeout = null);");
      repository.Should().Contain("return _db.Linq.Query<RowData>(commandTimeout);");

      repository.Should().NotContain("GetAllAsync(long accountId");
      repository.Should().NotContain("Query(long accountId");
   }

   [Fact]
   public void EveryTenancyShape_EmitsSourceThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile(TENANCY_INSIDE_KEY);
      GeneratorHarness.AssertGeneratedSourcesCompile(TENANCY_OUTSIDE_KEY);
      GeneratorHarness.AssertGeneratedSourcesCompile(TWO_TENANCY_COLUMNS);
      GeneratorHarness.AssertGeneratedSourcesCompile(TENANCY_COLUMN_IS_UNIQUE);
      GeneratorHarness.AssertGeneratedSourcesCompile(UNTENANTED);
   }

   [Fact]
   public void EveryTenancyShape_ReportsNothing()
   {
      GeneratorHarness.RunGenerator(TENANCY_INSIDE_KEY).Diagnostics.Should().BeEmpty();
      GeneratorHarness.RunGenerator(TENANCY_OUTSIDE_KEY).Diagnostics.Should().BeEmpty();
      GeneratorHarness.RunGenerator(TWO_TENANCY_COLUMNS).Diagnostics.Should().BeEmpty();
      GeneratorHarness.RunGenerator(TENANCY_COLUMN_IS_UNIQUE).Diagnostics.Should().BeEmpty();
   }

   [Fact]
   public void GetByPrimaryKeyAsync_AndDeleteByPrimaryKeyAsync_AreUnchanged_WhenTheTenancyColumnIsAlreadyAKeyMember()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_INSIDE_KEY));

      repository.Should().Contain("Task<ProjectData?> GetByPrimaryKeyAsync(long accountId, long projectId, CancellationToken ct = default);");
      repository.Should().Contain("Task<bool> DeleteByPrimaryKeyAsync(long accountId, long projectId, CancellationToken ct = default);");
      repository.Should().Contain("public async Task<ProjectData?> GetByPrimaryKeyAsync(long accountId, long projectId, CancellationToken ct = default)");
      repository.Should().Contain("public async Task<bool> DeleteByPrimaryKeyAsync(long accountId, long projectId, CancellationToken ct = default)");

      // No duplicated predicate: the key predicate alone already constrains the tenant.
      repository.Should().Contain("""WHERE "account_id" = :accountId AND "project_id" = :projectId""");
   }

   [Fact]
   public void GetByPrimaryKeyAsync_AndDeleteByPrimaryKeyAsync_GainATenancyParameter_WhenItIsOutsideTheKey()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_OUTSIDE_KEY));

      repository.Should().Contain("Task<RowData?> GetByPrimaryKeyAsync(long accountId, long rowId, CancellationToken ct = default);");
      repository.Should().Contain("Task<bool> DeleteByPrimaryKeyAsync(long accountId, long rowId, CancellationToken ct = default);");
      repository.Should().Contain("public async Task<RowData?> GetByPrimaryKeyAsync(long accountId, long rowId, CancellationToken ct = default)");
      repository.Should().Contain("public async Task<bool> DeleteByPrimaryKeyAsync(long accountId, long rowId, CancellationToken ct = default)");

      repository.Should().Contain("""WHERE "row_id" = :rowId AND "account_id" = :accountId""");
      repository.Should().Contain("""["rowId"] = rowId,""");
      repository.Should().Contain("""["accountId"] = accountId,""");
   }

   [Fact]
   public void GetByPrimaryKeyAsync_ConstrainsBothTenancyColumns_WhenBothSitOutsideTheKey()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TWO_TENANCY_COLUMNS));

      repository.Should().Contain("Task<RowData?> GetByPrimaryKeyAsync(long accountId, long workspaceId, long rowId, CancellationToken ct = default);");
      repository.Should().Contain("""WHERE "row_id" = :rowId AND "account_id" = :accountId AND "workspace_id" = :workspaceId""");
   }

   [Fact]
   public void GetByUniqueAsync_AndDeleteByUniqueAsync_GainATenancyParameter_TenancyInsideTheKey()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_INSIDE_KEY));

      repository.Should().Contain("Task<ProjectData?> GetByCodeAsync(long accountId, string code, CancellationToken ct = default);");
      repository.Should().Contain("Task<bool> DeleteByCodeAsync(long accountId, string code, CancellationToken ct = default);");

      repository.Should().Contain("""WHERE "code" = :code AND "account_id" = :accountId""");
   }

   [Fact]
   public void GetByUniqueAsync_AndDeleteByUniqueAsync_GainATenancyParameter_TenancyOutsideTheKey()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_OUTSIDE_KEY));

      repository.Should().Contain("Task<RowData?> GetByCodeAsync(long accountId, string code, CancellationToken ct = default);");
      repository.Should().Contain("Task<bool> DeleteByCodeAsync(long accountId, string code, CancellationToken ct = default);");

      repository.Should().Contain("""WHERE "code" = :code AND "account_id" = :accountId""");
   }

   [Fact]
   public void GetByUniqueAsync_ConstrainsBothTenancyColumns_InDeclarationOrder()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TWO_TENANCY_COLUMNS));

      repository.Should().Contain("Task<RowData?> GetByCodeAsync(long accountId, long workspaceId, string code, CancellationToken ct = default);");
      repository.Should().Contain("""WHERE "code" = :code AND "account_id" = :accountId AND "workspace_id" = :workspaceId""");
   }

   [Fact]
   public void GetByUniqueAsync_AndDeleteByUniqueAsync_TakeTheTenancyColumnsOwnValueOnce_WhenItIsTheUniqueColumn()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_COLUMN_IS_UNIQUE));

      repository.Should().Contain("Task<RowData?> GetByAccountIdAsync(long accountId, CancellationToken ct = default);");
      repository.Should().Contain("Task<bool> DeleteByAccountIdAsync(long accountId, CancellationToken ct = default);");

      repository.Should().Contain("""WHERE "account_id" = :accountId""");
      repository.Should().NotContain("""WHERE "account_id" = :accountId AND""");
   }

   [Fact]
   public void GetByPrimaryKeyAsync_TakesTheTenancyColumnsOwnValueOnce_WhenItIsAlsoTheUniqueColumn()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_COLUMN_IS_UNIQUE));

      repository.Should().Contain("Task<RowData?> GetByPrimaryKeyAsync(long accountId, long rowId, CancellationToken ct = default);");
   }

   [Fact]
   public void UntenantedTable_GetByAndDeleteByAndPrimaryKeyMembers_AreUnchanged()
   {
      const string UNTENANTED_WITH_UNIQUE = """
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.rows")]
         public partial class RowTable
         {
            [PrimaryKey]
            [Generated]
            public long RowId { get; set; }

            [Unique]
            public string Code { get; set; } = string.Empty;

            public long AccountId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """;

      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(UNTENANTED_WITH_UNIQUE));

      repository.Should().Contain("Task<RowData?> GetByPrimaryKeyAsync(long rowId, CancellationToken ct = default);");
      repository.Should().Contain("Task<bool> DeleteByPrimaryKeyAsync(long rowId, CancellationToken ct = default);");
      repository.Should().Contain("Task<RowData?> GetByCodeAsync(string code, CancellationToken ct = default);");
      repository.Should().Contain("Task<bool> DeleteByCodeAsync(string code, CancellationToken ct = default);");
   }

   [Fact]
   public void CreateAndUpdateCommandTypes_MakeTheTenancyColumnRequired_AndTheDataTypeDoesNot()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_OUTSIDE_KEY));

      // Asserted against each type's own body: all three carry the column, and an assertion over the whole file would
      // pass on one command type making it required and the other not.
      TypeBody(repository, "CreateRowCommand").Should().Contain("public required long AccountId { get; set; }");
      TypeBody(repository, "UpdateRowCommand").Should().Contain("public required long AccountId { get; set; }");

      // The data type still materializes the column through a parameterless constructor, which cannot satisfy
      // required, so it stays plain.
      var dataType = TypeBody(repository, "RowData");
      dataType.Should().Contain("public long AccountId { get; set; }");
      dataType.Should().NotContain("required");
   }

   [Fact]
   public void CreateAsyncAndUpdateAsync_KeepTheirSingleCommandParameter_OnEitherShape()
   {
      // The two rows of the member table that gain nothing. The tenant reaches the write path as a required property
      // on the command type, so both signatures read exactly as they do on a table declaring no tenancy column.
      var insideKey = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_INSIDE_KEY));

      insideKey.Should().Contain("Task<ProjectData> CreateAsync(CreateProjectCommand data, CancellationToken ct = default);");
      insideKey.Should().Contain("Task<ProjectData> UpdateAsync(UpdateProjectCommand data, CancellationToken ct = default);");

      var outsideKey = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_OUTSIDE_KEY));

      outsideKey.Should().Contain("Task<RowData> CreateAsync(CreateRowCommand data, CancellationToken ct = default);");
      outsideKey.Should().Contain("Task<RowData> UpdateAsync(UpdateRowCommand data, CancellationToken ct = default);");
   }

   /// <summary>
   ///    The body of one emitted type, so an assertion about a property lands on the type declaring it. The tenancy
   ///    column appears on the data type and both command types, and only the command types make it <c>required</c>.
   /// </summary>
   private static string TypeBody(string source, string typeName)
   {
      var normalized = source.Replace("\r\n", "\n");
      var declaration = $"public partial class {typeName}\n{{\n";
      var start = normalized.IndexOf(declaration, StringComparison.Ordinal);

      start.Should().BeGreaterThanOrEqualTo(0, "the generated source declares {0}", typeName);

      var bodyStart = start + declaration.Length;
      var bodyEnd = normalized.IndexOf("\n}", bodyStart, StringComparison.Ordinal);

      return normalized.Substring(bodyStart, bodyEnd - bodyStart);
   }

   [Fact]
   public void CreateAsync_StillInsertsTheTenancyColumnLikeAnyOtherColumn()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_OUTSIDE_KEY));

      repository.Should().Contain("""INSERT INTO "public"."rows" ("account_id", "code", "name")""");
   }

   [Fact]
   public void UpdateStatement_ConstrainsTheTenancyColumnInWhere_AndExcludesItFromSet_WhenOutsideTheKey()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_OUTSIDE_KEY));

      repository.Should().Contain("""WHERE "row_id" = :RowId AND "account_id" = :AccountId""");
      repository.Should().Contain("""SET "code" = :Code, "name" = :Name""");
      repository.Should().NotContain("""SET "account_id" = :AccountId""");
   }

   [Fact]
   public void UpdateStatement_IsByteForByteUnchanged_WhenTheTenancyColumnIsAlreadyAKeyMember()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(TENANCY_INSIDE_KEY));

      // No extra tenancy predicate joins the key predicate: the row is already tenant-scoped by construction.
      repository.Should().Contain("""WHERE "account_id" = :AccountId AND "project_id" = :ProjectId""");
      repository.Should().Contain("""SET "code" = :Code, "name" = :Name""");
   }

   [Fact]
   public void TenancyColumn_AsTheOnlyAssignableColumn_ReportsPGSQL0007_AndGeneratesNothing()
   {
      // The tenancy column is excluded from what an update assigns, so a table whose only non-key, non-generated
      // column is its tenancy column has nothing left to assign — the pre-existing "no updatable columns" refusal,
      // reported against the table even though the tenancy declaration is what caused it.
      var result = GeneratorHarness.RunGenerator(TENANCY_ONLY_ASSIGNABLE_COLUMN);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0007");
      result.GeneratedSources.Should().BeEmpty();
   }

   [Fact]
   public void NullableTenancyColumn_FromANullableType_ReportsPGSQL0025_AndAbandonsTheTable()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.rows")]
         public partial class RowTable
         {
            [PrimaryKey]
            [Generated]
            public long RowId { get; set; }

            [Column(Tenancy = true)]
            public long? AccountId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0025").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
      diagnostic.GetMessage().Should().Contain("AccountId");
      result.GeneratedSources.Should().BeEmpty();
   }

   [Fact]
   public void NullableTenancyColumn_FromANullClaim_ReportsPGSQL0025_AndAbandonsTheTable()
   {
      // A non-nullable reference type carrying Null = true is not a contradiction — [Column]'s claim overrides the
      // type — so the column is nullable through the claim alone, which the tenancy rule must catch the same way.
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.rows")]
         public partial class RowTable
         {
            [PrimaryKey]
            [Generated]
            public long RowId { get; set; }

            [Column(Tenancy = true, Null = true)]
            public string AccountId { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;
         }
         """);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0025").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
      diagnostic.GetMessage().Should().Contain("AccountId");
      result.GeneratedSources.Should().BeEmpty();
   }

   [Fact]
   public void GeneratedTenancyColumn_ReportsPGSQL0026_AndAbandonsTheTable()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.rows")]
         public partial class RowTable
         {
            [PrimaryKey]
            public long RowId { get; set; }

            [Column(Tenancy = true)]
            [Generated]
            public long AccountId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0026").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
      diagnostic.GetMessage().Should().Contain("AccountId");
      result.GeneratedSources.Should().BeEmpty();
   }

   [Fact]
   public void NullablePrimaryKeyThatIsAlsoATenancyColumn_ReportsOnlyPGSQL0020_NotPGSQL0025()
   {
      // Malformed two ways at once: a nullable key member that also carries Tenancy = true. The key rule already
      // abandons the table, so the tenancy rule must not pile on a second, competing diagnostic for the same property.
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.rows")]
         public partial class RowTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long? AccountId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0020");
      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0025");
      result.GeneratedSources.Should().BeEmpty();
   }

   [Fact]
   public void WellFormedTenancyColumns_ReportNeitherPGSQL0025NorPGSQL0026()
   {
      var insideKey = GeneratorHarness.RunGenerator(TENANCY_INSIDE_KEY);
      var outsideKey = GeneratorHarness.RunGenerator(TENANCY_OUTSIDE_KEY);

      insideKey.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0025" || x.Id == "PGSQL0026");
      outsideKey.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0025" || x.Id == "PGSQL0026");
   }

   // PGSQL0027 — a relation whose join could reach across tenants. RelationResolver is the only stage that sees two
   // tables at once, so every shape below declares both ends of the relation it exercises.

   [Fact]
   public void RelationToOneRow_PinnedByTheDeclaringTablesOwnTenancyColumn_ReportsNoWarning()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.accounts")]
         public partial class AccountTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            public string Name { get; set; } = string.Empty;
         }

         [Table("public.documents")]
         public partial class DocumentTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            [PrimaryKey]
            [Generated]
            public long DocumentId { get; set; }

            public string Title { get; set; } = string.Empty;

            [Relation(nameof(AccountId))]
            public AccountTable? Account { get; set; }
         }
         """);

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0027");
   }

   [Fact]
   public void RelationToOneRow_PairedAgainstAnUnrelatedProperty_ReportsPGSQL0027_TheStrictForm()
   {
      // The declaring table does carry a tenancy column, but the relation's foreign key is a different property
      // entirely. The loose reading — warn only when nothing at all is paired against the target's tenancy column —
      // would pass this, because something is paired. The strict form must warn anyway.
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.accounts")]
         public partial class AccountTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            public string Name { get; set; } = string.Empty;
         }

         [Table("public.documents")]
         public partial class DocumentTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            [PrimaryKey]
            [Generated]
            public long DocumentId { get; set; }

            public long OwnerId { get; set; }

            [Relation(nameof(OwnerId))]
            public AccountTable? Owner { get; set; }
         }
         """);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0027").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
      diagnostic.GetMessage().Should().Contain("Owner").And.Contain("AccountId");
   }

   [Fact]
   public void RelationToOneRow_DeclaringTableWithNoTenancyColumnAtAll_ReportsPGSQL0027()
   {
      // No property on the declaring table carries Tenancy = true, so whatever is paired against the target's
      // tenancy column falls out of the rule automatically: it cannot be the declaring table's own tenancy column.
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.accounts")]
         public partial class AccountTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            public string Name { get; set; } = string.Empty;
         }

         [Table("public.documents")]
         public partial class DocumentTable
         {
            [PrimaryKey]
            [Generated]
            public long DocumentId { get; set; }

            public long AccountId { get; set; }

            [Relation(nameof(AccountId))]
            public AccountTable? Account { get; set; }
         }
         """);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0027");
   }

   [Fact]
   public void RelationToAnUntenantedTarget_ReportsNoWarning()
   {
      // The target declares no tenancy column at all, so there is nothing to pin and nothing to warn about — a
      // relation to a shared, untenanted table is a legitimate shape.
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.categories")]
         public partial class CategoryTable
         {
            [PrimaryKey]
            [Generated]
            public long CategoryId { get; set; }

            public string Name { get; set; } = string.Empty;
         }

         [Table("public.documents")]
         public partial class DocumentTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            [PrimaryKey]
            [Generated]
            public long DocumentId { get; set; }

            public long? CategoryId { get; set; }

            [Relation(nameof(CategoryId))]
            public CategoryTable? Category { get; set; }
         }
         """);

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0027");
   }

   [Fact]
   public void RelationToMany_PinnedByTheTargetsOwnTenancyColumn_ReportsNoWarning()
   {
      // The sides swap for a relation to many: the foreign key lives on the target, so the check reads the target's
      // paired property against the declaring table's own tenancy column.
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.documents")]
         public partial class DocumentTable
         {
            [PrimaryKey]
            [Generated]
            public long DocumentId { get; set; }

            [Column(Tenancy = true)]
            public long AccountId { get; set; }

            public string Title { get; set; } = string.Empty;
         }

         [Table("public.accounts")]
         public partial class AccountTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            public string Name { get; set; } = string.Empty;

            [Relation(nameof(DocumentTable.AccountId))]
            public List<DocumentTable> Documents { get; set; } = new();
         }
         """);

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0027");
   }

   [Fact]
   public void RelationToMany_PairedAgainstAnUnrelatedProperty_ReportsPGSQL0027()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.documents")]
         public partial class DocumentTable
         {
            [PrimaryKey]
            [Generated]
            public long DocumentId { get; set; }

            public long OwnerAccountId { get; set; }

            public string Title { get; set; } = string.Empty;
         }

         [Table("public.accounts")]
         public partial class AccountTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            public string Name { get; set; } = string.Empty;

            [Relation(nameof(DocumentTable.OwnerAccountId))]
            public List<DocumentTable> Documents { get; set; } = new();
         }
         """);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0027").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
      diagnostic.GetMessage().Should().Contain("Documents").And.Contain("AccountId");
   }

   [Fact]
   public void RelationTenancyColumn_OutsideTheJoinedKey_ReportsExactlyOnePGSQL0027()
   {
      // The target's tenancy column is not part of the primary key the relation joins on, so nothing is paired
      // against it at all — which is the same failure as pairing the wrong property, and warns exactly once.
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.tenants")]
         public partial class TenantTable
         {
            [PrimaryKey]
            [Generated]
            public long TenantId { get; set; }

            [Column(Tenancy = true)]
            public long AccountId { get; set; }

            public string Name { get; set; } = string.Empty;
         }

         [Table("public.widgets")]
         public partial class WidgetTable
         {
            [PrimaryKey]
            [Generated]
            public long WidgetId { get; set; }

            public long TenantId { get; set; }

            [Relation(nameof(TenantId))]
            public TenantTable? Tenant { get; set; }
         }
         """);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0027").Subject;

      diagnostic.GetMessage().Should().Contain("Tenant").And.Contain("AccountId");
   }

   [Fact]
   public void RelationThatWarns_IsStillMirroredOntoTheDataTypeAndStillRegistered()
   {
      // PGSQL0027 drops nothing at all — not the relation, not the table — unlike every other relation diagnostic,
      // which drops the relation, and unlike PGSQL0025/26, which drop the table.
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.accounts")]
         public partial class AccountTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            public string Name { get; set; } = string.Empty;
         }

         [Table("public.documents")]
         public partial class DocumentTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            [PrimaryKey]
            [Generated]
            public long DocumentId { get; set; }

            public long OwnerId { get; set; }

            [Relation(nameof(OwnerId))]
            public AccountTable? Owner { get; set; }
         }
         """);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0027");

      var documentRelations = GeneratorHarness.GeneratedSource(result, "Demo_DocumentTable.Relations.g.cs");
      var registration = GeneratorHarness.RegistrationSource(result);

      documentRelations.Should().Contain("public global::Demo.AccountData? Owner { get; set; }");
      registration.Should().Contain("x => x.Owner");
   }

   [Fact]
   public void EveryPGSQL0027Shape_EmitsSourceThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.accounts")]
         public partial class AccountTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            public string Name { get; set; } = string.Empty;
         }

         [Table("public.documents")]
         public partial class DocumentTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            [PrimaryKey]
            [Generated]
            public long DocumentId { get; set; }

            public long OwnerId { get; set; }

            [Relation(nameof(OwnerId))]
            public AccountTable? Owner { get; set; }
         }
         """);
   }
}
