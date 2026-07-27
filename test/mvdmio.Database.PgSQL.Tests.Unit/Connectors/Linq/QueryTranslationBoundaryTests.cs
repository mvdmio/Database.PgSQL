using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Exceptions;
using Npgsql;

namespace mvdmio.Database.PgSQL.Tests.Unit.Connectors.Linq;

public class QueryTranslationBoundaryTests
{
   [Fact]
   public void Execute_WhenTheFailedExecutionSentSql_ReportsThatSql()
   {
      var lastSql = "SELECT * FROM the_previous_query";
      var source = new LinqQuerySource(() => throw new NotSupportedException(), () => lastSql);

      var failure = Assert.Throws<QueryException>(
         () => QueryTranslationBoundary.Execute<int>(
            () =>
            {
               lastSql = "SELECT * FROM the_failing_query";

               throw new PostgresException("boom", "ERROR", "ERROR", "42P01");
            },
            source
         )
      );

      failure.Sql.Should().Be("SELECT * FROM the_failing_query");
   }

   [Fact]
   public void Execute_WhenTheFailureSentNoSqlAtAll_DoesNotReportThePreviousQuerysSql()
   {
      // A failure before the command was built — a dropped connection, say — leaves the previous query's SQL behind.
      var source = new LinqQuerySource(() => throw new NotSupportedException(), () => "SELECT * FROM the_previous_query");

      var failure = Assert.Throws<QueryException>(
         () => QueryTranslationBoundary.Execute<int>(
            () => throw new PostgresException("connection failure", "FATAL", "FATAL", "08006"),
            source
         )
      );

      failure.Sql.Should().NotContain("the_previous_query");
   }

   [Fact]
   public async Task ExecuteAsync_AppliesTheSameSqlAttribution()
   {
      var source = new LinqQuerySource(() => throw new NotSupportedException(), () => "SELECT * FROM the_previous_query");

      var failure = await Assert.ThrowsAsync<QueryException>(
         () => QueryTranslationBoundary.ExecuteAsync<int>(
            () => throw new PostgresException("connection failure", "FATAL", "FATAL", "08006"),
            source
         )
      );

      failure.Sql.Should().NotContain("the_previous_query");
   }

   [Fact]
   public void Execute_ForAFailureThisLibraryDoesNotOwn_LeavesItAlone()
   {
      var source = new LinqQuerySource(() => throw new NotSupportedException(), () => null);

      Assert.Throws<InvalidOperationException>(
         () => QueryTranslationBoundary.Execute<int>(() => throw new InvalidOperationException("Sequence contains no elements"), source)
      );
   }
}
