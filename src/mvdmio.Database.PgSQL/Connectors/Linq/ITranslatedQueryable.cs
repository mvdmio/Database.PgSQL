using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

internal interface ITranslatedQueryable
{
   bool IsRoot { get; }

   Expression Expression { get; }

   LinqQuerySource Source { get; }
}
