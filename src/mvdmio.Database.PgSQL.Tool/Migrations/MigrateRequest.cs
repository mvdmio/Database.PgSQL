namespace mvdmio.Database.PgSQL.Tool.Migrations;

/// <summary>
///    Describes the requested migration target.
/// </summary>
internal sealed record MigrateRequest(long? TargetIdentifier)
{
   public static MigrateRequest Latest { get; } = new((long?)null);

   public bool IsLatest => !TargetIdentifier.HasValue;

   public static MigrateRequest To(long targetIdentifier)
   {
      return new MigrateRequest(targetIdentifier);
   }
}
