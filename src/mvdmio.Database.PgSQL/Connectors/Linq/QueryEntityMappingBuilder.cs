using JetBrains.Annotations;
using LinqToDB.Mapping;
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
   /// <returns>The same builder, so calls can be chained.</returns>
   public QueryEntityMappingBuilder<TEntity> Column<TProperty>(Expression<Func<TEntity, TProperty>> property, string columnName, bool isPrimaryKey = false)
   {
      ArgumentNullException.ThrowIfNull(property);
      ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

      var propertyBuilder = _builder.Property(property).HasColumnName(columnName);

      if (isPrimaryKey)
         propertyBuilder.IsPrimaryKey();

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
}
