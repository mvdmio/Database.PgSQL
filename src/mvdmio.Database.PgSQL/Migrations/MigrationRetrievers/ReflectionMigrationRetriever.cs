using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;

/// <summary>
///    Migration retriever that uses reflection to search for migrations in a given collection of assemblies.
/// </summary>
public sealed class ReflectionMigrationRetriever : IMigrationRetriever
{
   private readonly Assembly[] _assembliesContainingMigrations;

   /// <summary>
   ///    Initializes a new instance of the <see cref="ReflectionMigrationRetriever"/> class.
   /// </summary>
   /// <param name="assembliesContainingMigrations">
   ///    List of assemblies to use for searching <see cref="IDbMigration" />
   ///    classes.
   /// </param>
   public ReflectionMigrationRetriever(params Assembly[] assembliesContainingMigrations)
   {
      _assembliesContainingMigrations = assembliesContainingMigrations;
   }

   /// <inheritdoc />
   public IEnumerable<IDbMigration> RetrieveMigrations()
   {
      return RetrieveMigrationsOfType<IDbMigration>();
   }

   private IEnumerable<T> RetrieveMigrationsOfType<T>()
      where T : class
   {
      var types = new List<Type>();

      foreach (var assembly in _assembliesContainingMigrations)
      {
         foreach (var type in assembly.GetTypes())
         {
            // Only consider concrete types that can be instantiated with a parameterless constructor.
            // Conventional migrations always satisfy this; abstract bases, interfaces, generic definitions,
            // and helper types with parameterized constructors are skipped instead of crashing instantiation.
            if (typeof(T).IsAssignableFrom(type)
                && type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                && type.GetConstructor(Type.EmptyTypes) is not null)
            {
               types.Add(type);
            }
         }
      }

      foreach (var type in types)
      {
         var instance = (T?)Activator.CreateInstance(type);

         if (instance is not null)
            yield return instance;
      }
   }
}
