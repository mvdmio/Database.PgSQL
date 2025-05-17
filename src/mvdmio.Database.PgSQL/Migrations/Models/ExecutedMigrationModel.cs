namespace mvdmio.Database.PgSQL.Migrations.Models;

/// <summary>
///    A record of a migration that has run previously.
/// </summary>
public record struct ExecutedMigrationModel(long Identifier, string Name, DateTime ExecutedAtUtc);