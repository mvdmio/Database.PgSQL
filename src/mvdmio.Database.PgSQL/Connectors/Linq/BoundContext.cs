using LinqToDB.Data;
using Npgsql;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

internal sealed record BoundContext(DataConnection Context, NpgsqlConnection Connection, NpgsqlTransaction? Transaction)
{
   public bool IsBoundTo(NpgsqlConnection connection, NpgsqlTransaction? transaction)
   {
      return ReferenceEquals(Connection, connection) && ReferenceEquals(Transaction, transaction);
   }
}
