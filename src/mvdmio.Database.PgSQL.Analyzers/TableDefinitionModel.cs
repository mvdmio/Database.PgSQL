using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

internal sealed class TableDefinitionModel
{
   public TableDefinitionModel(
      string namespaceName,
      string accessibility,
      string tableClassName,
      string tableClassFullName,
      string entityName,
      string dataTypeName,
      string createCommandTypeName,
      string updateCommandTypeName,
      string repositoryInterfaceTypeName,
      string repositoryTypeName,
      string schemaName,
      string tableName,
      ImmutableArray<PropertyDefinitionModel> primaryKeys,
      ImmutableArray<PropertyDefinitionModel> dataProperties,
      ImmutableArray<PropertyDefinitionModel> createProperties,
      ImmutableArray<PropertyDefinitionModel> lookupProperties,
      ImmutableArray<PropertyDefinitionModel> mutableUpdateProperties,
      ImmutableArray<PropertyDefinitionModel> tenancyColumns,
      ImmutableArray<RelationDeclarationModel> relations
   )
   {
      NamespaceName = namespaceName;
      Accessibility = accessibility;
      TableClassName = tableClassName;
      TableClassFullName = tableClassFullName;
      EntityName = entityName;
      DataTypeName = dataTypeName;
      CreateCommandTypeName = createCommandTypeName;
      UpdateCommandTypeName = updateCommandTypeName;
      RepositoryInterfaceTypeName = repositoryInterfaceTypeName;
      RepositoryTypeName = repositoryTypeName;
      SchemaName = schemaName;
      TableName = tableName;
      PrimaryKeys = primaryKeys;
      DataProperties = dataProperties;
      CreateProperties = createProperties;
      LookupProperties = lookupProperties;
      MutableUpdateProperties = mutableUpdateProperties;
      TenancyColumns = tenancyColumns;
      Relations = relations;
   }

   public string NamespaceName { get; }
   public string Accessibility { get; }
   public string TableClassName { get; }

   public string TableClassFullName { get; }

   public string EntityName { get; }
   public string DataTypeName { get; }
   public string CreateCommandTypeName { get; }
   public string UpdateCommandTypeName { get; }
   public string RepositoryInterfaceTypeName { get; }
   public string RepositoryTypeName { get; }
   public string SchemaName { get; }
   public string TableName { get; }
   public ImmutableArray<PropertyDefinitionModel> PrimaryKeys { get; }

   public ImmutableArray<PropertyDefinitionModel> DataProperties { get; }
   public ImmutableArray<PropertyDefinitionModel> CreateProperties { get; }

   public ImmutableArray<PropertyDefinitionModel> UpdateProperties => PrimaryKeyConstraint.InStatementOrder.Concat(MutableUpdateProperties).ToImmutableArray();

   public ImmutableArray<PropertyDefinitionModel> LookupProperties { get; }
   public ImmutableArray<PropertyDefinitionModel> MutableUpdateProperties { get; }

   public ImmutableArray<PropertyDefinitionModel> TenancyColumns { get; }

   public ConstrainedColumns PrimaryKeyConstraint => new(PrimaryKeys, TenancyColumnsOutsideKey);

   public ConstrainedColumns LookupConstraint(PropertyDefinitionModel property)
   {
      return new ConstrainedColumns([property], TenancyColumns.Where(x => !ReferenceEquals(x, property)));
   }

   public ConstrainedColumns TenancyConstraint => new([], TenancyColumns);

   private IEnumerable<PropertyDefinitionModel> TenancyColumnsOutsideKey => TenancyColumns.Where(x => !PrimaryKeys.Any(key => ReferenceEquals(key, x)));

   public ImmutableArray<RelationDeclarationModel> Relations { get; }
}

internal sealed class ConstrainedColumns
{
   public ConstrainedColumns(IEnumerable<PropertyDefinitionModel> subject, IEnumerable<PropertyDefinitionModel> tenancy)
   {
      Subject = subject.ToImmutableArray();
      Tenancy = tenancy.ToImmutableArray();
   }

   public ImmutableArray<PropertyDefinitionModel> Subject { get; }

   public ImmutableArray<PropertyDefinitionModel> Tenancy { get; }

   public bool IsEmpty => Subject.IsEmpty && Tenancy.IsEmpty;

   public IEnumerable<PropertyDefinitionModel> InParameterOrder => Tenancy.Concat(Subject);

   public IEnumerable<PropertyDefinitionModel> InStatementOrder => Subject.Concat(Tenancy);
}

internal sealed class RelationDeclarationModel
{
   public RelationDeclarationModel(
      string propertyName,
      string targetClassFullName,
      string targetTypeDisplayName,
      ImmutableArray<RelationKeyPairDeclaration> keyPairs,
      bool isToMany,
      Location? location,
      RelationConditionDeclaration? condition = null
   )
   {
      PropertyName = propertyName;
      TargetClassFullName = targetClassFullName;
      TargetTypeDisplayName = targetTypeDisplayName;
      KeyPairs = keyPairs;
      IsToMany = isToMany;
      Location = location;
      Condition = condition;
   }

   public string PropertyName { get; }

   public string TargetClassFullName { get; }

   public string TargetTypeDisplayName { get; }

   public ImmutableArray<RelationKeyPairDeclaration> KeyPairs { get; }

   public bool IsToMany { get; }
   public Location? Location { get; }

   public RelationConditionDeclaration? Condition { get; }
}

internal sealed class RelationConditionDeclaration
{
   public RelationConditionDeclaration(string bodyText, ImmutableArray<RelationConditionMemberAccess> memberAccesses, Location location)
   {
      BodyText = bodyText;
      MemberAccesses = memberAccesses;
      Location = location;
   }

   public string BodyText { get; }

   public ImmutableArray<RelationConditionMemberAccess> MemberAccesses { get; }

   public Location Location { get; }
}

internal readonly struct RelationConditionMemberAccess
{
   public RelationConditionMemberAccess(bool isDeclaringSide, string memberName, Location location)
   {
      IsDeclaringSide = isDeclaringSide;
      MemberName = memberName;
      Location = location;
   }

   public bool IsDeclaringSide { get; }

   public string MemberName { get; }
   public Location Location { get; }
}

internal readonly struct RelationKeyPairDeclaration
{
   public RelationKeyPairDeclaration(string? declaringPropertyName, string? targetPropertyName, Location? location)
   {
      DeclaringPropertyName = declaringPropertyName;
      TargetPropertyName = targetPropertyName;
      Location = location;
   }

   public string? DeclaringPropertyName { get; }

   public string? TargetPropertyName { get; }

   public Location? Location { get; }
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
      bool isTenancy,
      bool typeCanHoldNull,
      NullabilityClaim nullability,
      bool requiresNullForgivingInitializer,
      ColumnStorage storage
   )
   {
      PropertyName = propertyName;
      ParameterName = parameterName;
      TypeName = typeName;
      ColumnName = columnName;
      IsPrimaryKey = isPrimaryKey;
      IsUnique = isUnique;
      IsGenerated = isGenerated;
      IsTenancy = isTenancy;
      TypeCanHoldNull = typeCanHoldNull;
      Nullability = nullability;
      RequiresNullForgivingInitializer = requiresNullForgivingInitializer;
      Storage = storage;
   }

   public string PropertyName { get; }
   public string ParameterName { get; }
   public string TypeName { get; }
   public string ColumnName { get; }
   public bool IsPrimaryKey { get; }
   public bool IsUnique { get; }
   public bool IsGenerated { get; }

   public bool IsTenancy { get; }

   public bool TypeCanHoldNull { get; }

   public NullabilityClaim Nullability { get; }

   public bool RequiresNullForgivingInitializer { get; }

   public ColumnStorage Storage { get; }
}
