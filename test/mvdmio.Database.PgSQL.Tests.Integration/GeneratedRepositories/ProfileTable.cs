using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

[Table("public.generated_profiles")]
public partial class ProfileTable
{
   [PrimaryKey]
   [Generated]
   public long ProfileId { get; set; }

   [Unique]
   public string Handle { get; set; } = string.Empty;

   public string? Nickname { get; set; }

   public DateOnly BirthDate { get; set; }

   public TimeOnly WakeTime { get; set; }

   public Uri? HomePage { get; set; }

   public Dictionary<string, string>? Metadata { get; set; }
}
