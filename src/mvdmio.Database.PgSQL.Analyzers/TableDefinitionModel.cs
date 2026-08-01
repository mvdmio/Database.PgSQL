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

   /// <summary>
   ///    What the update command type carries: every key member, then every tenancy column not already among them,
   ///    then every column the update actually assigns. Wider than <see cref="MutableUpdateProperties" /> by the
   ///    columns the statement only addresses its row by — the <c>WHERE</c> clause needs their values as much as the
   ///    <c>SET</c> list needs the rest.
   /// </summary>
   public ImmutableArray<PropertyDefinitionModel> UpdateProperties => PrimaryKeyConstraint.InStatementOrder.Concat(MutableUpdateProperties).ToImmutableArray();

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
   ///    What <c>GetByPrimaryKeyAsync</c>, <c>DeleteByPrimaryKeyAsync</c> and the update statement's <c>WHERE</c>
   ///    clause constrain: the key, plus every tenancy column not already among it. Where every tenancy column is a key
   ///    member the tenancy half is empty, which is what leaves a table safe by construction with the surface it has
   ///    today.
   /// </summary>
   public ConstrainedColumns PrimaryKeyConstraint => new(PrimaryKeys, TenancyColumnsOutsideKey);

   /// <summary>
   ///    What the <c>[Unique]</c> lookup and delete named after <paramref name="property" /> constrain: that property,
   ///    plus every tenancy column other than it. Where the property carries <c>Tenancy = true</c> itself the tenancy
   ///    half drops it, so its value is taken and constrained once rather than twice.
   /// </summary>
   public ConstrainedColumns LookupConstraint(PropertyDefinitionModel property)
   {
      return new ConstrainedColumns([property], TenancyColumns.Where(x => !ReferenceEquals(x, property)));
   }

   /// <summary>
   ///    What <c>GetAllAsync</c> and <c>Query</c> constrain: the tenancy columns and nothing else. Empty on a table
   ///    that declares none, which is the one member that used to have no predicate to forget.
   /// </summary>
   public ConstrainedColumns TenancyConstraint => new([], TenancyColumns);

   /// <summary>
   ///    The tenancy columns not already a primary-key member, in declaration order. Kept private: what the rest of the
   ///    generator asks for is a whole <see cref="ConstrainedColumns" />, not the ingredient it is mixed from.
   /// </summary>
   private IEnumerable<PropertyDefinitionModel> TenancyColumnsOutsideKey => TenancyColumns.Where(x => !PrimaryKeys.Any(key => ReferenceEquals(key, x)));

   /// <summary>
   ///    The relations declared on this table, as declared. Whether each one resolves is decided once every table has
   ///    been parsed — see <see cref="RelationResolver" />.
   /// </summary>
   public ImmutableArray<RelationDeclarationModel> Relations { get; }
}

/// <summary>
///    The columns one generated member constrains, split into the columns it addresses its row by and the tenancy
///    columns it adds on top of them.
/// </summary>
/// <remarks>
///    One answer to a question the generated signature, the generated SQL and the generated parameter dictionary all
///    ask, so the three cannot come to disagree — a signature taking a tenant the statement never constrains would
///    otherwise compile. They read that answer in two orders, and the difference is deliberate: a caller sees the
///    tenancy half first on every member, while the statement names what it addresses the row by first. Both orders
///    come off the same pair of sequences, so only membership has to be decided once.
/// </remarks>
internal sealed class ConstrainedColumns
{
   public ConstrainedColumns(IEnumerable<PropertyDefinitionModel> subject, IEnumerable<PropertyDefinitionModel> tenancy)
   {
      Subject = subject.ToImmutableArray();
      Tenancy = tenancy.ToImmutableArray();
   }

   /// <summary>
   ///    What the member addresses its row by: the primary key, the <c>[Unique]</c> column it is named after, or
   ///    nothing at all for the two members that constrain only the tenant.
   /// </summary>
   public ImmutableArray<PropertyDefinitionModel> Subject { get; }

   /// <summary>The tenancy columns constrained on top of <see cref="Subject" />, in declaration order.</summary>
   public ImmutableArray<PropertyDefinitionModel> Tenancy { get; }

   /// <summary>Whether the member constrains nothing, which only an untenanted table's <c>GetAllAsync</c> and <c>Query</c> do.</summary>
   public bool IsEmpty => Subject.IsEmpty && Tenancy.IsEmpty;

   /// <summary>Tenancy first and then the subject — the order every generated signature takes these values in.</summary>
   public IEnumerable<PropertyDefinitionModel> InParameterOrder => Tenancy.Concat(Subject);

   /// <summary>
   ///    The subject first and then tenancy — the order the generated statement names them in, which its parameter
   ///    dictionary follows so the two read alike.
   /// </summary>
   public IEnumerable<PropertyDefinitionModel> InStatementOrder => Subject.Concat(Tenancy);
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

   /// <summary>The namespace-qualified name of the target table definition class.</summary>
   public string TargetClassFullName { get; }

   /// <summary>The target as the developer wrote it, for a diagnostic message to quote.</summary>
   public string TargetTypeDisplayName { get; }

   /// <summary>The column pairs read off a relation definition's <c>Keys</c> override, in the order they are written.</summary>
   public ImmutableArray<RelationKeyPairDeclaration> KeyPairs { get; }

   public bool IsToMany { get; }
   public Location? Location { get; }

   /// <summary>
   ///    The relation definition's <c>Condition</c> override, read off its syntax — <see langword="null" /> when the
   ///    override is absent, which is an ordinary relation.
   /// </summary>
   public RelationConditionDeclaration? Condition { get; }
}

/// <summary>
///    A relation definition's <c>Condition</c> override, read off its syntax: the lambda body with its two parameters
///    already rewritten to the names the emitted join lambda uses, and every member touched directly on either
///    parameter, for <c>PGSQL0032</c> to check against that table's generated data type.
/// </summary>
internal sealed class RelationConditionDeclaration
{
   public RelationConditionDeclaration(string bodyText, ImmutableArray<RelationConditionMemberAccess> memberAccesses, Location location)
   {
      BodyText = bodyText;
      MemberAccesses = memberAccesses;
      Location = location;
   }

   /// <summary>
   ///    The condition's body, already rewritten to reference the emitted join lambda's own parameters ("x" and "y")
   ///    rather than the names the developer wrote — ready to be inlined into the join verbatim.
   /// </summary>
   public string BodyText { get; }

   /// <summary>Every member accessed directly on either of the condition's two parameters.</summary>
   public ImmutableArray<RelationConditionMemberAccess> MemberAccesses { get; }

   public Location Location { get; }
}

/// <summary>One member accessed directly on a relation condition's declaring-side or target-side parameter.</summary>
internal readonly struct RelationConditionMemberAccess
{
   public RelationConditionMemberAccess(bool isDeclaringSide, string memberName, Location location)
   {
      IsDeclaringSide = isDeclaringSide;
      MemberName = memberName;
      Location = location;
   }

   /// <summary>Whether this member was accessed on the declaring-side parameter rather than the target-side one.</summary>
   public bool IsDeclaringSide { get; }

   public string MemberName { get; }
   public Location Location { get; }
}

/// <summary>
///    One column pair read off a relation definition's <c>Keys</c> override. Either side is <see langword="null" />
///    when the syntax written there is not a direct reference to a property of the expected parameter — a method
///    call, an indexer, a nested access, or a reference to something other than the lambda's own parameter.
/// </summary>
internal readonly struct RelationKeyPairDeclaration
{
   public RelationKeyPairDeclaration(string? declaringPropertyName, string? targetPropertyName, Location? location)
   {
      DeclaringPropertyName = declaringPropertyName;
      TargetPropertyName = targetPropertyName;
      Location = location;
   }

   /// <summary>The property named on the declaring side of the pair, or <see langword="null" /> if that side is not a direct property reference.</summary>
   public string? DeclaringPropertyName { get; }

   /// <summary>The property named on the target side of the pair, or <see langword="null" /> if that side is not a direct property reference.</summary>
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

   /// <summary>
   ///    Whether this column carries the tenant. More than one column on a table may say so; each is constrained
   ///    independently by every generated member, in declaration order.
   /// </summary>
   public bool IsTenancy { get; }

   /// <summary>
   ///    Whether the property's C# type can hold null, which a primary key member's may not. The type-level fact, and
   ///    deliberately not the negation of <see cref="Nullability" />'s answer: the two disagree wherever a
   ///    <c>[Column]</c> argument overrides the type, and wherever the type states nothing at all.
   /// </summary>
   /// <remarks>
   ///    Read this only where the C# type is itself the fact — the primary-key rule, and the nullability the generated
   ///    signatures mirror. Everything that is about what the <em>column</em> holds reads <see cref="Nullability" />
   ///    instead, which is what the query surface is told. Reading the wrong one of the two is how <c>PGSQL0035</c>
   ///    came to check something the query provider never sees; see ADR 0011.
   /// </remarks>
   public bool TypeCanHoldNull { get; }

   /// <summary>
   ///    What the definition claims about the column holding null, together with the contradiction that claim earned if
   ///    it made one. Carried whole rather than split apart, for the reason <see cref="NullabilityClaim" /> gives.
   /// </summary>
   public NullabilityClaim Nullability { get; }

   public bool RequiresNullForgivingInitializer { get; }

   /// <summary>
   ///    What the definition says about how this column is stored, and what follows from that for each surface. The one
   ///    place both the emitted parameter binding and the emitted query mapping read from, so the two cannot disagree.
   /// </summary>
   public ColumnStorage Storage { get; }
}
