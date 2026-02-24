using Dapper;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using mvdmio.Database.PgSQL.Dapper.TypeHandlers.Base;
using System.Reflection;

namespace mvdmio.Database.PgSQL;

/// <summary>
///   Extensions methods for <see cref="IServiceCollection"/>.
/// </summary>
[PublicAPI]
public static class ServiceCollectionExtensions
{
   extension(IServiceCollection services)
   {
      /// <summary>
      ///   Adds the mvdmio.Database.PgSQL package dependencies to the <see cref="IServiceCollection"/>.
      /// </summary>
      /// <returns>The service collection for chaining.</returns>
      public IServiceCollection AddDatabase()
      {
         services.TryAddSingleton<DatabaseConnectionFactory>();
         return services;
      }

      /// <summary>
      ///   Add Dapper type handlers for all enums in the specified assemblies.
      /// </summary>
      /// <param name="assemblies">The assemblies to scan for enum types.</param>
      /// <returns>The service collection for chaining.</returns>
      public IServiceCollection AddEnumDapperTypeHandlers(params Assembly[] assemblies)
      {
         var enumTypes = assemblies.SelectMany(assembly => assembly.GetTypes().Where(type => type.IsEnum));

         foreach (var enumType in enumTypes)
         {
            var typeHandlerType = typeof(EnumAsStringTypeHandler<>).MakeGenericType(enumType);
            var typeHandler = Activator.CreateInstance(typeHandlerType);
            SqlMapper.AddTypeHandler(enumType, (SqlMapper.ITypeHandler)typeHandler!);
         }

         return services;
      }
   }
}
