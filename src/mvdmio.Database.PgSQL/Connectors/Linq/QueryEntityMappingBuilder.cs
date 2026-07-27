using JetBrains.Annotations;
using LinqToDB.Mapping;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    Describes how the properties of a generated data type map to the columns of its table.
///    Instances are handed to the callback passed to <see cref="QueryMappings.Register{TEntity}" />.
/// </summary>
/// <typeparam name="TEntity">The generated data type being mapped.</typeparam>
[PublicAPI]
public sealed class QueryEntityMappingBuilder<TEntity>
   where TEntity : class
{
   private readonly EntityMappingBuilder<TEntity> _builder;

   internal QueryEntityMappingBuilder(EntityMappingBuilder<TEntity> builder)
   {
      _builder = builder;
   }

   /// <summary>
   ///    Maps a property of <typeparamref name="TEntity" /> to a database column.
   /// </summary>
   /// <typeparam name="TProperty">The property type.</typeparam>
   /// <param name="property">An expression selecting the property to map.</param>
   /// <param name="columnName">The name of the database column the property maps to.</param>
   /// <param name="isPrimaryKey">Whether the column is the table's primary key.</param>
   /// <returns>The same builder, so calls can be chained.</returns>
   public QueryEntityMappingBuilder<TEntity> Column<TProperty>(Expression<Func<TEntity, TProperty>> property, string columnName, bool isPrimaryKey = false)
   {
      ArgumentNullException.ThrowIfNull(property);
      ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

      var propertyBuilder = _builder.Property(property).HasColumnName(columnName);

      if (isPrimaryKey)
         propertyBuilder.IsPrimaryKey();

      return this;
   }
}
