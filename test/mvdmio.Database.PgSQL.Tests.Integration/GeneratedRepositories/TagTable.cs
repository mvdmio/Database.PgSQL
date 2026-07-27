using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    The far side of a many-to-many, reached through a join table that is itself a table definition.
/// </summary>
[Table("public.generated_tags")]
public partial class TagTable
{
   [PrimaryKey]
   [Generated]
   public long TagId { get; set; }

   [Unique]
   public string Label { get; set; } = string.Empty;

   [Relation(nameof(BookTagTable.TagId))]
   public List<BookTagTable> BookTags { get; set; } = [];
}
