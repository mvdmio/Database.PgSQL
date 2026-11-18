using mvdmio.Database.PgSQL.Attributes;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

public enum WorkState
{
   Open,
   InProgress,
   Closed
}

[Table("public.generated_storage_claims")]
public partial class StorageClaimTable
{
   [PrimaryKey]
   [Generated]
   public long ClaimId { get; private set; }

   [Generated]
   public DateTime CreatedAt { get; private set; }

   public WorkState State { get; set; }

   [Column(StoredAs = NpgsqlDbType.Text)]
   public WorkState Phase { get; set; }

   [Column(StoredAs = NpgsqlDbType.Integer)]
   public WorkState Priority { get; set; }

   [Column(StoredAs = NpgsqlDbType.Smallint)]
   public WorkState Severity { get; set; }

   [Column(StoredAs = NpgsqlDbType.Bigint)]
   public WorkState Epoch { get; set; }

   public WorkState? ReviewState { get; set; }

   [Column(StoredAs = NpgsqlDbType.Integer)]
   public WorkState? ReviewPriority { get; set; }

   [Column(StoredAs = NpgsqlDbType.Jsonb)]
   public required string Document { get; init; }

   [Column(StoredAs = NpgsqlDbType.Json)]
   public string? Draft { get; set; }

   public string LegacyDocument { get; set; } = string.Empty;

   [Column(StoredAs = NpgsqlDbType.Text)]
   public string PlainNote { get; set; } = string.Empty;

   public sbyte OffsetHours { get; set; }

   [Column(StoredAs = NpgsqlDbType.Smallint)]
   public sbyte OffsetClaimed { get; set; }

   public sbyte? OptionalOffset { get; set; }

   public Dictionary<string, string>? Metadata { get; set; }

   [Column(StoredAs = NpgsqlDbType.Jsonb)]
   public Dictionary<string, string>? ClaimedMetadata { get; set; }

   [Column(StoredAs = NpgsqlDbType.Json)]
   public Dictionary<string, string>? JsonMetadata { get; set; }
}
