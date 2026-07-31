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
      ImmutableArray<PropertyDefinitionModel> updateProperties,
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
      UpdateProperties = updateProperties;
      LookupProperties = lookupProperties;
      MutableUpdateProperties = mutableUpdateProperties;
      TenancyColumns = tenancyColumns;
      Relations = relations;
   }

   public string NamespaceName { get; }
   public string Accessibility { get; }
   public string TableClassName { get; }

   /// <summary>The namespace-qualified name of the table definition class, which is how a relation names its target.</summary>
   public string TableClassFullName { get; }

   public string EntityName { get; }
   public string DataTypeName { get; }
   public string CreateCommandTypeName { get; }
   public string UpdateCommandTypeName { get; }
   public string RepositoryInterfaceTypeName { get; }
   public string RepositoryTypeName { get; }
   public string SchemaName { get; }
   public string TableName { get; }
   /// <summary>
   ///    The properties forming the primary key, in key order — which is the order they were declared in, and which
   ///    fixes the parameter order of the generated lookup and delete.
   /// </summary>
   public ImmutableArray<PropertyDefinitionModel> PrimaryKeys { get; }

   public ImmutableArray<PropertyDefinitionModel> DataProperties { get; }
   public ImmutableArray<PropertyDefinitionModel> CreateProperties { get; }
   public ImmutableArray<PropertyDefinitionModel> UpdateProperties { get; }

   /// <summary>
   ///    The <c>[Unique]</c> properties, each of which gets a lookup and a delete named after itself. The primary key is
   ///    not among them: its pair is named after the key, so that every repository names it the same way.
   /// </summary>
   public ImmutableArray<PropertyDefinitionModel> LookupProperties { get; }
   public ImmutableArray<PropertyDefinitionModel> MutableUpdateProperties { get; }

   /// <summary>
   ///    The columns declared <c>[Column(Tenancy = true)]</c>, in declaration order — the order every generated member
   ///    constrains them in, and the order their parameters are added in.
   /// </summary>
   public ImmutableArray<PropertyDefinitionModel> TenancyColumns { get; }

   /// <summary>
   ///    The relations declared on this table, as declared. Whether each one resolves is decided once every table has
   ///    been parsed — see <see cref="RelationResolver" />.
   /// </summary>
   public ImmutableArray<RelationDeclarationModel> Relations { get; }
}

/// <summary>
///    One relation as it was declared, before anything cross-table has been checked.
/// </summary>
internal sealed class RelationDeclarationModel
{
   public RelationDeclarationModel(
      string propertyName,
      string targetClassFullName,
      string targetTypeDisplayName,
      ImmutableArray<string> foreignKeyPropertyNames,
      bool isToMany,
      Location? location
   )
   {
      PropertyName = propertyName;
      TargetClassFullName = targetClassFullName;
      TargetTypeDisplayName = targetTypeDisplayName;
      ForeignKeyPropertyNames = foreignKeyPropertyNames;
      IsToMany = isToMany;
      Location = location;
   }

   public string PropertyName { get; }

   /// <summary>The namespace-qualified name of the target table definition class.</summary>
   public string TargetClassFullName { get; }

   /// <summary>The target as the developer wrote it, for a diagnostic message to quote.</summary>
   public string TargetTypeDisplayName { get; }

   /// <summary>
   ///    The foreign-key property names as declared, in the order they are paired against the target's primary key.
   /// </summary>
   public ImmutableArray<string> ForeignKeyPropertyNames { get; }

   public bool IsToMany { get; }
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
      bool isNullable,
      bool isDeclaredNotNull,
      string? nullabilityContradiction,
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
      IsNullable = isNullable;
      IsDeclaredNotNull = isDeclaredNotNull;
      NullabilityContradiction = nullabilityContradiction;
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

   /// <summary>
   ///    Whether this column carries the tenant. More than one column on a table may say so; each is constrained
   ///    independently by every generated member, in declaration order.
   /// </summary>
   public bool IsTenancy { get; }

   /// <summary>Whether the property can hold null, which a primary key member may not.</summary>
   public bool IsNullable { get; }

   /// <summary>
   ///    Whether the definition claims the column cannot hold null — a separate notion from <see cref="IsNullable" />,
   ///    which stays the type-level fact the primary-key rule is about. This one also answers for a non-nullable
   ///    reference type in a nullable-oblivious file, and it is what a <c>[Column]</c> nullability argument overrides.
   /// </summary>
   public bool IsDeclaredNotNull { get; }

   /// <summary>
   ///    Why the declared nullability cannot be honoured, or <see langword="null" /> when nothing contradicts. A
   ///    contradiction is reported and then dropped, leaving <see cref="IsDeclaredNotNull" /> at what the property's
   ///    type and key membership settle on their own.
   /// </summary>
   public string? NullabilityContradiction { get; }

   public bool RequiresNullForgivingInitializer { get; }

   /// <summary>
   ///    What the definition says about how this column is stored, and what follows from that for each surface. The one
   ///    place both the emitted parameter binding and the emitted query mapping read from, so the two cannot disagree.
   /// </summary>
   public ColumnStorage Storage { get; }
}
