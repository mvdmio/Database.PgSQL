using System.ComponentModel.DataAnnotations.Schema;
using mvdmio.Database.PgSQL;

namespace ExampleProject;

[Table("example")]
public class ExampleTable : DbTable
{
   [Column("id")]
   public long Id { get; set; }
}