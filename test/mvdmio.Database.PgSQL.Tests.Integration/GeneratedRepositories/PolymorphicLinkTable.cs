using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>The two kinds a <see cref="PolymorphicLinkTable" /> row's identifier may point at.</summary>
public enum LinkTargetKind
{
   Person,
   Asset
}

/// <summary>
///    A link table carrying a kind column beside an identifier, reaching two different targets through that same
///    pair of columns — the polymorphic shape the spec's problem statement describes. Each relation's Relation
///    condition fixes the kind value it reaches, so the per-kind column this shape would otherwise need never
///    appears in C#.
/// </summary>
[Table("public.generated_polymorphic_links")]
public partial class PolymorphicLinkTable
{
   [PrimaryKey]
   [Generated]
   public long LinkId { get; set; }

   public LinkTargetKind Kind { get; set; }
   public long TargetId { get; set; }

   private PersonRelation? Person { get; set; }
   private AssetRelation? Asset { get; set; }

   private class PersonRelation : RelationDefinition<PolymorphicLinkTable, LinkPersonTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.TargetId, y => y.PersonId),
      ];

      public override Expression<Func<PolymorphicLinkTable, LinkPersonTable, bool>> Condition
         => (link, person) => link.Kind == LinkTargetKind.Person;
   }

   private class AssetRelation : RelationDefinition<PolymorphicLinkTable, LinkAssetTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.TargetId, y => y.AssetId),
      ];

      public override Expression<Func<PolymorphicLinkTable, LinkAssetTable, bool>> Condition
         => (link, asset) => link.Kind == LinkTargetKind.Asset;
   }
}
