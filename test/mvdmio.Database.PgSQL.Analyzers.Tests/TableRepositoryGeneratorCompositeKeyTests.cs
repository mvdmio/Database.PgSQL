using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

/// <summary>
///    What a table definition whose primary key has more than one member generates, and the diagnostics that stop a
///    malformed one reaching run time.
/// </summary>
public class TableRepositoryGeneratorCompositeKeyTests
{
   /// <summary>
   ///    A tenant-scoped pair: both keys are two columns and both share <c>AccountId</c>, so a relation whose foreign key
   ///    overlaps the declaring table's own key is the ordinary case here rather than a special one. The project's second
   ///    key member is database-generated, which is what makes a part-supplied, part-generated key observable.
   /// </summary>
   private const string _COMPOSITE_KEY_TABLES = """
      using mvdmio.Database.PgSQL.Attributes;
      using System.Collections.Generic;

      namespace Demo;

      [Table("public.projects")]
      public partial class ProjectTable
      {
         [PrimaryKey]
         public long AccountId { get; set; }

         [PrimaryKey]
         [Generated]
         public long ProjectId { get; set; }

         public string Name { get; set; } = string.Empty;

         [Relation(nameof(TaskTable.AccountId), nameof(TaskTable.ProjectId))]
         public List<TaskTable> Tasks { get; set; } = new();
      }

      [Table("public.tasks")]
      public partial class TaskTable
      {
         [PrimaryKey]
         public long AccountId { get; set; }

         [PrimaryKey]
         public long TaskId { get; set; }

         public long ProjectId { get; set; }

         public string Title { get; set; } = string.Empty;

         [Relation(nameof(AccountId), nameof(ProjectId))]
         public ProjectTable? Project { get; set; }
      }
      """;

   [Fact]
   public void CompositeKeyTable_GeneratesEveryTypeJustAsASingleColumnKeyDoes()
   {
      var result = GeneratorHarness.RunGenerator(_COMPOSITE_KEY_TABLES);

      result.Diagnostics.Should().BeEmpty();

      var project = GeneratorHarness.GeneratedSource(result, "Demo_ProjectTable.Repository.g.cs");

      project.Should().ContainAll(
         "public partial class ProjectData",
         "public partial class CreateProjectCommand",
         "public partial class UpdateProjectCommand",
         "public partial interface IProjectRepository",
         "public partial class ProjectRepository : IProjectRepository",
         "IQueryable<ProjectData> Query(TimeSpan? commandTimeout = null);"
      );
   }

   [Fact]
   public void CompositeKeyTable_TakesOneLookupParameterPerKeyMemberInDeclarationOrder()
   {
      var result = GeneratorHarness.RunGenerator(_COMPOSITE_KEY_TABLES);
      var project = GeneratorHarness.GeneratedSource(result, "Demo_ProjectTable.Repository.g.cs");

      project.Should().Contain("Task<ProjectData?> GetByPrimaryKeyAsync(long accountId, long projectId, CancellationToken ct = default);");
      project.Should().Contain("Task<bool> DeleteByPrimaryKeyAsync(long accountId, long projectId, CancellationToken ct = default);");

      // Declaration order, not alphabetical and not column order: the file already states it.
      project.Should().Contain("""WHERE "account_id" = :accountId AND "project_id" = :projectId""");
   }

   [Fact]
   public void CompositeKeyTable_AddressesEveryKeyMemberInTheUpdateAndTheDelete()
   {
      var result = GeneratorHarness.RunGenerator(_COMPOSITE_KEY_TABLES);
      var task = GeneratorHarness.GeneratedSource(result, "Demo_TaskTable.Repository.g.cs");

      task.Should().Contain("""UPDATE "public"."tasks" """.TrimEnd());
      task.Should().Contain("""WHERE "account_id" = :AccountId AND "task_id" = :TaskId""");
      task.Should().Contain("""DELETE FROM "public"."tasks" """.TrimEnd());

      // The update command carries every key member, so the caller can name the row it means.
      task.Should().Contain("public partial class UpdateTaskCommand");
      task.Should().ContainAll("[\"AccountId\"] = data.AccountId", "[\"TaskId\"] = data.TaskId");
   }

   [Fact]
   public void CompositeKeyTable_ExcludesADatabaseComputedKeyMemberFromTheCreateCommand()
   {
      var result = GeneratorHarness.RunGenerator(_COMPOSITE_KEY_TABLES);
      var project = GeneratorHarness.GeneratedSource(result, "Demo_ProjectTable.Repository.g.cs");

      // A key that is part caller-supplied and part database-computed needs no special handling: the create command
      // already excludes generated columns per property.
      project.Should().Contain("""INSERT INTO "public"."projects" ("account_id", "name")""");
      project.Should().Contain("""RETURNING "account_id" AS "AccountId", "project_id" AS "ProjectId", "name" AS "Name" """.TrimEnd());
   }

   [Fact]
   public void CompositeKeyTable_RegistersEveryKeyMemberAsAPrimaryKeyColumn()
   {
      var registration = GeneratorHarness.RegistrationSource(GeneratorHarness.RunGenerator(_COMPOSITE_KEY_TABLES));

      registration.Should().ContainAll(
         """.Column(x => x.AccountId, "account_id", isPrimaryKey: true)""",
         """.Column(x => x.ProjectId, "project_id", isPrimaryKey: true)""",
         """.Column(x => x.TaskId, "task_id", isPrimaryKey: true)"""
      );

      // A non-key column stays a plain column even when it is a relation's foreign key — it only states that its type
      // cannot hold null, which every key member states through the key argument instead.
      registration.Should().Contain(""".Column(x => x.ProjectId, "project_id", isNotNull: true)""");
   }

   [Fact]
   public void CompositeRelation_IsRegisteredThroughThePredicateOverload()
   {
      var registration = GeneratorHarness.RegistrationSource(GeneratorHarness.RunGenerator(_COMPOSITE_KEY_TABLES));

      registration.Should().Contain(
         ".Relation<global::Demo.TaskData>(x => x.Tasks, (x, y) => x.AccountId == y.AccountId && x.ProjectId == y.ProjectId)"
      );

      registration.Should().Contain(
         ".Relation<global::Demo.ProjectData>(x => x.Project, (x, y) => x.AccountId == y.AccountId && x.ProjectId == y.ProjectId)"
      );
   }

   /// <remarks>
   ///    The defensive half of the pair above. The provider's key-based overloads leave their key type parameters
   ///    unconstrained, so an anonymous type compiles there, registers as a single key named after its constructor, and
   ///    fails only at the first query with a coercion error naming the two entity types. Nothing but a source assertion
   ///    can hold the generator off that shape.
   /// </remarks>
   [Fact]
   public void CompositeRelation_IsNeverRegisteredThroughAKeyExpression()
   {
      var registration = GeneratorHarness.RegistrationSource(GeneratorHarness.RunGenerator(_COMPOSITE_KEY_TABLES));

      registration.Should().NotContainAny("x => new", "y => new", "ValueTuple", "System.Tuple");

      // Every relation registered here is composite, so no three-argument key-based call may appear at all.
      registration.Should().NotContain(".Relation<global::Demo.TaskData, ");
      registration.Should().NotContain(".Relation<global::Demo.ProjectData, ");
   }

   [Fact]
   public void CompositeRelations_ProduceCodeThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile(_COMPOSITE_KEY_TABLES);
   }

   [Fact]
   public void CompositeRelations_MirrorTheRelationPropertiesOntoTheDataTypes()
   {
      var result = GeneratorHarness.RunGenerator(_COMPOSITE_KEY_TABLES);

      GeneratorHarness.GeneratedSource(result, "Demo_ProjectTable.Relations.g.cs")
         .Should().Contain("public global::System.Collections.Generic.List<global::Demo.TaskData> Tasks { get; set; } = new();");

      GeneratorHarness.GeneratedSource(result, "Demo_TaskTable.Relations.g.cs")
         .Should().Contain("public global::Demo.ProjectData? Project { get; set; }");
   }

   [Fact]
   public void TableWithNoPrimaryKeyProperty_ProducesDiagnosticAndAbandonsTheTable()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.projects")]
         public partial class ProjectTable
         {
            public long AccountId { get; set; }
            public string Name { get; set; } = string.Empty;
         }
         """);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0004").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
      diagnostic.GetMessage().Should().Contain("at least one");

      // A malformed key leaves every generated signature undefined, so the table is abandoned rather than half emitted.
      result.GeneratedSources.Should().BeEmpty();
   }

   [Theory]
   [InlineData("long?")]
   [InlineData("string?")]
   public void TableWithANullablePrimaryKeyProperty_ProducesDiagnosticAndAbandonsTheTable(string keyType)
   {
      var result = GeneratorHarness.RunGenerator($$"""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.projects")]
         public partial class ProjectTable
         {
            [PrimaryKey]
            public long AccountId { get; set; }

            [PrimaryKey]
            public {{keyType}} ProjectId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0020").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
      diagnostic.GetMessage().Should().ContainAll("ProjectId", keyType);
      result.GeneratedSources.Should().BeEmpty();
   }

   [Theory]
   [InlineData("[Relation(nameof(AccountId))]", 1, 2)]
   [InlineData("[Relation(nameof(AccountId), nameof(ProjectId), nameof(TaskId))]", 3, 2)]
   public void RelationNamingTheWrongNumberOfForeignKeys_ProducesDiagnosticWithoutAbandoningTheTable(
      string relationAttribute,
      int declaredArity,
      int keyArity
   )
   {
      var result = GeneratorHarness.RunGenerator(TaskSourceWithRelation($$"""
         {{relationAttribute}}
            public ProjectTable? Project { get; set; }
         """));

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0019").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
      diagnostic.GetMessage().Should().ContainAll($"arity {declaredArity}", $"arity {keyArity}", "ProjectTable");

      // A relation-level problem drops only the relation, so the diagnostic is not buried under type-not-found errors.
      result.GeneratedSources.Should().NotBeEmpty();
      GeneratorHarness.GeneratedSource(result, "Demo_TaskTable.Repository.g.cs").Should().Contain("public partial class TaskData");
      result.GeneratedSources.Should().NotContain(x => x.HintName == "Demo_TaskTable.Relations.g.cs");
   }

   [Fact]
   public void RelationWithAForeignKeyThatCannotMatchItsKeyMember_NamesThePositionAndBothProperties()
   {
      var result = GeneratorHarness.RunGenerator(TaskSourceWithRelation("""
         [Relation(nameof(AccountId), nameof(Title))]
            public ProjectTable? Project { get; set; }
         """));

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0013").Subject;

      diagnostic.GetMessage().Should().ContainAll("Title", "string", "ProjectId", "long", "at key position 2");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationNamingSeveralUnknownForeignKeys_ReportsEachOfThem()
   {
      var result = GeneratorHarness.RunGenerator(TaskSourceWithRelation("""
         [Relation("NoSuchAccount", "NoSuchProject")]
            public ProjectTable? Project { get; set; }
         """));

      var messages = result.Diagnostics.Where(x => x.Id == "PGSQL0012").Select(x => x.GetMessage()).ToList();

      messages.Should().HaveCount(2);
      messages.Should().Contain(x => x.Contains("NoSuchAccount", StringComparison.Ordinal));
      messages.Should().Contain(x => x.Contains("NoSuchProject", StringComparison.Ordinal));
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void UniqueColumnNamedAfterThePrimaryKeyLookup_ProducesDiagnostic()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.projects")]
         public partial class ProjectTable
         {
            [PrimaryKey]
            public long AccountId { get; set; }

            [Unique]
            public string PrimaryKey { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;
         }
         """);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0010").Subject;

      diagnostic.GetMessage().Should().ContainAll("GetByPrimaryKeyAsync", "the primary key's own lookup");
      result.GeneratedSources.Should().BeEmpty();
   }

   /// <summary>
   ///    The task table carrying whichever relation member the caller spells out, plus the composite-key project table it
   ///    points at.
   /// </summary>
   private static string TaskSourceWithRelation(string member)
   {
      return $$"""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.tasks")]
         public partial class TaskTable
         {
            [PrimaryKey]
            public long AccountId { get; set; }

            [PrimaryKey]
            public long TaskId { get; set; }

            public long ProjectId { get; set; }
            public string Title { get; set; } = string.Empty;

            {{member}}
         }

         [Table("public.projects")]
         public partial class ProjectTable
         {
            [PrimaryKey]
            public long AccountId { get; set; }

            [PrimaryKey]
            public long ProjectId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """;
   }
}
