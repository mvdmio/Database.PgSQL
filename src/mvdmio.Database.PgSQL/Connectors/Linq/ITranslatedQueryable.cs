using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    The part of the query decorator that <see cref="QueryRootRewriter" /> needs in order to replace it with the
///    provider's own expression, without knowing the element type.
/// </summary>
internal interface ITranslatedQueryable
{
   /// <summary>
   ///    Whether this queryable is the root of the composition — the table itself, with no operators applied.
   /// </summary>
   bool IsRoot { get; }

   /// <summary>
   ///    The composed expression. For a root this is a constant node holding the decorator itself.
   /// </summary>
   Expression Expression { get; }

   /// <summary>
   ///    The query source this queryable composes over, which is what the rewriter resolves its root against.
   /// </summary>
   LinqQuerySource Source { get; }
}
