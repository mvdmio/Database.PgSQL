using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors;

/// <summary>
///   Configuration options for the <see cref="BulkConnector.UpsertAsync" /> method.
/// </summary>
[PublicAPI]
public sealed class UpsertConfiguration
{
   /// <summary>
   ///   The on-conflict columns to use.
   /// </summary>
   public required string[] OnConflictColumns { get; init; }
   
   /// <summary>
   ///   The on-conflict where clause to use. This is used to filter the rows that are updated in the case of a conflict. Must NOT start with "WHERE", this is added automatically.
   /// </summary>
   public string OnConflictWhereClause { get; init; } = string.Empty;
}