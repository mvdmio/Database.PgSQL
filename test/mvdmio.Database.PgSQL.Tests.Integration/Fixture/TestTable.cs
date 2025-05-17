using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture;

[Table("test")]
public partial class TestTable : DbTable
{
  [Column("id")]
  public long Id { get; init; }
}