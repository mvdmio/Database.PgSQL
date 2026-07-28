using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    A definition whose columns both permit null in the real table, so that what happens when a nullability claim is
///    wrong is pinned rather than assumed.
/// </summary>
/// <remarks>
///    Nothing verifies a claim against the schema — the same trade the library already makes for column names, composite
///    keys and generated columns. <see cref="Label" /> claims not-null through its type alone and is therefore wrong
///    about its column; <see cref="Note" /> is the same shape with the claim withdrawn, which is the escape hatch for a
///    property whose type cannot say that its column is nullable.
/// </remarks>
[Table("public.generated_unverified_claims")]
public partial class UnverifiedClaimTable
{
   [PrimaryKey]
   public long ClaimId { get; set; }

   public string Label { get; set; } = string.Empty;

   [Column(Null = true)]
   public string Note { get; set; } = string.Empty;
}
