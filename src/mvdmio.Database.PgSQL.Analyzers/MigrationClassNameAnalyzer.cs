using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace mvdmio.Database.PgSQL.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigrationClassNameAnalyzer : DiagnosticAnalyzer
{
   [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "DiagnosticId is the name every Roslyn analyzer exposes its ID under; UPPER_SNAKE_CASE would fight that idiom, and the member is public, so renaming it would break surface for housekeeping.")]
   public const string DiagnosticId = "PGSQL0001";

   private const string CATEGORY = "Naming";

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

   public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(_rule);

   public override void Initialize(AnalysisContext context)
   {
      context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
      context.EnableConcurrentExecution();
      context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
   }

   private static void AnalyzeNamedType(SymbolAnalysisContext context)
   {
      var namedType = (INamedTypeSymbol)context.Symbol;

      if (namedType.TypeKind != TypeKind.Class || namedType.IsAbstract)
         return;

      if (MigrationInterface.FindOn(namedType) is null)
         return;

      var className = namedType.Name;

      if (_migrationNameRegex.IsMatch(className))
         return;

      var diagnostic = Diagnostic.Create(
         _rule,
         namedType.Locations[0],
         className,
         "{YYYYMMddHHmm}",
         "{MigrationName}"
      );

      context.ReportDiagnostic(diagnostic);
   }
}
