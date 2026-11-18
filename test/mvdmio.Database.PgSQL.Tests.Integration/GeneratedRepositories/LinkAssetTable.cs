using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

[Table("public.generated_link_assets")]
public partial class LinkAssetTable
{
   [PrimaryKey]
   [Generated]
   public long AssetId { get; set; }

   public string Name { get; set; } = string.Empty;

   private List<LinksRelation> Links { get; set; } = [];

   private class LinksRelation : RelationDefinition<LinkAssetTable, PolymorphicLinkTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AssetId, y => y.TargetId),
      ];

      public override Expression<Func<LinkAssetTable, PolymorphicLinkTable, bool>> Condition
         => (asset, link) => link.Kind == LinkTargetKind.Asset;
   }
}
