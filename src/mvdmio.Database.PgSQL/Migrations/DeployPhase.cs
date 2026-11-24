using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    The part of a migration that runs at one point of a deploy. A single part and a single history row carry one
///    value; a migrate run accepts any non-zero combination.
/// </summary>
[PublicAPI]
[Flags]
public enum DeployPhase
{
   /// <summary>
   ///    The part that runs before the new application version is deployed.
   /// </summary>
   PreDeploy = 1,

   /// <summary>
   ///    The part that runs after the new application version is deployed.
   /// </summary>
   PostDeploy = 2,

   /// <summary>
   ///    Both parts, for local databases and tests.
   /// </summary>
   All = PreDeploy | PostDeploy
}
