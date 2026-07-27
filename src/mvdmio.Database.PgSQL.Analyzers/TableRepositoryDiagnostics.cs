using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class TableRepositoryDiagnostics
{
   private const string CATEGORY_GENERATION = "Generation";
   private const string CATEGORY_NAMING = "Naming";

   public static readonly DiagnosticDescriptor TableClassNameShouldEndWithTable = new(
      id: "PGSQL0002",
      title: "Table definition class name should end with Table",
      messageFormat: "'{0}' is decorated with [Table] but its name does not end with 'Table'",
      category: CATEGORY_NAMING,
      defaultSeverity: DiagnosticSeverity.Warning,
      isEnabledByDefault: true,
      description: "Classes annotated with [Table] should end with 'Table' so generated types have predictable names."
   );

   public static readonly DiagnosticDescriptor TableClassMustBePartial = new(
      id: "PGSQL0003",
      title: "Table definition class must be partial",
      messageFormat: "'{0}' is decorated with [Table] but is not declared partial",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "Classes annotated with [Table] must be partial so generated companion types can extend the model safely."
   );

   public static readonly DiagnosticDescriptor TableClassMustHaveSinglePrimaryKey = new(
      id: "PGSQL0004",
      title: "Table definition must declare exactly one primary key",
      messageFormat: "'{0}' must declare exactly one property with [PrimaryKey], but found {1}",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "Generated repositories require exactly one primary key property."
   );

   public static readonly DiagnosticDescriptor DuplicateMappedColumnName = new(
      id: "PGSQL0005",
      title: "Duplicate mapped column name",
      messageFormat: "'{0}' maps multiple properties to the database column '{1}'",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "Each generated property must map to a unique database column name."
   );

   public static readonly DiagnosticDescriptor DuplicateLookupMethodName = new(
      id: "PGSQL0006",
      title: "Duplicate generated lookup method name",
      messageFormat: "'{0}' would generate duplicate repository lookup methods for property '{1}'",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "Primary key and unique properties must produce distinct repository method names."
   );

   public static readonly DiagnosticDescriptor NoUpdatableColumns = new(
      id: "PGSQL0007",
      title: "Table definition has no updatable columns",
      messageFormat: "'{0}' has no mutable non-generated columns, so an update command cannot be generated",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "Generated update commands require at least one mutable non-generated column besides the primary key."
   );

   public static readonly DiagnosticDescriptor InvalidTableName = new(
      id: "PGSQL0008",
      title: "Invalid table name",
      messageFormat: "'{0}' has an invalid [Table] value '{1}'; use 'table' or 'schema.table'",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "The [Table] attribute must specify either a table name or a schema-qualified table name."
   );

   public static readonly DiagnosticDescriptor UnsupportedPropertyShape = new(
      id: "PGSQL0009",
      title: "Unsupported table property shape",
      messageFormat: "'{0}.{1}' must be a public instance property with a public getter and setter and cannot be an indexer",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "Generated repositories only support public instance properties with public getters and setters."
   );

   public static readonly DiagnosticDescriptor GeneratedTypeNameCollision = new(
      id: "PGSQL0010",
      title: "Generated type name collision",
      messageFormat: "'{0}' cannot generate type '{1}' because that name is already used by a non-partial type in the same namespace",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "Generated repository companion types require unique names or an existing partial class with the same name."
   );

   public static readonly DiagnosticDescriptor UnmappableQueryPropertyType = new(
      id: "PGSQL0011",
      title: "Property type cannot be mapped by the query surface",
      messageFormat: "'{0}.{1}' has type '{2}', which the query surface cannot map; register a conversion with LinqDatabaseConnector.ConfigureMappingSchema",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Warning,
      isEnabledByDefault: true,
      description: "Query() can only translate property types the query surface knows how to convert. Other types need a conversion registered through the mapping hook."
   );

   // The relation diagnostics below drop only the relation they describe and let the rest of the table generate,
   // unlike every diagnostic above. Abandoning the table would suppress its generated data type and bury the one
   // message describing the actual mistake under type-not-found errors from everything that names that type.

   public static readonly DiagnosticDescriptor RelationForeignKeyNotFound = new(
      id: "PGSQL0012",
      title: "Relation foreign key property not found",
      messageFormat: "'{0}.{1}' names foreign key property '{2}', which '{3}' does not declare as a mapped column",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A relation is resolved through a foreign key property: on the declaring type for a relation to one row, and on the target type for a relation to many."
   );

   public static readonly DiagnosticDescriptor RelationForeignKeyTypeMismatch = new(
      id: "PGSQL0013",
      title: "Relation foreign key type cannot match the primary key",
      messageFormat: "'{0}.{1}' joins foreign key '{2}' of type '{3}' to primary key '{4}' of type '{5}'",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A relation always joins its foreign key to the target's primary key, so the two must have the same type apart from nullability."
   );

   public static readonly DiagnosticDescriptor RelationTargetIsNotATableDefinition = new(
      id: "PGSQL0014",
      title: "Relation target is not a table definition",
      messageFormat: "'{0}.{1}' declares a relation to '{2}', which is not a table definition in this compilation",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A relation target must be a class annotated with [Table] in the same compilation, so both ends of the relation are registered together."
   );

   public static readonly DiagnosticDescriptor RelationToOneMustBeNullable = new(
      id: "PGSQL0015",
      title: "Relation to one row must be nullable",
      messageFormat: "'{0}.{1}' declares a relation to one row and must be nullable, because a relation is always an outer join",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A foreign key pointing at a missing row yields no related row, so a relation to one row promises more than it can deliver unless it is nullable."
   );

   public static readonly DiagnosticDescriptor UnsupportedRelationPropertyType = new(
      id: "PGSQL0016",
      title: "Unsupported relation property type",
      messageFormat: "'{0}.{1}' has type '{2}'; a relation property must be a table definition or a list, collection or sequence of one",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A relation property states its target and its cardinality through its own type, so only a table definition or a supported collection of one can carry a relation."
   );

   public static readonly DiagnosticDescriptor UnsupportedRelationPropertyShape = new(
      id: "PGSQL0017",
      title: "Unsupported relation property shape",
      messageFormat: "'{0}.{1}' must be a public instance property with a public getter and setter and cannot be an indexer",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A relation property follows the same shape rules as a mapped column so that the generated data type can mirror it."
   );

   public static readonly DiagnosticDescriptor RelationCannotBeAColumn = new(
      id: "PGSQL0018",
      title: "Relation property cannot also be a column",
      messageFormat: "'{0}.{1}' carries both [Relation] and [Column], but a relation is not a column",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A relation property is skipped by column mapping, so naming a column for it describes something that will never be read or written."
   );
}
