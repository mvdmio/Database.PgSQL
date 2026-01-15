using System.Reflection;
using Dapper;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.Database.PgSQL.Dapper.TypeHandlers;
using mvdmio.Database.PgSQL.Dapper.TypeHandlers.Base;

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