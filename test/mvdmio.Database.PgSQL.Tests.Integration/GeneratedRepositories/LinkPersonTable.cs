using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

[Table("public.generated_link_people")]
public partial class LinkPersonTable
{
   [PrimaryKey]
   [Generated]
   public long PersonId { get; set; }

   public string Name { get; set; } = string.Empty;

   private List<LinksRelation> Links { get; set; } = [];

   private class LinksRelation : RelationDefinition<LinkPersonTable, PolymorphicLinkTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.PersonId, y => y.TargetId),
      ];

      public override Expression<Func<LinkPersonTable, PolymorphicLinkTable, bool>> Condition
         => (person, link) => link.Kind == LinkTargetKind.Person;
   }
}
