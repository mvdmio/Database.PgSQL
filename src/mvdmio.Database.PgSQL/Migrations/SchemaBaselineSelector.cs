using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Pure decision logic for schema-first bootstrap: selects which baseline rows the applied schema files may
///    establish. A schema file may only establish a baseline for a scope its own assembly vouches for — the
///    scopes of migrations discovered from that assembly, plus the assembly's simple name (the default scope).
///    Header lines naming any other scope are rejected, so a foreign header line (e.g. from a schema pulled off
///    a shared database) can never fabricate a watermark that silently suppresses another assembly's migrations.
/// </summary>
internal static class SchemaBaselineSelector
{
   /// <summary>
   ///    Selects the baseline rows to record from the applied schema-file headers.
   ///    Vouched scoped lines record one baseline per scope: the highest identifier for that scope across all
   ///    vouching files. Legacy scope-less lines are recorded individually (one baseline per identifier, not
   ///    collapsed), to be attributed by the backfill. Scoped lines whose scope the file's assembly does not
   ///    vouch for are rejected so the caller can warn about them.
   /// </summary>
   /// <param name="headers">One entry per applied schema file, with its parsed header lines and vouched scopes.</param>
   public static SchemaBaselineSelection SelectBaselines(IReadOnlyList<SchemaFileHeader> headers)
   {
      var vouchedInfos = new List<SchemaFileMigrationInfo>();
      var legacyInfos = new List<SchemaFileMigrationInfo>();
      var rejected = new List<RejectedSchemaBaseline>();

      foreach (var header in headers)
      {
         foreach (var info in header.MigrationVersions)
         {
            if (info.Scope is null)
               legacyInfos.Add(info);
            else if (header.VouchedScopes.Contains(info.Scope, StringComparer.Ordinal))
               vouchedInfos.Add(info);
            else
               rejected.Add(new RejectedSchemaBaseline(info, header.ResourceName, header.AssemblyName));
         }
      }

      var scopedBaselines = vouchedInfos
         .GroupBy(info => info.Scope!, StringComparer.Ordinal)
         .Select(group => group.OrderByDescending(info => info.Identifier).First());

      // Legacy scope-less header lines are recorded individually, not collapsed: each represents a
      // different assembly's baseline, and the backfill attributes each to its scope by identifier.
      // Collapsing them to the highest would leave every other scope without a watermark, re-running
      // migrations whose effects the schema already contains.
      var legacyBaselines = legacyInfos
         .GroupBy(info => info.Identifier)
         .Select(group => group.First());

      var baselines = scopedBaselines
         .Concat(legacyBaselines)
         .OrderBy(info => info.Identifier)
         .ToArray();

      return new SchemaBaselineSelection(baselines, rejected);
   }
}

/// <summary>
///    The parsed header of one applied schema file, together with the scopes its source assembly vouches for.
/// </summary>
/// <param name="ResourceName">Name of the embedded schema resource, for diagnostics.</param>
/// <param name="AssemblyName">Simple name of the assembly the schema file came from, for diagnostics.</param>
/// <param name="MigrationVersions">The migration-version lines parsed from the file's header.</param>
/// <param name="VouchedScopes">
///    The scopes the file's assembly vouches for: the scopes of migrations discovered from that assembly,
///    plus the assembly's simple name (the default scope).
/// </param>
internal sealed record SchemaFileHeader(
   string ResourceName,
   string AssemblyName,
   IReadOnlyList<SchemaFileMigrationInfo> MigrationVersions,
   IReadOnlyCollection<string> VouchedScopes);

/// <summary>
///    A header line that was rejected because the schema file's assembly does not vouch for its scope.
/// </summary>
/// <param name="HeaderLine">The rejected migration-version line.</param>
/// <param name="ResourceName">Name of the embedded schema resource the line came from.</param>
/// <param name="AssemblyName">Simple name of the assembly the schema file came from.</param>
internal sealed record RejectedSchemaBaseline(SchemaFileMigrationInfo HeaderLine, string ResourceName, string AssemblyName);

/// <summary>
///    Result of selecting baselines from the applied schema-file headers.
/// </summary>
/// <param name="Baselines">The baseline rows to record, ordered by identifier.</param>
/// <param name="Rejected">Header lines whose scope was not vouched for; no baseline is recorded for these.</param>
internal sealed record SchemaBaselineSelection(IReadOnlyList<SchemaFileMigrationInfo> Baselines, IReadOnlyList<RejectedSchemaBaseline> Rejected);
