using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.SourceGenerators.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture;

[Table("test_table", schema: "public")]
public partial class TestTable
{
  [Column("id", isPrimaryKey: true)]
  public long Id { get; init; }
  
  [Column("required_string_value")]
  public string RequiredStringValue { get; set; }
  
  [Column("optional_string_value")]
  public string? OptionalStringValue { get; set; }
}