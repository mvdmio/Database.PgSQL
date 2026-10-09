using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL;

/// <summary>
///    Settings a <see cref="DatabaseConnectionFactory" /> applies to every data source it builds, whichever caller builds
///    it first.
/// </summary>
/// <remarks>
///    Each value is decided on its own: a value set here wins, then a keyword in the connection string, then the
///    library's default. A value left <see langword="null" /> is not set. A value that is set is used exactly as given;
///    a value Npgsql refuses, such as a negative cap, fails when the data source is built.
/// </remarks>
[PublicAPI]
public sealed class DatabaseConnectionFactorySettings
{
   /// <summary>
   ///    The maximum number of connections in each pool the factory builds. When <see langword="null" />, the connection
   ///    string's <c>Maximum Pool Size</c> keyword applies, or else the default of 10.
   /// </summary>
   public int? MaxPoolSize { get; init; }

   /// <summary>
   ///    The name of each pool the factory builds. It sets both the connection string's <c>Application Name</c>, which
   ///    Postgres shows in <c>pg_stat_activity</c>, and the data source's <see cref="Npgsql.NpgsqlDataSourceBuilder.Name" />,
   ///    which Npgsql uses in traces, logs and metrics. When <see langword="null" />, the connection string's
   ///    <c>Application Name</c> keyword applies, or else the entry assembly's name.
   /// </summary>
   public string? ApplicationName { get; init; }
}
