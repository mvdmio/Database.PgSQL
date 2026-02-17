using Dapper;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.Database.PgSQL.Dapper.TypeHandlers.Base;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Extensions;

/// <summary>
///   Extensions methods for <see cref="IServiceCollection"/>.
/// </summary>
[PublicAPI]
public static class ServiceCollectionExtensions
{
   /// <summary>
   ///   Add Dapper type handlers for all enums in the specified assemblies.
   /// </summary>
   /// <param name="services">The service collection to add the type handlers to.</param>
   /// <param name="assemblies">The assemblies to scan for enum types.</param>
   /// <returns>The service collection for chaining.</returns>
   public static IServiceCollection AddEnumDapperTypeHandlers(this IServiceCollection services, params Assembly[] assemblies)
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
