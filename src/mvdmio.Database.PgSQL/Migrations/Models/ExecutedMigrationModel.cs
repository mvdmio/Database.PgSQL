using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Migrations.Models;

/// <summary>
///    A record of a migration that has run previously.
/// </summary>
/// <param name="Identifier">The migration identifier (timestamp-based, e.g., 202602161430).</param>
/// <param name="Name">The migration name (e.g., "AddUsersTable").</param>
/// <param name="ExecutedAtUtc">When the migration was executed, in UTC.</param>
/// <param name="Scope">
///    The scope the migration belongs to, or null for legacy rows recorded before scopes existed
///    that could not (yet) be attributed to a scope.
/// </param>
[PublicAPI]
public record struct ExecutedMigrationModel(long Identifier, string Name, DateTime ExecutedAtUtc, string? Scope = null);
