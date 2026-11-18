using AwesomeAssertions;
using LinqToDB;
using mvdmio.Database.PgSQL.Connectors.Linq;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Tests.Unit.Connectors.Linq;

public class QueryStorageTypesTests
{
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
