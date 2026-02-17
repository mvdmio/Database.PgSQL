using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Configuration for the migration tracking table.
/// </summary>
[PublicAPI]
public sealed class MigrationTableConfiguration
{
   /// <summary>
   ///    The default schema name for the migration tracking table.
   /// </summary>
   public const string DEFAULT_SCHEMA = "mvdmio";

   /// <summary>
   ///    The default table name for the migration tracking table.
   /// </summary>
   public const string DEFAULT_TABLE = "migrations";

   /// <summary>
   ///    Gets the default migration table configuration.
   /// </summary>
   public static MigrationTableConfiguration Default { get; } = new();

   /// <summary>
   ///    The schema name where the migration tracking table is stored.
   /// </summary>
   public string Schema { get; init; } = DEFAULT_SCHEMA;

   /// <summary>
   ///    The table name for the migration tracking table.
   /// </summary>
   public string Table { get; init; } = DEFAULT_TABLE;

   /// <summary>
   ///    Gets the fully qualified table name in the format "schema"."table".
   /// </summary>
   public string FullyQualifiedTableName => $"\"{Schema}\".\"{Table}\"";
}
