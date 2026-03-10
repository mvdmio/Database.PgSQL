using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

internal sealed class TableDefinitionModel
{
   public TableDefinitionModel(
      string namespaceName,
      string accessibility,
      string tableClassName,
      string entityName,
      string dataTypeName,
      string createCommandTypeName,
      string updateCommandTypeName,
      string repositoryInterfaceTypeName,
      string repositoryTypeName,
      string schemaName,
      string tableName,
      PropertyDefinitionModel primaryKey,
      ImmutableArray<PropertyDefinitionModel> dataProperties,
      ImmutableArray<PropertyDefinitionModel> createProperties,
      ImmutableArray<PropertyDefinitionModel> updateProperties,
      ImmutableArray<PropertyDefinitionModel> lookupProperties,
      ImmutableArray<PropertyDefinitionModel> mutableUpdateProperties
   )
   {
      NamespaceName = namespaceName;
      Accessibility = accessibility;
      TableClassName = tableClassName;
      EntityName = entityName;
      DataTypeName = dataTypeName;
      CreateCommandTypeName = createCommandTypeName;
      UpdateCommandTypeName = updateCommandTypeName;
      RepositoryInterfaceTypeName = repositoryInterfaceTypeName;
      RepositoryTypeName = repositoryTypeName;
      SchemaName = schemaName;
      TableName = tableName;
      PrimaryKey = primaryKey;
      DataProperties = dataProperties;
      CreateProperties = createProperties;
      UpdateProperties = updateProperties;
      LookupProperties = lookupProperties;
      MutableUpdateProperties = mutableUpdateProperties;
   }

   public string NamespaceName { get; }
   public string Accessibility { get; }
   public string TableClassName { get; }
   public string EntityName { get; }
   public string DataTypeName { get; }
   public string CreateCommandTypeName { get; }
   public string UpdateCommandTypeName { get; }
   public string RepositoryInterfaceTypeName { get; }
   public string RepositoryTypeName { get; }
   public string SchemaName { get; }
   public string TableName { get; }
   public PropertyDefinitionModel PrimaryKey { get; }
   public ImmutableArray<PropertyDefinitionModel> DataProperties { get; }
   public ImmutableArray<PropertyDefinitionModel> CreateProperties { get; }
   public ImmutableArray<PropertyDefinitionModel> UpdateProperties { get; }
   public ImmutableArray<PropertyDefinitionModel> LookupProperties { get; }
   public ImmutableArray<PropertyDefinitionModel> MutableUpdateProperties { get; }
}

internal sealed class PropertyDefinitionModel
{
   public PropertyDefinitionModel(
      string propertyName,
      string parameterName,
      string typeName,
      string columnName,
      bool isPrimaryKey,
      bool isUnique,
      bool isGenerated,
      bool requiresNullForgivingInitializer
   )
   {
      PropertyName = propertyName;
      ParameterName = parameterName;
      TypeName = typeName;
      ColumnName = columnName;
      IsPrimaryKey = isPrimaryKey;
      IsUnique = isUnique;
      IsGenerated = isGenerated;
      RequiresNullForgivingInitializer = requiresNullForgivingInitializer;
   }

   public string PropertyName { get; }
   public string ParameterName { get; }
   public string TypeName { get; }
   public string ColumnName { get; }
   public bool IsPrimaryKey { get; }
   public bool IsUnique { get; }
   public bool IsGenerated { get; }
   public bool RequiresNullForgivingInitializer { get; }
}
