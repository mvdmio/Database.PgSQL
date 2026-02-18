using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Migrations.Models;

/// <summary>
///    Represents migration version information extracted from a schema file header.
/// </summary>
/// <param name="Identifier">The migration identifier (timestamp-based, e.g., 202602161430).</param>
/// <param name="Name">The migration name (e.g., "AddUsersTable").</param>
[PublicAPI]
public readonly record struct SchemaFileMigrationInfo(long Identifier, string Name);
