using Dapper;
using JetBrains.Annotations;
using Npgsql;
using NpgsqlTypes;
using System.Data;

namespace mvdmio.Database.PgSQL.Dapper.QueryParameters;

/// <summary>
///   Custom query parameter for setting PostgreSQL parameter types.
/// </summary>
[PublicAPI]
public sealed class TypedQueryParameter : SqlMapper.ICustomQueryParameter
{
   private readonly object? _value;
   private readonly NpgsqlDbType _dbType;

   /// <summary>
   ///   Initializes a new instance of the <see cref="TypedQueryParameter"/> class.
   /// </summary>
   /// <param name="value">The value of the parameter.</param>
   /// <param name="dbType">The PostgreSQL data type of the parameter.</param>
   public TypedQueryParameter(object? value, NpgsqlDbType dbType)
   {
      _value = value;
      _dbType = dbType;
   }

   /// <inheritdoc />
   public void AddParameter(IDbCommand command, string name)
   {
      var parameter = new NpgsqlParameter(name, _dbType)
      {
         Value = _value
      };

      command.Parameters.Add(parameter);
   }
}
