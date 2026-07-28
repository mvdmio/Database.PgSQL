using LinqToDB.Data;
using Npgsql;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    A query provider context together with the connection state it was constructed against. The provider binds a
///    transaction at construction, so a context is only valid for the connection and transaction it was built for —
///    keeping the three together is what makes "still valid?" a question with one answer.
/// </summary>
internal sealed record BoundContext(DataConnection Context, NpgsqlConnection Connection, NpgsqlTransaction? Transaction)
{
   public bool IsBoundTo(NpgsqlConnection connection, NpgsqlTransaction? transaction)
   {
      return ReferenceEquals(Connection, connection) && ReferenceEquals(Transaction, transaction);
   }
}
