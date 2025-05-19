using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.SourceGenerators.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture.Entities;

[Table("simple_table", "public")]
public partial class SimpleRecord
{
   [Column("id", true)]
   public long Id { get; init; }

   [Column("required_string_value")]
   public string RequiredStringValue { get; set; }

   [Column("optional_string_value")]
   public string? OptionalStringValue { get; set; }

   public SimpleRecord(long id, string requiredStringValue, string? optionalStringValue = null)
   {
      Id = id;
      RequiredStringValue = requiredStringValue;
      OptionalStringValue = optionalStringValue;
   }
}