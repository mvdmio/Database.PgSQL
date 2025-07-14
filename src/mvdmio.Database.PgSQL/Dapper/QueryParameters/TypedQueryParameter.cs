using System.Data;
using Dapper;
using JetBrains.Annotations;
using Npgsql;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Dapper.QueryParameters;

/// <summary>
///   Custom query parameter for setting PostgreSQL parameter types.
/// </summary>
[PublicAPI]
public class TypedQueryParameter : SqlMapper.ICustomQueryParameter
{
   private readonly object? _value;
   private readonly NpgsqlDbType _dbType;

   /// <summary>
   /// Constructor.
   /// </summary>
   public TypedQueryParameter(object? value, NpgsqlDbType dbType)
   {
      _value = value;
      _dbType = dbType;
   }

   /// <inheritdoc />
   public void AddParameter(IDbCommand command, string name)
   {
      var parameter = new NpgsqlParameter(name, _dbType) {
         Value = _value
      };

      command.Parameters.Add(parameter);
   }
}