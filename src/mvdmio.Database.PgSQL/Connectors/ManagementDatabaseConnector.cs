using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Connectors.Schema;

namespace mvdmio.Database.PgSQL.Connectors;

/// <summary>
///   Management functions for databases.
/// </summary>
[PublicAPI]
public sealed class ManagementDatabaseConnector
{
   private readonly DatabaseConnection _db;

   /// <summary>
   ///    Provides access to schema extraction methods for introspecting the database structure.
   /// </summary>
   public SchemaExtractor Schema { get; }

   /// <summary>
   ///    Initializes a new instance of the <see cref="ManagementDatabaseConnector"/> class.
   /// </summary>
   /// <param name="db">The database connection to use for management operations.</param>
   public ManagementDatabaseConnector(DatabaseConnection db)
   {
      _db = db;
      Schema = new SchemaExtractor(db);
   }

   /// <summary>
   ///    Generates a complete, idempotent SQL script that recreates the database schema.
   ///    This is a convenience method that delegates to <see cref="SchemaExtractor.GenerateSchemaScriptAsync"/>.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>A SQL script string that can be executed to recreate the database schema.</returns>
   public async Task<string> GenerateSchemaScriptAsync(CancellationToken cancellationToken = default)
   {
      return await Schema.GenerateSchemaScriptAsync(cancellationToken);
   }

   /// <summary>
   ///    Check if a table with the given name on the given schema exists.
   /// </summary>
   /// <param name="schema">The schema name to check.</param>
   /// <param name="tableName">The table name to check.</param>
   /// <returns><c>true</c> if the table exists; otherwise, <c>false</c>.</returns>
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
   /// <param name="schema">The schema name to check.</param>
   /// <param name="tableName">The table name to check.</param>
   /// <returns>A task that represents the asynchronous operation. The task result is <c>true</c> if the table exists; otherwise, <c>false</c>.</returns>
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
   /// <param name="schema">The schema name to check.</param>
   /// <returns><c>true</c> if the schema exists; otherwise, <c>false</c>.</returns>
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
   /// <param name="schema">The schema name to check.</param>
   /// <returns>A task that represents the asynchronous operation. The task result is <c>true</c> if the schema exists; otherwise, <c>false</c>.</returns>
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
