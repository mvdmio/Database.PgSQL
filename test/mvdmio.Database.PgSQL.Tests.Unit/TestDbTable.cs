using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.SourceGenerators.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Unit;

[Table("test_table", schema: "test_schema")]
public partial class TestDbTable
{
   [Column("id", isPrimaryKey: true)]
   public long Id { get; set; }

   [Column("first_name")]
   public required string FirstName { get; set; }

   [Column("last_name")]
   public required string LastName { get; set; }

   [Column("email")]
   public required string Email { get; set; }
}