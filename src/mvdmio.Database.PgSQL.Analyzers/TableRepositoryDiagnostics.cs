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

   public const string COLLISION_REASON_NON_PARTIAL_TYPE = "that name is already used by a non-partial type in the same namespace";

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
      messageFormat: "'{0}.{1}' has type '{2}', which the query surface cannot map; the warning stays even after a conversion is registered with LinqDatabaseConnector.ConfigureMappingSchema, because the analyzer cannot see the registration, so suppress it yourself with '#pragma warning disable PGSQL0011' or NoWarn for a type you registered a conversion for",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Warning,
      isEnabledByDefault: true,
      description: "Query() can only translate property types the query surface knows how to convert. A conversion registered with LinqDatabaseConnector.ConfigureMappingSchema does not clear this warning, because the analyzer cannot see the registration. Suppress it yourself with '#pragma warning disable PGSQL0011' or NoWarn for a type you registered a conversion for."
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
      messageFormat: "'{0}.{1}' has type '{2}'; a relation property must be a relation definition or a list, collection or sequence of one",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A relation property states its target and its cardinality through its own type, so only a class deriving RelationDefinition<,> or a supported collection of one can carry a relation."
   );

   public static readonly DiagnosticDescriptor UnsupportedRelationPropertyShape = new(
      id: "PGSQL0017",
      title: "Unsupported relation property shape",
      messageFormat: "'{0}.{1}' must be an instance property with a getter and a setter of any accessibility, and cannot be an indexer",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A relation property is purely declarative — nothing ever reads or writes it at run time, only its type identifies the relation — so unlike a mapped column, its own accessibility is never checked. A getter and a setter still have to exist so the generator can be sure it is a real property rather than a computed member, and it cannot be an indexer."
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

   public static readonly DiagnosticDescriptor NullablePrimaryKeyProperty = new(
      id: "PGSQL0020",
      title: "Primary key property cannot be nullable",
      messageFormat: "'{0}.{1}' is marked [PrimaryKey] but its type '{2}' is nullable",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A nullable key member is a key the database would reject, and it is also what would let the query surface widen a relation's join with an 'or both are null' alternative, which costs the join its index. Refusing it here makes that shape unreachable."
   );

   public const string NULLABILITY_REASON_NOT_NULL_OVER_A_NULLABLE_TYPE = "NotNull says it cannot hold null, but its type can";

   public const string NULLABILITY_REASON_NULL_OVER_A_NON_NULLABLE_VALUE_TYPE = "Null says it can hold null, but a non-nullable value type cannot";

   public const string NULLABILITY_REASON_BOTH_DIRECTIONS = "Null and NotNull are both set, and they cannot both be true";

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

   public static readonly DiagnosticDescriptor RefusedStorageClaim = new(
      id: "PGSQL0022",
      title: "Storage claim cannot be honoured for the property's type",
      messageFormat: "'{0}.{1}' claims StoredAs = {2}, which cannot be honoured for '{3}'; store the value in a property of a matching type, or claim {4}",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A storage claim states how the column is represented; it does not ask for a conversion. A claim joins the refused set only once a test demonstrates it failing, so this refuses few combinations by design and names the legal ones instead of listing the illegal ones. The claim is dropped and the column is bound the way an unclaimed one would be."
   );

   public const string STORAGE_ALTERNATIVES_FOR_A_STRING = "Text, Json or Jsonb";

   public static readonly DiagnosticDescriptor UnwritablePropertyType = new(
      id: "PGSQL0023",
      title: "Property type cannot be written by a generated repository",
      messageFormat: "'{0}.{1}' has type '{2}', which no PostgreSQL type accepts; use {3} instead",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "The driver registers no integer or numeric mapping for the unsigned integer types, by inference or with an explicit type, so a repository over one of them reads and filters and then throws on every insert and update. Refused here rather than at run time. Separate from PGSQL0011, which a registered conversion can be suppressed for, because here there is no PostgreSQL type to convert to."
   );

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

   public static readonly DiagnosticDescriptor RelationCouldReachAcrossTenants = new(
      id: "PGSQL0027",
      title: "Relation could reach across tenants",
      messageFormat: "'{0}.{1}' does not pin tenancy column '{2}' across the join, so the relation can reach another tenant's rows",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Warning,
      isEnabledByDefault: true,
      description: "A tenancy column appearing on either side of a relation's joined key pairs must be paired with a tenancy column on the other side, or the join can pull another tenant's related rows — and a tenancy column that sits outside every pair entirely is paired with nothing, which is the same failure. Checked pair by pair and direction-free, so it covers the declaring side as well as the target side. Reported once per unpinned tenancy column on either table."
   );

   public static readonly DiagnosticDescriptor RelationDeclaringTableMismatch = new(
      id: "PGSQL0028",
      title: "Relation declaring table mismatch",
      messageFormat: "'{0}.{1}' is declared on '{0}', but its relation definition's TDeclaring type argument is '{2}'",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A relation definition's TDeclaring type argument must be the table definition the relation property is declared on, so the join is always between the two tables the developer meant."
   );

   public static readonly DiagnosticDescriptor RelationStatesNoKeys = new(
      id: "PGSQL0029",
      title: "Relation states no keys",
      messageFormat: "'{0}.{1}' declares a relation whose Keys override states no pairs, which would register a cross join",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A relation definition's Keys override must state at least one column pair. There is no sensible default for zero pairs, because that is a cross join rather than a relation."
   );

   public static readonly DiagnosticDescriptor RelationKeyIsNotAColumnReference = new(
      id: "PGSQL0030",
      title: "Relation key is not a column reference",
      messageFormat: "'{0}.{1}' declares a relation key pair whose side is not a direct reference to a mapped column",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "Each side of a relation key pair must be a direct property reference on its own table definition — Key(x => x.Column, y => y.Column) — so the generator can turn it into a join condition rather than having to evaluate an arbitrary expression."
   );

   public static readonly DiagnosticDescriptor RelationConditionCannotBeCarried = new(
      id: "PGSQL0032",
      title: "Relation condition cannot be carried",
      messageFormat: "'{0}.{1}' declares a relation condition that touches '{2}.{3}', which has no counterpart on '{2}'s generated data type",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A relation condition's body is lifted into the emitted join, with its two parameters rewritten from Table definition types to generated data types. A member touched directly on either parameter must exist on that table's generated data type — a mapped column or another relation property — or the lift would fail inside generated source with no line in the developer's own code to fix."
   );

   public static readonly DiagnosticDescriptor RelationAttributeOnNonRelationProperty = new(
      id: "PGSQL0033",
      title: "Relation attribute on a non-relation property",
      messageFormat: "'{0}.{1}' carries [Relation], but its type is not a relation definition or a supported collection of one",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "[Relation] is an optional marker: a property is a Relation because its type derives from RelationDefinition<,>, or is a supported collection of one, not because it carries this attribute. Writing it on a property whose type is neither would let the attribute say something untrue."
   );

   public static readonly DiagnosticDescriptor RelationToOneRowMayReachSeveral = new(
      id: "PGSQL0031",
      title: "Relation to one row may reach several",
      messageFormat: "'{0}.{1}' declares a relation to one row, but its key pairs contain nothing '{2}' claims unique — its primary key or a [Unique] column — so it may reach an arbitrary one of several rows",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Warning,
      isEnabledByDefault: true,
      description: "A relation to one row is a claim, exactly like every other claim a Table definition makes, so pairing against nothing the target claims unique is a warning rather than an error — a relation whose Relation condition makes the pairing unique still builds. A superset of a unique set is still unique and reports nothing."
   );

   public static readonly DiagnosticDescriptor RelationMayResolveEveryKind = new(
      id: "PGSQL0034",
      title: "Relation may resolve every kind",
      messageFormat: "'{0}.{1}' pairs the same key columns as another relation on '{0}' that declares a condition, but states none itself, so it may resolve every kind the condition would otherwise narrow",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Warning,
      isEnabledByDefault: true,
      description: "Two relations on the same table can pair the same columns and still reach different rows, each narrowed by its own condition. Where one of them declares no condition at all, it silently returns every kind the conditioned ones distinguish between — a forgotten condition rather than a deliberate, unconditioned relation."
   );

   public static readonly DiagnosticDescriptor RelationKeyPairBothNullable = new(
      id: "PGSQL0035",
      title: "Relation key pair can both hold null",
      messageFormat: "'{0}.{1}' pairs '{2}' against '{3}.{4}', and both can hold null; give one side a type that cannot hold null, claim [Column(NotNull = true)] on a side whose type cannot say it, or pair a column that cannot hold null",
      category: CATEGORY_GENERATION,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A Relation key pair whose two columns can both hold null widens the join the query provider emits into 'equal, or both are null', which joins every null on one side to every null on the other and loses the index behind either column. The rule reads the Nullability claim each side registers — the same claim the query provider is told — rather than the property's C# type. Which fix applies depends on what the side's type already says: where the type can hold null the type is the fix, because [Column(NotNull = true)] over it contradicts the type and is refused as PGSQL0021 rather than believed; where the type says nothing at all — an unannotated reference type in a file with nullable annotations switched off — or where the column is claimed [Column(Null = true)] over a type that cannot hold null, the claim is the only thing that can carry the fact and stating [Column(NotNull = true)] clears this. Whether either column is [Unique] does not matter: a not-null foreign key paired against a nullable [Unique] column is left alone, because the equality join it emits simply cannot reach a row whose unique column is null."
   );
}
