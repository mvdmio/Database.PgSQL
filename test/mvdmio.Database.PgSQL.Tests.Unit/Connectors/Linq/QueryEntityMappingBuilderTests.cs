using AwesomeAssertions;
using LinqToDB.Mapping;
using mvdmio.Database.PgSQL.Connectors.Linq;

namespace mvdmio.Database.PgSQL.Tests.Unit.Connectors.Linq;

/// <summary>
///    What the mapping builder tells the query surface about a column, asserted through the builder itself rather than
///    through generated code.
/// </summary>
/// <remarks>
///    The builder is public surface a consumer may call by hand, and it is where the rule that a key member cannot hold
///    null is applied — deliberately, so that a hand-written registration cannot lose the join-condition improvement by
///    omitting an argument. Generator tests assert what is emitted; only this asserts what the shipped builder does with
///    it. Registration is process-wide, so the entity type here is used by nothing else.
/// </remarks>
public class QueryEntityMappingBuilderTests
{
   private sealed class HandMappedRow
   {
      public long RowId { get; set; }
      public string Label { get; set; } = string.Empty;
      public string? Note { get; set; }
   }

   [Fact]
   public void Column_StatesNullabilityForAKeyMemberWithoutBeingAsked()
   {
      QueryMappings.Register<HandMappedRow>(
         "public",
         "hand_mapped_rows",
         entity => entity
            .Column(x => x.RowId, "row_id", isPrimaryKey: true)
            .Column(x => x.Label, "label", isNotNull: true)
            .Column(x => x.Note, "note")
      );

      var columns = QueryMappings.Schema.GetEntityDescriptor(typeof(HandMappedRow)).Columns;

      // The caller said nothing about nullability here: the key argument alone settles it.
      Column(columns, nameof(HandMappedRow.RowId)).CanBeNull.Should().BeFalse();
      Column(columns, nameof(HandMappedRow.RowId)).IsPrimaryKey.Should().BeTrue();

      Column(columns, nameof(HandMappedRow.Label)).CanBeNull.Should().BeFalse();

      // Nullable is the default, so a nullable column needs no argument.
      Column(columns, nameof(HandMappedRow.Note)).CanBeNull.Should().BeTrue();
   }

   private static ColumnDescriptor Column(IReadOnlyList<ColumnDescriptor> columns, string memberName)
   {
      return columns.Single(x => string.Equals(x.MemberName, memberName, StringComparison.Ordinal));
   }
}
