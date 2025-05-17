using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Unit;

[Table("test")]
public partial class TestDbTable
{
   [Column("id")]
   public long Id { get; set; }

   [Column("first_name")]
   public required string FirstName { get; set; }

   [Column("last_name")]
   public required string LastName { get; set; }

   [Column("email")]
   public required string Email { get; set; }
}