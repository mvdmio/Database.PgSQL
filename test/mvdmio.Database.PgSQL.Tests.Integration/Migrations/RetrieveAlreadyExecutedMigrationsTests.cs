using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Models;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.Migrations;

public class RetrieveAlreadyExecutedMigrationsTests : TestBase
{
   public RetrieveAlreadyExecutedMigrationsTests(TestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public async Task RetrieveAlreadyExecutedMigrationsAsync_ReportsThePhaseOfEachRow_AndReadsNullPhaseAsPreDeploy()
   {
      await Db.Dapper.ExecuteAsync(
         """
         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at, scope, phase)
         VALUES (202601010001, 'NullPhase', NOW(), 'Retrieve.Tests', NULL),
                (202601010002, 'PreDeploy', NOW(), 'Retrieve.Tests', 'pre-deploy'),
                (202601010002, 'PostDeploy', NOW(), 'Retrieve.Tests', 'post-deploy')
         """,
         ct: CancellationToken);

      var rows = (await BuildMigrator().RetrieveAlreadyExecutedMigrationsAsync(CancellationToken))
         .Where(m => m.Scope == "Retrieve.Tests")
         .Select(m => (m.Identifier, m.Name, m.Phase))
         .OrderBy(m => m.Identifier).ThenBy(m => m.Name)
         .ToArray();

      rows.Should().Equal(
         (202601010001L, "NullPhase", DeployPhase.PreDeploy),
         (202601010002L, "PostDeploy", DeployPhase.PostDeploy),
         (202601010002L, "PreDeploy", DeployPhase.PreDeploy)
      );
   }

   [Fact]
   public async Task RetrieveAlreadyExecutedMigrationsAsync_OnTableWithoutPhaseColumn_ReadsRowsAsPreDeploy()
   {
      await Db.Dapper.ExecuteAsync(
         """
         DROP TABLE "mvdmio"."migrations";

         CREATE TABLE "mvdmio"."migrations" (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            scope       TEXT        NULL
         );

         CREATE UNIQUE INDEX migrations_scope_identifier_key ON "mvdmio"."migrations" (scope, identifier);

         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at, scope)
         VALUES (202601010001, 'Version045Row', NOW(), 'Retrieve.Tests');
         """,
         ct: CancellationToken);

      var rows = (await BuildMigrator().RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();

      rows.Should().ContainSingle().Which.Should().Match<ExecutedMigrationModel>(
         m => m.Identifier == 202601010001 && m.Name == "Version045Row" && m.Scope == "Retrieve.Tests" && m.Phase == DeployPhase.PreDeploy
      );
   }

   [Fact]
   public async Task RetrieveAlreadyExecutedMigrationsAsync_OnTableWithoutScopeColumn_ReadsRowsAsPreDeploy()
   {
      await Db.Dapper.ExecuteAsync(
         """
         DROP TABLE "mvdmio"."migrations";

         CREATE TABLE "mvdmio"."migrations" (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (identifier)
         );

         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at)
         VALUES (202601010001, 'LegacyRow', NOW());
         """,
         ct: CancellationToken);

      var rows = (await BuildMigrator().RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();

      rows.Should().ContainSingle().Which.Should().Match<ExecutedMigrationModel>(
         m => m.Identifier == 202601010001 && m.Name == "LegacyRow" && m.Scope == null && m.Phase == DeployPhase.PreDeploy
      );
   }

   private DatabaseMigrator BuildMigrator()
   {
      return new DatabaseMigrator(Db, NullLoggerFactory.Instance, typeof(TestFixture).Assembly);
   }
}
