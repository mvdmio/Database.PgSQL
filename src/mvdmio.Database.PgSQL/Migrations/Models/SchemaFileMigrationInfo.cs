using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Migrations.Models;

/// <summary>
///    Represents migration version information extracted from a schema file header.
/// </summary>
/// <param name="Identifier">The migration identifier (timestamp-based, e.g., 202602161430).</param>
/// <param name="Name">The migration name (e.g., "AddUsersTable").</param>
/// <param name="Scope">
///    The scope the version line belongs to, or null when the header was written in the legacy
///    scope-less single-line format.
/// </param>
[PublicAPI]
public readonly record struct SchemaFileMigrationInfo(long Identifier, string Name, string? Scope = null);
