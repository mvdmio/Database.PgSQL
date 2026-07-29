using JetBrains.Annotations;
using LinqToDB.Mapping;
using NpgsqlTypes;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    Describes how the properties of a generated data type map to the columns of its table.
///    Instances are handed to the callback passed to <see cref="QueryMappings.Register{TEntity}" />.
/// </summary>
/// <typeparam name="TEntity">The generated data type being mapped.</typeparam>
[PublicAPI]
public sealed class QueryEntityMappingBuilder<TEntity>
   where TEntity : class
{
   private readonly EntityMappingBuilder<TEntity> _builder;

   internal QueryEntityMappingBuilder(EntityMappingBuilder<TEntity> builder)
   {
      _builder = builder;
   }

   /// <summary>
   ///    Maps a property of <typeparamref name="TEntity" /> to a database column.
   /// </summary>
   /// <typeparam name="TProperty">The property type.</typeparam>
   /// <param name="property">An expression selecting the property to map.</param>
   /// <param name="columnName">The name of the database column the property maps to.</param>
   /// <param name="isPrimaryKey">Whether the column is part of the table's primary key. Every member of a composite key sets it.</param>
   /// <param name="isNotNull">
   ///    Whether the column cannot hold null. A key member is not-null whichever way this is left, and nullable is what
   ///    the query surface assumes, so this is only ever set to state that a non-key column cannot hold null.
   /// </param>
   /// <returns>The same builder, so calls can be chained.</returns>
   /// <remarks>
   ///    Stating that a column cannot hold null is what keeps a predicate over it, and a join condition on it, free of
   ///    the "or the column is null" alternative the query surface's null-comparison mode otherwise adds — an
   ///    alternative that can never match on such a column and that costs the predicate its index. The claim is never
   ///    verified against the real table, and a column that does hold null is not caught when the row is read. What a
   ///    wrong claim costs is rows: the alternative it removed is what would have matched the null ones.
   /// </remarks>
   public QueryEntityMappingBuilder<TEntity> Column<TProperty>(
      Expression<Func<TEntity, TProperty>> property,
      string columnName,
      bool isPrimaryKey = false,
      bool isNotNull = false
   )
   {
      ArgumentNullException.ThrowIfNull(property);
      ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

      Configure(_builder.Property(property).HasColumnName(columnName), isPrimaryKey, isNotNull);

      return this;
   }

   /// <summary>
   ///    Maps a property of <typeparamref name="TEntity" /> to a database column whose storage the definition states,
   ///    where the value needs no conversion to reach it.
   /// </summary>
   /// <typeparam name="TProperty">The property type.</typeparam>
   /// <param name="property">An expression selecting the property to map.</param>
   /// <param name="columnName">The name of the database column the property maps to.</param>
   /// <param name="storedAs">The PostgreSQL type the value is stored as.</param>
   /// <param name="isPrimaryKey">Whether the column is part of the table's primary key. Every member of a composite key sets it.</param>
   /// <param name="isNotNull">Whether the column cannot hold null. See the overload without a storage claim.</param>
   /// <returns>The same builder, so calls can be chained.</returns>
   /// <remarks>
   ///    This is the shape a <c>string</c> on a <c>jsonb</c> column takes: the value travels as it stands and only the
   ///    type it is bound as differs from what the driver would infer. A claim the provider cannot represent is dropped
   ///    here rather than refused — the Dapper surface still honours it, and the build warns that the two diverge.
   /// </remarks>
   public QueryEntityMappingBuilder<TEntity> Column<TProperty>(
      Expression<Func<TEntity, TProperty>> property,
      string columnName,
      NpgsqlDbType storedAs,
      bool isPrimaryKey = false,
      bool isNotNull = false
   )
   {
      ArgumentNullException.ThrowIfNull(property);
      ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

      Configure(MapColumn(property, columnName, storedAs), isPrimaryKey, isNotNull);

      return this;
   }

   /// <summary>
   ///    Maps a property of <typeparamref name="TEntity" /> to a database column whose storage the definition states,
   ///    converting the value on its way to and from the column.
   /// </summary>
   /// <typeparam name="TProperty">The property type.</typeparam>
   /// <typeparam name="TStored">The type the value is converted to before it reaches the column.</typeparam>
   /// <param name="property">An expression selecting the property to map.</param>
   /// <param name="columnName">The name of the database column the property maps to.</param>
   /// <param name="storedAs">The PostgreSQL type the converted value is stored as.</param>
   /// <param name="toStored">Converts a property value to what the column holds.</param>
   /// <param name="fromStored">Converts what the column holds back to a property value.</param>
   /// <param name="isPrimaryKey">Whether the column is part of the table's primary key. Every member of a composite key sets it.</param>
   /// <param name="isNotNull">Whether the column cannot hold null. See the overload without a storage claim.</param>
   /// <returns>The same builder, so calls can be chained.</returns>
   /// <remarks>
   ///    This is the shape an enum column takes, and the reason the claim is per column rather than per type: the same
   ///    enum can be stored as text here and as a number on another table, and each column carries its own conversion.
   ///    The conversion applies to a predicate's parameter as well as to a materialized row, which is what keeps
   ///    <c>Where(x =&gt; x.State == Open)</c> comparing against what the column actually holds.
   /// </remarks>
   public QueryEntityMappingBuilder<TEntity> Column<TProperty, TStored>(
      Expression<Func<TEntity, TProperty>> property,
      string columnName,
      NpgsqlDbType storedAs,
      Expression<Func<TProperty, TStored>> toStored,
      Expression<Func<TStored, TProperty>> fromStored,
      bool isPrimaryKey = false,
      bool isNotNull = false
   )
   {
      ArgumentNullException.ThrowIfNull(property);
      ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
      ArgumentNullException.ThrowIfNull(toStored);
      ArgumentNullException.ThrowIfNull(fromStored);

      var propertyBuilder = MapColumn(property, columnName, storedAs);

      propertyBuilder.HasConversion(toStored, fromStored);
      Configure(propertyBuilder, isPrimaryKey, isNotNull);

      return this;
   }

   /// <summary>
   ///    Maps a relation property of <typeparamref name="TEntity" /> to the one row it points at.
   /// </summary>
   /// <typeparam name="TTarget">The generated data type on the other side of the relation.</typeparam>
   /// <typeparam name="TThisKey">The type of the key on this side.</typeparam>
   /// <typeparam name="TTargetKey">The type of the key on the other side.</typeparam>
   /// <param name="property">An expression selecting the relation property to map.</param>
   /// <param name="thisKey">An expression selecting the key on this side of the relation.</param>
   /// <param name="targetKey">An expression selecting the key on the other side of the relation.</param>
   /// <returns>The same builder, so calls can be chained.</returns>
   /// <remarks>
   ///    Always an outer join. That is the query surface's contract — a foreign key pointing at a missing row yields
   ///    nothing rather than dropping the row that holds it — and stating it here means a consumer changing the
   ///    provider's global default cannot change what generated code means.
   /// </remarks>
   public QueryEntityMappingBuilder<TEntity> Relation<TTarget, TThisKey, TTargetKey>(
      Expression<Func<TEntity, TTarget?>> property,
      Expression<Func<TEntity, TThisKey>> thisKey,
      Expression<Func<TTarget, TTargetKey>> targetKey
   )
      where TTarget : class
   {
      ArgumentNullException.ThrowIfNull(property);
      ArgumentNullException.ThrowIfNull(thisKey);
      ArgumentNullException.ThrowIfNull(targetKey);

      _builder.Association(property!, thisKey, targetKey, canBeNull: true);

      return this;
   }

   /// <summary>
   ///    Maps a relation property of <typeparamref name="TEntity" /> to the many rows it points at.
   /// </summary>
   /// <typeparam name="TTarget">The generated data type on the other side of the relation.</typeparam>
   /// <typeparam name="TThisKey">The type of the key on this side.</typeparam>
   /// <typeparam name="TTargetKey">The type of the key on the other side.</typeparam>
   /// <param name="property">An expression selecting the relation property to map.</param>
   /// <param name="thisKey">An expression selecting the key on this side of the relation.</param>
   /// <param name="targetKey">An expression selecting the key on the other side of the relation.</param>
   /// <returns>The same builder, so calls can be chained.</returns>
   /// <remarks>
   ///    A property typed as a concrete list satisfies this overload and the single-target one both, so generated code
   ///    states its type arguments explicitly. A hand-written call resolves without them.
   /// </remarks>
   public QueryEntityMappingBuilder<TEntity> Relation<TTarget, TThisKey, TTargetKey>(
      Expression<Func<TEntity, IEnumerable<TTarget>>> property,
      Expression<Func<TEntity, TThisKey>> thisKey,
      Expression<Func<TTarget, TTargetKey>> targetKey
   )
      where TTarget : class
   {
      ArgumentNullException.ThrowIfNull(property);
      ArgumentNullException.ThrowIfNull(thisKey);
      ArgumentNullException.ThrowIfNull(targetKey);

      _builder.Association(property, thisKey, targetKey, canBeNull: true);

      return this;
   }

   /// <summary>
   ///    Maps a relation property of <typeparamref name="TEntity" /> to the one row it points at, joined on a predicate
   ///    rather than on a single pair of keys.
   /// </summary>
   /// <typeparam name="TTarget">The generated data type on the other side of the relation.</typeparam>
   /// <param name="property">An expression selecting the relation property to map.</param>
   /// <param name="predicate">
   ///    An expression comparing the two sides, with the declaring entity first and the target second. One equality per
   ///    member of the target's primary key, combined with <c>&amp;&amp;</c>.
   /// </param>
   /// <returns>The same builder, so calls can be chained.</returns>
   /// <remarks>
   ///    This is the form a composite key takes. The provider's key-based overloads carry one key each, and their key
   ///    type parameters are unconstrained, so an anonymous type or a tuple compiles there and registers as a single key
   ///    named after its constructor — failing only at the first query. A predicate is checked by the compiler member by
   ///    member instead. Always an outer join, for the same reason as the key-based overloads.
   /// </remarks>
   public QueryEntityMappingBuilder<TEntity> Relation<TTarget>(
      Expression<Func<TEntity, TTarget?>> property,
      Expression<Func<TEntity, TTarget, bool>> predicate
   )
      where TTarget : class
   {
      ArgumentNullException.ThrowIfNull(property);
      ArgumentNullException.ThrowIfNull(predicate);

      _builder.Association(property!, predicate, canBeNull: true);

      return this;
   }

   /// <summary>
   ///    Maps a relation property of <typeparamref name="TEntity" /> to the many rows it points at, joined on a predicate
   ///    rather than on a single pair of keys.
   /// </summary>
   /// <typeparam name="TTarget">The generated data type on the other side of the relation.</typeparam>
   /// <param name="property">An expression selecting the relation property to map.</param>
   /// <param name="predicate">
   ///    An expression comparing the two sides, with the declaring entity first and the target second. One equality per
   ///    member of the declaring type's primary key, combined with <c>&amp;&amp;</c>.
   /// </param>
   /// <returns>The same builder, so calls can be chained.</returns>
   /// <remarks>
   ///    A property typed as a concrete list satisfies this overload and the single-target one both, so generated code
   ///    states its type argument explicitly.
   /// </remarks>
   public QueryEntityMappingBuilder<TEntity> Relation<TTarget>(
      Expression<Func<TEntity, IEnumerable<TTarget>>> property,
      Expression<Func<TEntity, TTarget, bool>> predicate
   )
      where TTarget : class
   {
      ArgumentNullException.ThrowIfNull(property);
      ArgumentNullException.ThrowIfNull(predicate);

      _builder.Association(property, predicate, canBeNull: true);

      return this;
   }

   /// <summary>The column, its name, and its storage claim where the provider can represent one.</summary>
   /// <remarks>
   ///    A claim the provider has no equivalent for is left unstated rather than refused: the Dapper surface still honours
   ///    it and the build warns that the two diverge, which beats a registration that throws at startup.
   /// </remarks>
   private PropertyMappingBuilder<TEntity, TProperty> MapColumn<TProperty>(
      Expression<Func<TEntity, TProperty>> property,
      string columnName,
      NpgsqlDbType storedAs
   )
   {
      var propertyBuilder = _builder.Property(property).HasColumnName(columnName);
      var dataType = QueryStorageTypes.DataTypeFor(storedAs);

      if (dataType is not null)
         propertyBuilder.HasDataType(dataType.Value);

      return propertyBuilder;
   }

   /// <summary>What every <c>Column</c> overload settles the same way, whatever it says about storage.</summary>
   /// <remarks>
   ///    The key rule lives here rather than in the generator, because this builder is public surface a consumer calls
   ///    by hand: a key member cannot hold null, so every caller gets that without having to say it.
   /// </remarks>
   private static void Configure<TProperty>(PropertyMappingBuilder<TEntity, TProperty> propertyBuilder, bool isPrimaryKey, bool isNotNull)
   {
      if (isPrimaryKey)
         propertyBuilder.IsPrimaryKey();

      if (isPrimaryKey || isNotNull)
         propertyBuilder.IsNotNull();
   }
}
