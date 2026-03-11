using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    Warns when a class marked with <c>TableAttribute</c> does not end with <c>Table</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TableDefinitionClassNameAnalyzer : DiagnosticAnalyzer
{
   private const string TABLE_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.TableAttribute";

   /// <inheritdoc/>
   public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
      ImmutableArray.Create(TableRepositoryDiagnostics.TableClassNameShouldEndWithTable);

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

      if (namedType.TypeKind != TypeKind.Class || namedType.IsAbstract)
         return;

      if (!HasTableAttribute(namedType))
         return;

      if (namedType.Name.EndsWith("Table", StringComparison.Ordinal))
         return;

      context.ReportDiagnostic(Diagnostic.Create(
         TableRepositoryDiagnostics.TableClassNameShouldEndWithTable,
         namedType.Locations[0],
         namedType.Name
      ));
   }

   private static bool HasTableAttribute(INamedTypeSymbol type)
   {
      return type.GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() == TABLE_ATTRIBUTE_FULL_NAME);
   }
}
