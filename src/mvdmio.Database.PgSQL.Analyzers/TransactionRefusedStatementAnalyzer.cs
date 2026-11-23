using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace mvdmio.Database.PgSQL.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TransactionRefusedStatementAnalyzer : DiagnosticAnalyzer
{
   [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "DiagnosticId is the name every Roslyn analyzer exposes its ID under; UPPER_SNAKE_CASE would fight that idiom, and the member is public, so renaming it would break surface for housekeeping.")]
   public const string DiagnosticId = "PGSQL0039";

   private const string CATEGORY = "Usage";
   private const string RUNS_IN_TRANSACTION_NAME = "RunsInTransaction";
   private const string INTERPOLATION_HOLE_WORD = "_hole_";

   private static readonly DiagnosticDescriptor _rule = new(
      id: DiagnosticId,
      title: "Migration runs a statement PostgreSQL refuses inside a transaction",
      messageFormat: "Migration '{0}' runs '{1}', which PostgreSQL refuses inside a transaction. Add 'public bool RunsInTransaction => false;' to the migration.",
      category: CATEGORY,
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "A migration runs in a transaction unless it overrides RunsInTransaction to return false. " +
                   "PostgreSQL refuses statements such as CREATE INDEX CONCURRENTLY and VACUUM inside a transaction, so the migration would fail when it is applied."
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

      var migrationInterface = MigrationInterface.FindOn(namedType);

      if (migrationInterface is null || !RunsInTransaction(context.Compilation, namedType, migrationInterface))
         return;

      foreach (var reference in namedType.DeclaringSyntaxReferences)
      {
         foreach (var node in reference.GetSyntax(context.CancellationToken).DescendantNodes())
         {
            var statement = ReadText(node) is { } text ? TransactionRefusedStatements.FindFirst(text) : null;

            if (statement is not null)
               context.ReportDiagnostic(Diagnostic.Create(_rule, node.GetLocation(), namedType.Name, statement));
         }
      }
   }

   private static string? ReadText(SyntaxNode node)
   {
      return node switch
      {
         LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
         InterpolatedStringExpressionSyntax interpolated => JoinInterpolatedText(interpolated),
         _ => null
      };
   }

   private static string JoinInterpolatedText(InterpolatedStringExpressionSyntax interpolated)
   {
      var text = new StringBuilder();

      foreach (var content in interpolated.Contents)
      {
         text.Append(content is InterpolatedStringTextSyntax literal ? literal.TextToken.ValueText : INTERPOLATION_HOLE_WORD);
      }

      return text.ToString();
   }

   private static bool RunsInTransaction(Compilation compilation, INamedTypeSymbol migration, INamedTypeSymbol migrationInterface)
   {
      var interfaceMember = migrationInterface.GetMembers(RUNS_IN_TRANSACTION_NAME).OfType<IPropertySymbol>().FirstOrDefault();

      if (interfaceMember is null)
         return false;

      if (migration.FindImplementationForInterfaceMember(interfaceMember) is not IPropertySymbol implementation)
         return !interfaceMember.IsAbstract;

      var isInterfaceDefault = SymbolEqualityComparer.Default.Equals(implementation.ContainingType, migrationInterface);

      if (isInterfaceDefault)
         return !implementation.IsAbstract;

      var expression = ConstantExpressionOf(implementation);

      if (expression is null)
         return false;

#pragma warning disable RS1030 // The implementation can sit in another syntax tree of the compilation, which the context's own model does not cover.
      var model = compilation.GetSemanticModel(expression.SyntaxTree);
#pragma warning restore RS1030

      return model.GetConstantValue(expression).Value is true;
   }

   private static ExpressionSyntax? ConstantExpressionOf(IPropertySymbol property)
   {
      if (property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not PropertyDeclarationSyntax declaration)
         return null;

      if (declaration.ExpressionBody is not null)
         return declaration.ExpressionBody.Expression;

      var accessors = declaration.AccessorList?.Accessors ?? default;
      var getter = accessors.FirstOrDefault(x => x.IsKind(SyntaxKind.GetAccessorDeclaration));

      if (getter is null)
         return null;

      if (getter.ExpressionBody is not null)
         return getter.ExpressionBody.Expression;

      if (getter.Body is not null)
         return getter.Body.Statements.Count == 1 && getter.Body.Statements[0] is ReturnStatementSyntax returnStatement ? returnStatement.Expression : null;

      return accessors.Count == 1 ? declaration.Initializer?.Value : null;
   }
}
