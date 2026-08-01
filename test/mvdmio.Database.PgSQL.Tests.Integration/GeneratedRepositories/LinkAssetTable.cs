using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>The other of the two kinds <see cref="PolymorphicLinkTable" /> reaches — the "Asset" kind.</summary>
[Table("public.generated_link_assets")]
public partial class LinkAssetTable
{
   [PrimaryKey]
   [Generated]
   public long AssetId { get; set; }

   public string Name { get; set; } = string.Empty;

   private List<LinksRelation> Links { get; set; } = [];

   /// <summary>
   ///    The reverse direction, declared on its own with the same class and the same kind of condition — declaring
   ///    this never implies the forward one declared on <see cref="PolymorphicLinkTable" />.
   /// </summary>
   private class LinksRelation : RelationDefinition<LinkAssetTable, PolymorphicLinkTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AssetId, y => y.TargetId),
      ];

      public override Expression<Func<LinkAssetTable, PolymorphicLinkTable, bool>> Condition
         => (asset, link) => link.Kind == LinkTargetKind.Asset;
   }
}
