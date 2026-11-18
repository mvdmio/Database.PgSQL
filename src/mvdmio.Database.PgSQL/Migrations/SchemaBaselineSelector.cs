using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Migrations;

internal static class SchemaBaselineSelector
{
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

internal sealed record SchemaFileHeader(
   string ResourceName,
   string AssemblyName,
   IReadOnlyList<SchemaFileMigrationInfo> MigrationVersions,
   IReadOnlyCollection<string> VouchedScopes);

internal sealed record RejectedSchemaBaseline(SchemaFileMigrationInfo HeaderLine, string ResourceName, string AssemblyName);

internal sealed record SchemaBaselineSelection(IReadOnlyList<SchemaFileMigrationInfo> Baselines, IReadOnlyList<RejectedSchemaBaseline> Rejected);
