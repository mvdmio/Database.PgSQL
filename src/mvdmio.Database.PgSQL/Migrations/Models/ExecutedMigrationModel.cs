using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Migrations.Models;

/// <summary>
///    A record of a migration that has run previously.
/// </summary>
[PublicAPI]
public record struct ExecutedMigrationModel(long Identifier, string Name, DateTime ExecutedAtUtc);
