using JetBrains.Annotations;
using Npgsql;

namespace mvdmio.Database.PgSQL;

/// <summary>
///   Information about the database connection.
/// </summary>
[PublicAPI]
public class DatabaseConnectionInfo
{
   private readonly NpgsqlConnectionStringBuilder _builder;

   /// <summary>
   ///   The hostname or IP address of the PostgreSQL server. Defaults to "localhost" if not specified.
   /// </summary>
   public string Host => _builder.Host ?? "localhost";

   /// <summary>
   ///   The port number of the PostgreSQL server. Defaults to 5432 if not specified.
   /// </summary>
   public int Port => _builder.Port == 0 ? 5432 : _builder.Port;

   /// <summary>
   ///   The name of the database to connect to. Defaults to "postgres" if not specified.
   /// </summary>
   public string Database => _builder.Database ?? "postgres";

   public DatabaseConnectionInfo(string connectionString)
   {
      _builder = new NpgsqlConnectionStringBuilder(connectionString);
   }
}
