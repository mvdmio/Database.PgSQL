using AwesomeAssertions;
using LinqToDB;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Exceptions;
using Npgsql;

namespace mvdmio.Database.PgSQL.Tests.Unit.Connectors.Linq;

public class QueryExceptionTranslatorTests
{
   [Fact]
   public void ShouldTranslate_ForACancellation_IsFalse()
   {
      QueryExceptionTranslator.ShouldTranslate(new OperationCanceledException()).Should().BeFalse();
      QueryExceptionTranslator.ShouldTranslate(new TaskCanceledException()).Should().BeFalse();
   }

   [Fact]
   public void ShouldTranslate_ForAFrameworkFailure_IsFalse()
   {
      QueryExceptionTranslator.ShouldTranslate(new InvalidOperationException("Sequence contains no elements")).Should().BeFalse();
      QueryExceptionTranslator.ShouldTranslate(new ObjectDisposedException("DatabaseConnection")).Should().BeFalse();
   }

   [Fact]
   public void Translate_ForAProviderFailureWithoutSql_ProducesATranslationException()
   {
      var exception = new LinqToDBException("The LINQ expression could not be converted to SQL.");

      QueryExceptionTranslator.ShouldTranslate(exception).Should().BeTrue();
      QueryExceptionTranslator.Translate(exception, () => null).Should().BeOfType<QueryTranslationException>();
   }

   [Fact]
   public void Translate_ForADatabaseFailure_ProducesAQueryExceptionCarryingTheSql()
   {
      var exception = new LinqToDBException("Failure", new PostgresException("boom", "ERROR", "ERROR", "42P01"));

      QueryExceptionTranslator.ShouldTranslate(exception).Should().BeTrue();

      var translated = QueryExceptionTranslator.Translate(exception, () => "SELECT 1");

      translated.Should().BeOfType<QueryException>();
      ((QueryException)translated).Sql.Should().Be("SELECT 1");
   }

   [Fact]
   public void Translate_ForADatabaseFailureWithoutCapturedSql_StillReportsNonNullSql()
   {
      var exception = new PostgresException("boom", "ERROR", "ERROR", "42P01");

      var translated = QueryExceptionTranslator.Translate(exception, () => null);

      ((QueryException)translated).Sql.Should().NotBeNullOrWhiteSpace();
   }
}
