namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    Commands filling every column of <see cref="StorageClaimTable" />, so the two test classes over that table agree
///    about what a row holds and neither has to be edited when the matrix gains a cell.
/// </summary>
internal static class StorageClaimRows
{
   public const string DOCUMENT = """{"kind": "invoice"}""";
   public const string DRAFT = """{"kind": "draft"}""";
   public const string LEGACY_DOCUMENT = """{"kind":"legacy"}""";
   public const string PLAIN_NOTE = "a plain note";
   public const sbyte OFFSET_HOURS = -3;

   /// <remarks>
   ///    The three enum arguments are spread across the five enum columns rather than each column taking its own, so that a
   ///    claim landing on the wrong column is visible instead of masked by every column holding the same member.
   /// </remarks>
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

   /// <summary>
   ///    An update carrying every column forward unchanged, so a test can change the one column it is about. Written from
   ///    the row rather than from literals, so that adding a matrix column cannot silently start writing null into it.
   /// </summary>
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
