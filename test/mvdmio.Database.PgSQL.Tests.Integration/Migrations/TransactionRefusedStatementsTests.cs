using AwesomeAssertions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using Npgsql;

namespace mvdmio.Database.PgSQL.Tests.Integration.Migrations;

public class TransactionRefusedStatementsTests : TestBase
{
   private const string REFUSED_MESSAGE = "cannot run inside a transaction block";

   private static readonly string[] _refusedStatements =
   [
      "CREATE INDEX CONCURRENTLY idx_refused ON public.refused_table (id)",
      "CREATE UNIQUE INDEX CONCURRENTLY idx_refused ON public.refused_table (id)",
      "DROP INDEX CONCURRENTLY idx_refused",
      "REINDEX TABLE CONCURRENTLY public.refused_table",
      "REINDEX (CONCURRENTLY) TABLE public.refused_table",
      "ALTER TABLE public.refused_parent DETACH PARTITION public.refused_child CONCURRENTLY",
      "VACUUM",
      "CREATE DATABASE refused_database",
      "DROP DATABASE refused_database",
      "CREATE TABLESPACE refused_tablespace LOCATION '/refused/location'",
      "DROP TABLESPACE refused_tablespace",
      "ALTER SYSTEM SET work_mem = '64MB'"
   ];

   public TransactionRefusedStatementsTests(TestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public async Task Statement_RefusedByTheAnalyzer_FailsInsideATransactionWithPostgresError()
   {
      await Db.Dapper.ExecuteAsync("CREATE TABLE public.refused_table (id INT NOT NULL)", ct: CancellationToken);
      await Db.Dapper.ExecuteAsync("CREATE INDEX idx_refused ON public.refused_table (id)", ct: CancellationToken);
      await Db.Dapper.ExecuteAsync("CREATE TABLE public.refused_parent (id INT NOT NULL) PARTITION BY RANGE (id)", ct: CancellationToken);
      await Db.Dapper.ExecuteAsync("CREATE TABLE public.refused_child PARTITION OF public.refused_parent FOR VALUES FROM (0) TO (100)", ct: CancellationToken);

      foreach (var statement in _refusedStatements)
      {
         await Db.Dapper.ExecuteAsync("SAVEPOINT before_refused", ct: CancellationToken);

         var act = async () => await Db.Dapper.ExecuteAsync(statement, ct: CancellationToken);

         var failure = (await act.Should().ThrowAsync<QueryException>($"'{statement}' must be refused inside a transaction")).Which;
         failure.InnerException.Should().BeOfType<PostgresException>().Which.MessageText.Should().Contain(REFUSED_MESSAGE, $"'{statement}' must be refused inside a transaction");

         await Db.Dapper.ExecuteAsync("ROLLBACK TO SAVEPOINT before_refused", ct: CancellationToken);
      }
   }

   [Fact]
   public async Task RefreshMaterializedViewConcurrently_RunsInsideATransaction()
   {
      await Db.Dapper.ExecuteAsync("CREATE TABLE public.order_lines (id INT NOT NULL, amount INT NOT NULL)", ct: CancellationToken);
      await Db.Dapper.ExecuteAsync("CREATE MATERIALIZED VIEW public.order_totals AS SELECT id, amount FROM public.order_lines", ct: CancellationToken);
      await Db.Dapper.ExecuteAsync("CREATE UNIQUE INDEX ux_order_totals_id ON public.order_totals (id)", ct: CancellationToken);

      await Db.Dapper.ExecuteAsync("REFRESH MATERIALIZED VIEW CONCURRENTLY public.order_totals", ct: CancellationToken);
   }
}
