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

   public static readonly DiagnosticDescriptor TableClassMustHaveAPrimaryKey = new(
      id: "PGSQL0004",
      title: "Table definition must declare at least one primary key",
      messageFormat: "'{0}' must declare at least one property with [PrimaryKey]",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "Generated repositories address a row by its primary key, so a table definition without one has no way to look a row up, update it or delete it."
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
      description: "Unique properties must produce distinct repository method names. The primary key does not take part: its lookup is named after the key rather than after a property."
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
      messageFormat: "'{0}.{1}' must be a public instance property with a public getter and a setter of any accessibility, and cannot be an indexer",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "Generated repositories only support public instance properties with a public getter and a setter. The setter may be private, protected or init-only — a table definition is never instantiated, so what its members permit a caller to do describes nothing about the column. A setter has to be there, because a get-only or expression-bodied member is a computed value rather than a column."
   );

   /// <summary>
   ///    The reason a generated name cannot be used, as <see cref="GeneratedNameCollision" />'s third argument. Stated
   ///    here rather than at the call site so both collisions read as one diagnostic with two causes.
   /// </summary>
   public const string COLLISION_REASON_NON_PARTIAL_TYPE = "that name is already used by a non-partial type in the same namespace";

   /// <inheritdoc cref="COLLISION_REASON_NON_PARTIAL_TYPE" />
   public const string COLLISION_REASON_PRIMARY_KEY_LOOKUP = "the primary key's own lookup and delete already take that name";

   public static readonly DiagnosticDescriptor GeneratedNameCollision = new(
      id: "PGSQL0010",
      title: "Generated name collision",
      messageFormat: "'{0}' cannot generate '{1}' because {2}",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A generated companion type needs a free name or an existing partial class of the same name, and a [Unique] property's generated lookup cannot be named after the primary key's."
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
      messageFormat: "'{0}.{1}' joins foreign key '{2}' of type '{3}' to primary key '{4}' of type '{5}' at key position {6}",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A relation always joins its foreign key to the target's primary key, pairing them positionally, so the two must have the same type at every position apart from nullability."
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

   public static readonly DiagnosticDescriptor RelationForeignKeyArityMismatch = new(
      id: "PGSQL0019",
      title: "Relation foreign key does not match the target's primary key arity",
      messageFormat: "'{0}.{1}' names a foreign key of arity {2} ({3}), but the primary key of '{4}' has arity {5}",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A relation pairs its foreign-key properties positionally against the target's primary key, so it must name exactly one per key member."
   );

   // Abandons the table, unlike the relation diagnostics above: a malformed key leaves the lookup, the delete and the
   // update undefined rather than one relation.

   public static readonly DiagnosticDescriptor NullablePrimaryKeyProperty = new(
      id: "PGSQL0020",
      title: "Primary key property cannot be nullable",
      messageFormat: "'{0}.{1}' is marked [PrimaryKey] but its type '{2}' is nullable",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A nullable key member is a key the database would reject, and it is also what would let the query surface widen a relation's join with an 'or both are null' alternative, which costs the join its index. Refusing it here makes that shape unreachable."
   );

   // Abandons nothing, unlike the key diagnostic above. A contradictory nullability claim leaves every generated
   // signature well-defined, so dropping the claim and reporting it is one error rather than a cascade of
   // type-not-found errors across the consumer's own code.

   /// <summary>
   ///    Which contradiction a declared nullability claim is, as <see cref="ContradictoryColumnNullability" />'s third
   ///    argument. Stated here rather than at the call site so all four read as one diagnostic with four causes.
   /// </summary>
   public const string NULLABILITY_REASON_NOT_NULL_OVER_A_NULLABLE_TYPE = "NotNull says it cannot hold null, but its type can";

   /// <inheritdoc cref="NULLABILITY_REASON_NOT_NULL_OVER_A_NULLABLE_TYPE" />
   public const string NULLABILITY_REASON_NULL_OVER_A_NON_NULLABLE_VALUE_TYPE = "Null says it can hold null, but a non-nullable value type cannot";

   /// <inheritdoc cref="NULLABILITY_REASON_NOT_NULL_OVER_A_NULLABLE_TYPE" />
   public const string NULLABILITY_REASON_BOTH_DIRECTIONS = "Null and NotNull are both set, and they cannot both be true";

   /// <inheritdoc cref="NULLABILITY_REASON_NOT_NULL_OVER_A_NULLABLE_TYPE" />
   public const string NULLABILITY_REASON_NULL_ON_A_KEY_MEMBER = "Null says it can hold null, but a [PrimaryKey] member cannot";

   public static readonly DiagnosticDescriptor ContradictoryColumnNullability = new(
      id: "PGSQL0021",
      title: "Contradictory column nullability",
      messageFormat: "'{0}.{1}' declares a column nullability that contradicts itself: {2}",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "[Column]'s Null and NotNull override what a property's type says about the column it maps to, so a claim that contradicts the type, the key or itself says two things at once. The claim is dropped and the column keeps whatever its type and key membership already settle."
   );

   // The three storage diagnostics below abandon nothing either, for the same reason PGSQL0021 does not: a refused
   // claim, an unwritable type and an unrepresentable claim all leave every generated signature well-defined.

   public static readonly DiagnosticDescriptor RefusedStorageClaim = new(
      id: "PGSQL0022",
      title: "Storage claim cannot be honoured for the property's type",
      messageFormat: "'{0}.{1}' claims StoredAs = {2}, which cannot be honoured for '{3}'; store the value in a property of a matching type, or claim {4}",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A storage claim states how the column is represented; it does not ask for a conversion. A claim joins the refused set only once a test demonstrates it failing, so this refuses few combinations by design and names the legal ones instead of listing the illegal ones. The claim is dropped and the column is bound the way an unclaimed one would be."
   );

   /// <summary>
   ///    What a refused claim's fifth argument offers instead. Stated here so the message names a way forward rather than
   ///    only what is wrong.
   /// </summary>
   public const string STORAGE_ALTERNATIVES_FOR_A_STRING = "Text, Json or Jsonb";

   public static readonly DiagnosticDescriptor UnwritablePropertyType = new(
      id: "PGSQL0023",
      title: "Property type cannot be written by a generated repository",
      messageFormat: "'{0}.{1}' has type '{2}', which no PostgreSQL type accepts; use {3} instead",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "The driver registers no integer or numeric mapping for the unsigned integer types, by inference or with an explicit type, so a repository over one of them reads and filters and then throws on every insert and update. Refused here rather than at run time. Separate from PGSQL0011, whose advice — register a conversion — cannot help, because there is no PostgreSQL type to convert to."
   );

   /// <summary>The signed types that cover each refused unsigned one's range, as <see cref="UnwritablePropertyType" />'s fourth argument.</summary>
   public const string WRITABLE_ALTERNATIVES_FOR_UNSIGNED_INTEGERS = "int, long or decimal";

   public static readonly DiagnosticDescriptor UnrepresentableStorageClaim = new(
      id: "PGSQL0024",
      title: "Storage claim has no query surface representation",
      messageFormat: "'{0}.{1}' claims StoredAs = {2}, which the query surface cannot represent; generated commands will use it and Query() will not",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Warning,
      isEnabledByDefault: true,
      description: "A storage claim feeds the parameter binding and the query surface mapping both, which is what stops the two disagreeing about a column. Where the query surface has no equivalent for the claim — the network address and geometry types among them — the claim is honoured on the Dapper surface and left unstated on the other, so the divergence is made visible here rather than left silent."
   );

   // The two diagnostics below abandon the table, the same as a malformed key: generating it anyway would emit
   // precisely the unguarded surface this feature removes, and would do it quietly.

   public static readonly DiagnosticDescriptor NullableTenancyColumn = new(
      id: "PGSQL0025",
      title: "Tenancy column property cannot be nullable",
      messageFormat: "'{0}.{1}' is marked Tenancy = true but can hold null, whether from its type or from a Null = true claim",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A null tenant matches no row, so every generated member over the table would return nothing. This follows the same reasoning that already refuses a nullable primary-key member."
   );

   public static readonly DiagnosticDescriptor GeneratedTenancyColumn = new(
      id: "PGSQL0026",
      title: "Tenancy column property cannot be [Generated]",
      messageFormat: "'{0}.{1}' is marked Tenancy = true but is also [Generated], so it is on no command type for a required property to carry",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A generated column is on no command type, so there is no property to make required — the developer would learn that at run time instead of build time."
   );

   // Unlike the two diagnostics above, this one abandons nothing at all — not the relation, not the table — because a
   // relation to a shared, untenanted table can be legitimate, and per ADR 0005 a relation-level problem drops the
   // relation rather than the table.

   public static readonly DiagnosticDescriptor RelationCouldReachAcrossTenants = new(
      id: "PGSQL0027",
      title: "Relation could reach across tenants",
      messageFormat: "'{0}.{1}' does not pin tenancy column '{2}' across the join, so the relation can reach another tenant's rows",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Warning,
      isEnabledByDefault: true,
      description: "A relation always pairs its foreign key positionally against the other side's primary key. The property paired against a tenancy column must be the other side's own tenancy column, or the join can pull another tenant's related rows — and a tenancy column that sits outside the joined key entirely is paired with nothing, which is the same failure. Reported once per tenancy column that comes out unpinned either way."
   );
}
