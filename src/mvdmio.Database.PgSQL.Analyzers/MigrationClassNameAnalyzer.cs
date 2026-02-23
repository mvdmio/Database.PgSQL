using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    Roslyn analyzer that warns when a class implementing <c>IDbMigration</c> does not follow
///    the required naming convention <c>_{identifier}_{name}</c> (e.g. <c>_202310191050_AddUsersTable</c>).
/// </summary>
/// <remarks>
///    The convention is required because <c>IDbMigration</c> provides default implementations of
///    <c>Identifier</c> and <c>Name</c> that are parsed directly from the class name at runtime.
///    A class that does not follow the convention will throw a <see cref="System.FormatException"/> at
///    runtime when those default properties are first accessed.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigrationClassNameAnalyzer : DiagnosticAnalyzer
{
   /// <summary>Diagnostic ID for an invalid migration class name.</summary>
   public const string DiagnosticId = "PGSQL0001";

   private const string CATEGORY = "Naming";
   private const string IDBMIGRATION_INTERFACE_NAME = "IDbMigration";
   private const string IDBMIGRATION_FULL_NAMESPACE = "mvdmio.Database.PgSQL.Migrations.Interfaces";

   // Matches: optional leading underscore, exactly 12 digits, underscore, at least one more char
   private static readonly Regex _migrationNameRegex = new(@"^_?(\d{12})_(.+)$", RegexOptions.Compiled);

   private static readonly DiagnosticDescriptor _rule = new(
      id: DiagnosticId,
      title: "Migration class name does not follow the required convention",
      messageFormat: "'{0}' implements IDbMigration but its name does not match the required format '_{1}_{2}' (e.g. '_202310191050_AddUsersTable'). " +
                     "The default Identifier and Name implementations parse these values from the class name.",
      category: CATEGORY,
      defaultSeverity: DiagnosticSeverity.Warning,
      isEnabledByDefault: true,
      description: "Classes implementing IDbMigration must be named using the pattern '_{YYYYMMddHHmm}_{MigrationName}' " +
                   "so that the default Identifier and Name property implementations can extract their values from the class name."
   );

   /// <inheritdoc/>
   public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(_rule);

   /// <inheritdoc/>
   public override void Initialize(AnalysisContext context)
   {
      context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
      context.EnableConcurrentExecution();
      context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
   }

   private static void AnalyzeNamedType(SymbolAnalysisContext context)
   {
      var namedType = (INamedTypeSymbol) context.Symbol;

      // Only care about concrete classes (not abstract, not interfaces, not structs)
      if (namedType.TypeKind != TypeKind.Class || namedType.IsAbstract)
         return;

      if (!ImplementsIDbMigration(namedType))
         return;

      var className = namedType.Name;

      if (_migrationNameRegex.IsMatch(className))
         return;

      // The class name is invalid — emit a diagnostic on the class declaration
      var diagnostic = Diagnostic.Create(
         _rule,
         namedType.Locations[0],
         className,
         "{YYYYMMddHHmm}",
         "{MigrationName}"
      );

      context.ReportDiagnostic(diagnostic);
   }

   private static bool ImplementsIDbMigration(INamedTypeSymbol type)
   {
      return type.AllInterfaces.Any(x => x.Name == IDBMIGRATION_INTERFACE_NAME && x.ContainingNamespace.ToDisplayString() == IDBMIGRATION_FULL_NAMESPACE);
   }
}
