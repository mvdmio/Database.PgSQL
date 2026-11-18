namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

internal static class StorageClaimRows
{
   public const string DOCUMENT = """{"kind": "invoice"}""";
   public const string DRAFT = """{"kind": "draft"}""";
   public const string LEGACY_DOCUMENT = """{"kind":"legacy"}""";
   public const string PLAIN_NOTE = "a plain note";
   public const sbyte OFFSET_HOURS = -3;

   public static CreateStorageClaimCommand Create(WorkState state, WorkState priority, WorkState? reviewState)
   {
      return new CreateStorageClaimCommand
      {
         State = state,
         Phase = state,
         Priority = priority,
         Severity = priority,
         Epoch = priority,
         ReviewState = reviewState,
         ReviewPriority = reviewState,
         Document = DOCUMENT,
         Draft = DRAFT,
         LegacyDocument = LEGACY_DOCUMENT,
         PlainNote = PLAIN_NOTE,
         OffsetHours = OFFSET_HOURS,
         OffsetClaimed = OFFSET_HOURS,
         OptionalOffset = null,
         Metadata = new Dictionary<string, string> { ["tier"] = "gold" },
         ClaimedMetadata = new Dictionary<string, string> { ["tier"] = "silver" },
         JsonMetadata = new Dictionary<string, string> { ["tier"] = "bronze" }
      };
   }

   public static UpdateStorageClaimCommand UpdateFrom(StorageClaimData row)
   {
      ArgumentNullException.ThrowIfNull(row);

      return new UpdateStorageClaimCommand
      {
         ClaimId = row.ClaimId,
         State = row.State,
         Phase = row.Phase,
         Priority = row.Priority,
         Severity = row.Severity,
         Epoch = row.Epoch,
         ReviewState = row.ReviewState,
         ReviewPriority = row.ReviewPriority,
         Document = row.Document,
         Draft = row.Draft,
         LegacyDocument = row.LegacyDocument,
         PlainNote = row.PlainNote,
         OffsetHours = row.OffsetHours,
         OffsetClaimed = row.OffsetClaimed,
         OptionalOffset = row.OptionalOffset,
         Metadata = row.Metadata,
         ClaimedMetadata = row.ClaimedMetadata,
         JsonMetadata = row.JsonMetadata
      };
   }
}
