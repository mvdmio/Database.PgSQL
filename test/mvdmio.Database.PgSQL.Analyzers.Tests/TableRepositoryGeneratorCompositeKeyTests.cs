using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

public class TableRepositoryGeneratorCompositeKeyTests
{
   private const string COMPOSITE_KEY_TABLES = """
      using mvdmio.Database.PgSQL.Attributes;
      using mvdmio.Database.PgSQL.Relations;
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

         private List<TasksRelation> Tasks { get; set; } = new();

         private class TasksRelation : RelationDefinition<ProjectTable, TaskTable>
         {
            public override IReadOnlyList<RelationKey> Keys => [
               Key(x => x.AccountId, y => y.AccountId),
               Key(x => x.ProjectId, y => y.ProjectId),
            ];
         }
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

         private ProjectRelation? Project { get; set; }

         private class ProjectRelation : RelationDefinition<TaskTable, ProjectTable>
         {
            public override IReadOnlyList<RelationKey> Keys => [
               Key(x => x.AccountId, y => y.AccountId),
               Key(x => x.ProjectId, y => y.ProjectId),
            ];
         }
      }
      """;

   [Fact]
   public void CompositeKeyTable_GeneratesEveryTypeJustAsASingleColumnKeyDoes()
   {
      var result = GeneratorHarness.RunGenerator(COMPOSITE_KEY_TABLES);

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
      var result = GeneratorHarness.RunGenerator(COMPOSITE_KEY_TABLES);
      var project = GeneratorHarness.GeneratedSource(result, "Demo_ProjectTable.Repository.g.cs");

      project.Should().Contain("Task<ProjectData?> GetByPrimaryKeyAsync(long accountId, long projectId, CancellationToken ct = default);");
      project.Should().Contain("Task<bool> DeleteByPrimaryKeyAsync(long accountId, long projectId, CancellationToken ct = default);");

      project.Should().Contain("""WHERE "account_id" = :accountId AND "project_id" = :projectId""");
   }

   [Fact]
   public void CompositeKeyTable_AddressesEveryKeyMemberInTheUpdateAndTheDelete()
   {
      var result = GeneratorHarness.RunGenerator(COMPOSITE_KEY_TABLES);
      var task = GeneratorHarness.GeneratedSource(result, "Demo_TaskTable.Repository.g.cs");

      task.Should().Contain("""UPDATE "public"."tasks" """.TrimEnd());
      task.Should().Contain("""WHERE "account_id" = :AccountId AND "task_id" = :TaskId""");
      task.Should().Contain("""DELETE FROM "public"."tasks" """.TrimEnd());

      task.Should().Contain("public partial class UpdateTaskCommand");
      task.Should().ContainAll("[\"AccountId\"] = data.AccountId", "[\"TaskId\"] = data.TaskId");
   }

   [Fact]
   public void CompositeKeyTable_ExcludesADatabaseComputedKeyMemberFromTheCreateCommand()
   {
      var result = GeneratorHarness.RunGenerator(COMPOSITE_KEY_TABLES);
      var project = GeneratorHarness.GeneratedSource(result, "Demo_ProjectTable.Repository.g.cs");

      project.Should().Contain("""INSERT INTO "public"."projects" ("account_id", "name")""");
      project.Should().Contain("""RETURNING "account_id" AS "AccountId", "project_id" AS "ProjectId", "name" AS "Name" """.TrimEnd());
   }

   [Fact]
   public void CompositeKeyTable_RegistersEveryKeyMemberAsAPrimaryKeyColumn()
   {
      var registration = GeneratorHarness.RegistrationSource(GeneratorHarness.RunGenerator(COMPOSITE_KEY_TABLES));

      registration.Should().ContainAll(
         """.Column(x => x.AccountId, "account_id", isPrimaryKey: true)""",
         """.Column(x => x.ProjectId, "project_id", isPrimaryKey: true)""",
         """.Column(x => x.TaskId, "task_id", isPrimaryKey: true)"""
      );

      registration.Should().Contain(""".Column(x => x.ProjectId, "project_id", isNotNull: true)""");
   }

   [Fact]
   public void CompositeRelation_IsRegisteredThroughThePredicateOverload()
   {
      var registration = GeneratorHarness.RegistrationSource(GeneratorHarness.RunGenerator(COMPOSITE_KEY_TABLES));

      registration.Should().Contain(
         ".Relation<global::Demo.TaskData>(x => x.Tasks, (x, y) => x.AccountId == y.AccountId && x.ProjectId == y.ProjectId)"
      );

      registration.Should().Contain(
         ".Relation<global::Demo.ProjectData>(x => x.Project, (x, y) => x.AccountId == y.AccountId && x.ProjectId == y.ProjectId)"
      );
   }

   [Fact]
   public void Relation_IsNeverRegisteredThroughAKeyExpression()
   {
      var registration = GeneratorHarness.RegistrationSource(GeneratorHarness.RunGenerator(COMPOSITE_KEY_TABLES));

      registration.Should().NotContainAny("x => new", "y => new", "ValueTuple", "System.Tuple");

      registration.Should().NotContain(".Relation<global::Demo.TaskData, ");
      registration.Should().NotContain(".Relation<global::Demo.ProjectData, ");
   }

   [Fact]
   public void CompositeRelations_ProduceCodeThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile(COMPOSITE_KEY_TABLES);
   }

   [Fact]
   public void CompositeRelations_MirrorTheRelationPropertiesOntoTheDataTypes()
   {
      var result = GeneratorHarness.RunGenerator(COMPOSITE_KEY_TABLES);

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
}
