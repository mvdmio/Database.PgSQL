using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Attributes;

namespace ExampleProject;

[Table("example")]
public partial class ExampleTable : DbTable
{
   [Column("id")]
   public long Id { get; set; }
}