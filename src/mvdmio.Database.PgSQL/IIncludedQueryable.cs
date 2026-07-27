using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL;

/// <summary>
///    A query that has been asked to materialize a relation, remembering which relation that was so that a further
///    level can be chained onto it.
/// </summary>
/// <typeparam name="TEntity">The element type of the query.</typeparam>
/// <typeparam name="TProperty">The type of the relation property most recently included.</typeparam>
/// <remarks>
///    It declares no members of its own: it exists to carry <typeparamref name="TProperty" /> to
///    <see cref="QueryableExtensions.ThenInclude{TEntity,TPreviousProperty,TProperty}(IIncludedQueryable{TEntity,TPreviousProperty},System.Linq.Expressions.Expression{System.Func{TPreviousProperty,TProperty}})" />,
///    which cannot be expressed as a member chain because a chain cannot step through a collection.
/// </remarks>
[PublicAPI]
public interface IIncludedQueryable<out TEntity, out TProperty> : IQueryable<TEntity>;
