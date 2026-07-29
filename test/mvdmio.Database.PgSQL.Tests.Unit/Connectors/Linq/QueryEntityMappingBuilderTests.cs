using AwesomeAssertions;
using LinqToDB;
using LinqToDB.Mapping;
using mvdmio.Database.PgSQL.Connectors.Linq;
using NpgsqlTypes;

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

   private enum HandMappedState
   {
      Open,
      Closed
   }

   private sealed class StoredAsRow
   {
      public long RowId { get; set; }
      public HandMappedState State { get; set; }
      public string Document { get; set; } = string.Empty;
      public string Address { get; set; } = string.Empty;
   }

   [Fact]
   public void Column_GivenAStorageClaim_StatesTheProvidersEquivalentDataType()
   {
      RegisterStoredAsRow();

      var columns = QueryMappings.Schema.GetEntityDescriptor(typeof(StoredAsRow)).Columns;

      Column(columns, nameof(StoredAsRow.State)).DataType.Should().Be(DataType.Text);
      Column(columns, nameof(StoredAsRow.Document)).DataType.Should().Be(DataType.BinaryJson);
   }

   /// <summary>
   ///    A conversion belongs to the column, not to the type: this is what a registry keyed by CLR type cannot express and
   ///    the reason the claim is stated per column.
   /// </summary>
   [Fact]
   public void Column_GivenAConversion_CarriesItOnTheColumn()
   {
      RegisterStoredAsRow();

      var state = Column(QueryMappings.Schema.GetEntityDescriptor(typeof(StoredAsRow)).Columns, nameof(StoredAsRow.State));

      state.ValueConverter.Should().NotBeNull();
      state.ValueConverter!.ToProviderExpression.Compile().DynamicInvoke(HandMappedState.Closed).Should().Be("Closed");
      state.ValueConverter.FromProviderExpression.Compile().DynamicInvoke("closed").Should().Be(HandMappedState.Closed);
   }

   /// <summary>
   ///    A claim the provider has no equivalent for is dropped here rather than refused. The Dapper surface still honours
   ///    it, and the build warns that the two diverge — which is better than a registration that throws at startup.
   /// </summary>
   [Fact]
   public void Column_GivenAClaimTheProviderCannotRepresent_LeavesTheDataTypeUnstated()
   {
      RegisterStoredAsRow();

      var address = Column(QueryMappings.Schema.GetEntityDescriptor(typeof(StoredAsRow)).Columns, nameof(StoredAsRow.Address));

      address.DataType.Should().Be(DataType.Undefined);
   }

   /// <remarks>
   ///    Registration is process-wide and registering a type twice is a no-op, so every test above registers the same
   ///    mapping and reads it back rather than depending on which of them ran first.
   /// </remarks>
   private static void RegisterStoredAsRow()
   {
      QueryMappings.Register<StoredAsRow>(
         "public",
         "stored_as_rows",
         entity => entity
            .Column(x => x.RowId, "row_id", isPrimaryKey: true)
            .Column<HandMappedState, string>(
               x => x.State,
               "state",
               NpgsqlDbType.Text,
               static x => x.ToString(),
               static x => Enum.Parse<HandMappedState>(x, true),
               isNotNull: true
            )
            .Column(x => x.Document, "document", NpgsqlDbType.Jsonb, isNotNull: true)
            .Column(x => x.Address, "address", NpgsqlDbType.Inet, isNotNull: true)
      );
   }

   private static ColumnDescriptor Column(IReadOnlyList<ColumnDescriptor> columns, string memberName)
   {
      return columns.Single(x => string.Equals(x.MemberName, memberName, StringComparison.Ordinal));
   }
}
