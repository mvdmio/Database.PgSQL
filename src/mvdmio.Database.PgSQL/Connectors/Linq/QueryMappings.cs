using JetBrains.Annotations;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using System.Text.Json;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    Owns the single, process-wide mapping schema that the query surface translates against.
///    Generated repositories register their table definitions here; the schema instance is shared so that
///    query translation stays cached instead of being rebuilt per connection.
/// </summary>
[PublicAPI]
public static class QueryMappings
{
   private static readonly object _lock = new();
   private static readonly HashSet<Type> _registeredEntities = [];
   private static readonly MappingSchema _schema = BuildSchema();

   internal static MappingSchema Schema => _schema;

   /// <summary>
   ///    Registers the table mapping for a generated data type. Called by generated code; there is no reason to call it
   ///    by hand. Registering the same type twice is a no-op.
   /// </summary>
   /// <typeparam name="TEntity">The generated data type to map.</typeparam>
   /// <param name="schemaName">The database schema the table lives in.</param>
   /// <param name="tableName">The name of the table.</param>
   /// <param name="configure">A callback that maps each property to its column.</param>
   public static void Register<TEntity>(string schemaName, string tableName, Action<QueryEntityMappingBuilder<TEntity>> configure)
      where TEntity : class
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
      ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
      ArgumentNullException.ThrowIfNull(configure);

      lock (_lock)
      {
         if (!_registeredEntities.Add(typeof(TEntity)))
            return;

         var fluentBuilder = new FluentMappingBuilder(_schema);
         var entityBuilder = fluentBuilder.Entity<TEntity>()
            .HasSchemaName(schemaName)
            .HasTableName(tableName);

         configure.Invoke(new QueryEntityMappingBuilder<TEntity>(entityBuilder));
         fluentBuilder.Build();
      }
   }

   internal static void Configure(Action<MappingSchema> configure)
   {
      lock (_lock)
      {
         configure.Invoke(_schema);
      }
   }

   /// <remarks>
   ///    Mirrors the Dapper type handlers registered in <c>DefaultConfig</c> so both surfaces read the same types.
   ///    <see cref="DateOnly" /> and <see cref="TimeOnly" /> need no conversion — the provider maps them natively.
   ///    The analyzer's <c>QueryMappableTypes</c> decides which property types warn at build time; it cannot reference
   ///    this assembly, so adding a conversion here means adding the type there too.
   /// </remarks>
   private static MappingSchema BuildSchema()
   {
      var schema = new MappingSchema();

      schema.AddScalarType(typeof(Uri), DataType.Text);
      schema.SetConverter<Uri, string>(x => x.AbsoluteUri);
      schema.SetConverter<Uri, DataParameter>(x => new DataParameter(null, x.AbsoluteUri, DataType.Text));
      schema.SetConverter<string, Uri>(x => new Uri(x));

      schema.AddScalarType(typeof(Dictionary<string, string>), DataType.BinaryJson);
      schema.SetConverter<Dictionary<string, string>, string>(x => JsonSerializer.Serialize(x));
      schema.SetConverter<Dictionary<string, string>, DataParameter>(x => new DataParameter(null, JsonSerializer.Serialize(x), DataType.BinaryJson));
      schema.SetConverter<string, Dictionary<string, string>>(x => JsonSerializer.Deserialize<Dictionary<string, string>>(x)!);

      return schema;
   }
}
