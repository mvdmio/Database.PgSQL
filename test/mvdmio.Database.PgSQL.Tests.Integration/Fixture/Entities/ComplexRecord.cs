using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.SourceGenerators.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture.Entities;

[Table("complex_table", schema: "public")]
public partial class ComplexRecord
{
   [Column("first_id", isPrimaryKey: true)]
   public long FirstId { get; init; }
  
   [Column("second_id", isPrimaryKey: true)]
   public long SecondId { get; init; }
}