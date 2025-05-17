using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors;

/// <summary>
///   Management functions for databases.
/// </summary>
[PublicAPI]
public class ManagementDatabaseConnector
{
   private readonly DatabaseConnection _db;

   /// <summary>
   ///    Constructor.
   /// </summary>
   public ManagementDatabaseConnector(DatabaseConnection db)
   {
      _db = db;
   }

   /// <summary>
   ///    Check if a table with the given name on the given schema exists.
   /// </summary>
   public bool TableExists(string schema, string tableName)
   {
      return _db.Dapper.ExecuteScalar<bool>(
         """
         SELECT EXISTS (
            SELECT table_name
            FROM information_schema.tables
            WHERE table_name = :tableName
            AND table_schema = :schema
         )
         """,
         new Dictionary<string, object?> {
            { "schema", schema },
            { "tableName", tableName }
         }
      );
   }

   /// <summary>
   ///    Check if a table with the given name on the given schema exists.
   /// </summary>
   public async Task<bool> TableExistsAsync(string schema, string tableName)
   {
      return await _db.Dapper.ExecuteScalarAsync<bool>(
         """
         SELECT EXISTS (
            SELECT table_name
            FROM information_schema.tables
            WHERE table_name = :tableName
            AND table_schema = :schema
         )
         """,
         new Dictionary<string, object?> {
            { "schema", schema },
            { "tableName", tableName }
         }
      );
   }

   /// <summary>
   ///    Check if a schema with the given name exists.
   /// </summary>
   public bool SchemaExists(string schema)
   {
      return _db.Dapper.ExecuteScalar<bool>(
         """
         SELECT EXISTS (
            SELECT schema_name
            FROM information_schema.schemata
            WHERE schema_name = :schema
         )
         """,
         new Dictionary<string, object?> {
            { "schema", schema }
         }
      );
   }

   /// <summary>
   ///    Check if a schema with the given name exists.
   /// </summary>
   public async Task<bool> SchemaExistsAsync(string schema)
   {
      return await _db.Dapper.ExecuteScalarAsync<bool>(
         """
         SELECT EXISTS (
            SELECT schema_name
            FROM information_schema.schemata
            WHERE schema_name = :schema
         )
         """,
         new Dictionary<string, object?> {
            { "schema", schema }
         }
      );
   }
}