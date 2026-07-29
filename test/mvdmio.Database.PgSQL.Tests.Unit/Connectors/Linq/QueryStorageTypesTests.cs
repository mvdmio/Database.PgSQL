using AwesomeAssertions;
using LinqToDB;
using mvdmio.Database.PgSQL.Connectors.Linq;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Tests.Unit.Connectors.Linq;

/// <summary>
///    The translation from a column's storage claim to what the query surface's provider understands.
/// </summary>
/// <remarks>
///    What this pins, and what it does not. It pins the library's table: an entry added, removed or repointed shows up
///    here. It cannot pin the analyzer's copy of which claims are representable — the analyzer is netstandard2.0 and
///    cannot reference this assembly — so a change made here and not there still leaves <c>PGSQL0024</c> warning about
///    the wrong set. Eliminating that mirror is deliberately out of scope; what stands against it is that the list below
///    is spelled out rather than derived, so changing the table means editing a list that names the analyzer's.
/// </remarks>
public class QueryStorageTypesTests
{
   /// <summary>
   ///    Every claim the analyzer's <c>ColumnStorage</c> treats as representable, spelled out here independently. Keep the
   ///    two in step: a claim added to one side and not the other fails this test.
   /// </summary>
   private static readonly NpgsqlDbType[] _representableClaims = [
      NpgsqlDbType.Bigint, NpgsqlDbType.Bit, NpgsqlDbType.Boolean, NpgsqlDbType.Bytea, NpgsqlDbType.Char,
      NpgsqlDbType.Date, NpgsqlDbType.Double, NpgsqlDbType.Integer, NpgsqlDbType.Interval, NpgsqlDbType.Json,
      NpgsqlDbType.Jsonb, NpgsqlDbType.Money, NpgsqlDbType.Numeric, NpgsqlDbType.Real, NpgsqlDbType.Smallint,
      NpgsqlDbType.Text, NpgsqlDbType.Time, NpgsqlDbType.TimeTz, NpgsqlDbType.Timestamp, NpgsqlDbType.TimestampTz,
      NpgsqlDbType.Uuid, NpgsqlDbType.Varbit, NpgsqlDbType.Varchar, NpgsqlDbType.Xml
   ];

   [Fact]
   public void Representable_IsExactlyWhatTheAnalyzerWarnsAboutTheAbsenceOf()
   {
      QueryStorageTypes.Representable.Should().BeEquivalentTo(_representableClaims);
   }

   [Theory]
   [InlineData(NpgsqlDbType.Text, DataType.Text)]
   [InlineData(NpgsqlDbType.Jsonb, DataType.BinaryJson)]
   [InlineData(NpgsqlDbType.Json, DataType.Json)]
   [InlineData(NpgsqlDbType.Smallint, DataType.Int16)]
   [InlineData(NpgsqlDbType.Integer, DataType.Int32)]
   [InlineData(NpgsqlDbType.Bigint, DataType.Int64)]
   public void DataTypeFor_GivenAnExercisedClaim_AnswersTheProvidersEquivalent(NpgsqlDbType storedAs, DataType expected)
   {
      QueryStorageTypes.DataTypeFor(storedAs).Should().Be(expected);
   }

   /// <summary>
   ///    A claim with no provider equivalent answers nothing rather than guessing. The claim still reaches the Dapper
   ///    surface; the build warns that the two diverge.
   /// </summary>
   [Theory]
   [InlineData(NpgsqlDbType.Inet)]
   [InlineData(NpgsqlDbType.Cidr)]
   [InlineData(NpgsqlDbType.Point)]
   [InlineData(NpgsqlDbType.Polygon)]
   [InlineData(NpgsqlDbType.MacAddr)]
   [InlineData(NpgsqlDbType.Hstore)]
   public void DataTypeFor_GivenAClaimTheProviderCannotRepresent_AnswersNothing(NpgsqlDbType storedAs)
   {
      QueryStorageTypes.DataTypeFor(storedAs).Should().BeNull();
   }
}
