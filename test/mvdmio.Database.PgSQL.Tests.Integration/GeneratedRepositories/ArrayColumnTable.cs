using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

[Table("public.generated_array_columns")]
public partial class ArrayColumnTable
{
   [PrimaryKey]
   [Generated]
   public long ArrayColumnId { get; private set; }

   public string[] Locales { get; set; } = [];

   public string[]? Tags { get; set; }

   public int[] Numbers { get; set; } = [];
}
