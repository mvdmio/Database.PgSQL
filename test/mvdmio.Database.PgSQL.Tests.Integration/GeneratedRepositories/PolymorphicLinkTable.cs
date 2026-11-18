using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

public enum LinkTargetKind
{
   Person,
   Asset
}

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
