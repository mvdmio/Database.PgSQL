using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

[Table("public.generated_users")]
public partial class UserTable
{
   [PrimaryKey]
   [Generated]
   public long UserId { get; set; }

   [Unique]
   public string UserName { get; set; } = string.Empty;

   [Column("first_name")]
   public string FirstName { get; set; } = string.Empty;
}
