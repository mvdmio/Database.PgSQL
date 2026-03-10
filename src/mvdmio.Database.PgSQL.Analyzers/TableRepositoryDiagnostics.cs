using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

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
}
