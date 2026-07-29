using mvdmio.Database.PgSQL.Attributes;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    The enum every enum row of the storage matrix is exercised over. Declared without explicit values, because the
///    representation is the column's business rather than the enum's — which is the whole point of stating it per column.
/// </summary>
/// <remarks>
///    A Dapper type handler storing this enum as text is registered process-wide by <c>TestFixture</c>, so every test over
///    the table below runs with the registration in place. That is deliberate: an opt-in convenience must not change what a
///    generated repository does, and the integer-stored column is the proof — a handler that intercepted the binding would
///    write a member name into an <c>integer</c> column and fail.
/// </remarks>
public enum WorkState
{
   Open,
   InProgress,
   Closed
}

/// <summary>
///    One column per cell of the documented storage matrix, plus every setter shape a column is allowed to have. Five
///    columns carry <see cref="WorkState" /> in five representations, which is what a registry keyed by type cannot
///    express and the reason the claim is stated per column.
/// </summary>
[Table("public.generated_storage_claims")]
public partial class StorageClaimTable
{
   [PrimaryKey]
   [Generated]
   public long ClaimId { get; private set; }

   /// <summary>Database-populated and non-publicly settable, which is the shape the setter rule used to refuse.</summary>
   [Generated]
   public DateTime CreatedAt { get; private set; }

   /// <summary>Unclaimed, so stored as the text of its member name.</summary>
   public WorkState State { get; set; }

   /// <summary>Claiming text is claiming the default, and has to behave identically to <see cref="State" />.</summary>
   [Column(StoredAs = NpgsqlDbType.Text)]
   public WorkState Phase { get; set; }

   /// <summary>The same enum over an <c>integer</c> column.</summary>
   [Column(StoredAs = NpgsqlDbType.Integer)]
   public WorkState Priority { get; set; }

   /// <summary>The same enum over a <c>smallint</c> column.</summary>
   [Column(StoredAs = NpgsqlDbType.Smallint)]
   public WorkState Severity { get; set; }

   /// <summary>The same enum over a <c>bigint</c> column.</summary>
   [Column(StoredAs = NpgsqlDbType.Bigint)]
   public WorkState Epoch { get; set; }

   /// <summary>Nullable and unclaimed, over a <c>text</c> column that holds null.</summary>
   public WorkState? ReviewState { get; set; }

   /// <summary>Nullable and claimed, so the conversion has to lift through nullability in both directions.</summary>
   [Column(StoredAs = NpgsqlDbType.Integer)]
   public WorkState? ReviewPriority { get; set; }

   /// <summary>
   ///    Arbitrary JSON held as a string. Also the caller-supplied shape a definition expresses with
   ///    <c>required … { get; init; }</c>, which used to abandon the whole table.
   /// </summary>
   [Column(StoredAs = NpgsqlDbType.Jsonb)]
   public required string Document { get; init; }

   /// <summary>The same string shape over <c>json</c> rather than <c>jsonb</c>, which stores the text as written.</summary>
   [Column(StoredAs = NpgsqlDbType.Json)]
   public string? Draft { get; set; }

   /// <summary>JSON in a <c>text</c> column, cast at query time. Unclaimed, so it must keep binding as text.</summary>
   public string LegacyDocument { get; set; } = string.Empty;

   /// <summary>Text claimed as text, which must be indistinguishable from claiming nothing.</summary>
   [Column(StoredAs = NpgsqlDbType.Text)]
   public string PlainNote { get; set; } = string.Empty;

   /// <summary>Writable without a claim: widening is all a signed byte ever needed.</summary>
   public sbyte OffsetHours { get; set; }

   /// <summary>The same signed byte, claiming the width it is widened to anyway.</summary>
   [Column(StoredAs = NpgsqlDbType.Smallint)]
   public sbyte OffsetClaimed { get; set; }

   /// <summary>The widening lifted through nullability, over a column that holds null.</summary>
   public sbyte? OptionalOffset { get; set; }

   /// <summary>The one JSON shape that already worked, through a process-wide conversion rather than a claim.</summary>
   public Dictionary<string, string>? Metadata { get; set; }

   /// <summary>
   ///    The same dictionary with the claim stated. Worth its own column: a claim sends the value through the
   ///    parameter-type mechanism instead of the process-wide handler, so this is the one matrix cell where stating the
   ///    claim takes a different route to the database than leaving it out.
   /// </summary>
   [Column(StoredAs = NpgsqlDbType.Jsonb)]
   public Dictionary<string, string>? ClaimedMetadata { get; set; }

   /// <summary>And over <c>json</c>, the other claim the matrix lists for a dictionary.</summary>
   [Column(StoredAs = NpgsqlDbType.Json)]
   public Dictionary<string, string>? JsonMetadata { get; set; }
}
