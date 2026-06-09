using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;

namespace mvdmio.Database.PgSQL.Tests.Unit.Migrations;

/// <summary>
///    Tests that <see cref="ReflectionMigrationRetriever" /> only instantiates concrete migration types that
///    have a parameterless constructor, and never crashes on abstract or parameterized-constructor types.
/// </summary>
public class ReflectionMigrationRetrieverTests
{
   [Fact]
   public void RetrieveMigrations_IncludesConcreteParameterlessMigrations()
   {
      var retriever = new ReflectionMigrationRetriever(typeof(_202601020001_Valid).Assembly);

      var migrations = retriever.RetrieveMigrations().ToArray();

      migrations.Should().ContainSingle(m => m is _202601020001_Valid);
   }

   [Fact]
   public void RetrieveMigrations_SkipsAbstractAndParameterizedConstructorMigrations()
   {
      var retriever = new ReflectionMigrationRetriever(typeof(_202601020001_Valid).Assembly);

      // The retriever must not throw on AbstractMigration / ParameterizedMigration, and must not return them.
      var migrations = retriever.RetrieveMigrations().ToArray();

      migrations.Should().NotContain(m => m is ParameterizedMigration);
   }

#pragma warning disable PGSQL0001 // Names are intentional fixtures for this test.
   private sealed class _202601020001_Valid : IDbMigration
   {
      public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
   }

   private abstract class AbstractMigration : IDbMigration
   {
      public abstract Task UpAsync(DatabaseConnection db);
   }

   private sealed class ParameterizedMigration(string unused) : IDbMigration
   {
      public long Identifier => 1;
      public string Name => unused;
      public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
   }
#pragma warning restore PGSQL0001
}
