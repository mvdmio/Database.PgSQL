using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigrationPartsAnalyzer : DiagnosticAnalyzer
{
   [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "A diagnostic ID is the name every Roslyn analyzer exposes its ID under; UPPER_SNAKE_CASE would fight that idiom, and the member is public, so renaming it would break surface for housekeeping.")]
   public const string PostDeployWithUpDiagnosticId = "PGSQL0036";

   [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "A diagnostic ID is the name every Roslyn analyzer exposes its ID under; UPPER_SNAKE_CASE would fight that idiom, and the member is public, so renaming it would break surface for housekeeping.")]
   public const string UpWithPreDeployDiagnosticId = "PGSQL0037";

   [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "A diagnostic ID is the name every Roslyn analyzer exposes its ID under; UPPER_SNAKE_CASE would fight that idiom, and the member is public, so renaming it would break surface for housekeeping.")]
   public const string NoPreDeployPartDiagnosticId = "PGSQL0038";

   private const string CATEGORY = "Migration";
   private const string UP_METHOD_NAME = "UpAsync";
   private const string PRE_DEPLOY_METHOD_NAME = "PreDeployAsync";
   private const string POST_DEPLOY_METHOD_NAME = "PostDeployAsync";

   private static readonly DiagnosticDescriptor _postDeployWithUpRule = new(
      id: PostDeployWithUpDiagnosticId,
      title: "Migration defines PostDeployAsync together with UpAsync",
      messageFormat: "'{0}' defines both UpAsync and PostDeployAsync. UpAsync is the pre-deploy part of a migration with no post-deploy part: name the pre-deploy part PreDeployAsync.",
      category: CATEGORY,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A migration with a post-deploy part names its pre-deploy part PreDeployAsync. UpAsync stands for a migration that has only a pre-deploy part."
   );

   private static readonly DiagnosticDescriptor _upWithPreDeployRule = new(
      id: UpWithPreDeployDiagnosticId,
      title: "Migration defines both UpAsync and PreDeployAsync",
      messageFormat: "'{0}' defines both UpAsync and PreDeployAsync, which name the same part. Pick one of the two.",
      category: CATEGORY,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "UpAsync and PreDeployAsync both name the pre-deploy part of a migration, so a migration defines only one of them."
   );

   private static readonly DiagnosticDescriptor _noPreDeployPartRule = new(
      id: NoPreDeployPartDiagnosticId,
      title: "Migration defines neither UpAsync nor PreDeployAsync",
      messageFormat: "'{0}' defines neither UpAsync nor PreDeployAsync. Add PreDeployAsync, the pre-deploy part every migration has.",
      category: CATEGORY,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "Every migration has a pre-deploy part, named UpAsync or PreDeployAsync. A migration with only PostDeployAsync has no pre-deploy part."
   );

   public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(_postDeployWithUpRule, _upWithPreDeployRule, _noPreDeployPartRule);

   public override void Initialize(AnalysisContext context)
   {
      context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
      context.EnableConcurrentExecution();
      context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
   }

   private static void AnalyzeNamedType(SymbolAnalysisContext context)
   {
      var namedType = (INamedTypeSymbol)context.Symbol;

      if (!MigrationSymbols.IsConcreteMigration(namedType))
         return;

      var rule = FindViolatedRule(
         definesUp: Defines(namedType, UP_METHOD_NAME),
         definesPreDeploy: Defines(namedType, PRE_DEPLOY_METHOD_NAME),
         definesPostDeploy: Defines(namedType, POST_DEPLOY_METHOD_NAME)
      );

      if (rule is null)
         return;

      context.ReportDiagnostic(Diagnostic.Create(rule, namedType.Locations[0], namedType.Name));
   }

   private static DiagnosticDescriptor? FindViolatedRule(bool definesUp, bool definesPreDeploy, bool definesPostDeploy)
   {
      return (definesUp, definesPreDeploy, definesPostDeploy) switch
      {
         (true, _, true) => _postDeployWithUpRule,
         (true, true, _) => _upWithPreDeployRule,
         (false, false, _) => _noPreDeployPartRule,
         _ => null
      };
   }

   private static bool Defines(INamedTypeSymbol type, string methodName)
   {
      for (var current = type; current is not null; current = current.BaseType)
      {
         if (current.GetMembers().OfType<IMethodSymbol>().Any(method => method.Name == methodName || method.ExplicitInterfaceImplementations.Any(implemented => implemented.Name == methodName)))
            return true;
      }

      return false;
   }
}
