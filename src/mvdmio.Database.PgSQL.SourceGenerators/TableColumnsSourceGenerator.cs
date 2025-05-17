using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace mvdmio.Database.PgSQL.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public class TableColumnsSourceGenerator : IIncrementalGenerator
{
   private const string TABLE_ATTRIBUTE_NAME = "mvdmio.Database.PgSQL.Attributes.TableAttribute";
   private const string COLUMN_ATTRIBUTE_NAME = "mvdmio.Database.PgSQL.Attributes.ColumnAttribute";
   private const string DB_TABLE_TYPE_NAME = "mvdmio.Database.PgSQL.DbTable";

   public void Initialize(IncrementalGeneratorInitializationContext context)
   {
      // Create a syntax provider for class declarations with attributes
      var classDeclarations = context.SyntaxProvider.CreateSyntaxProvider(
         static (node, _) => node is ClassDeclarationSyntax {
            AttributeLists.Count: > 0
         },
         static (ctx, _) => (ClassDeclarationSyntax)ctx.Node
      ).Where(static c => c != null);

      // Combine compilation, class declarations, and attribute type names
      var combinedProvider = context.CompilationProvider
         .Combine(classDeclarations.Collect())   // Collect class declarations into an array
         .Select((data, _) => (
            Compilation: data.Left,        // Compilation from CompilationProvider
            ClassDeclarations: data.Right  // Array of class declarations
         )
      );

      // Generate source code for each class declaration
      context.RegisterSourceOutput(
         combinedProvider,
         static (context, source) => {
            var (compilation, classDeclarations) = source;

            foreach (var classDeclaration in classDeclarations)
            {
               var model = compilation.GetSemanticModel(classDeclaration.SyntaxTree);

               if (model.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
               {
                  context.ReportDiagnostic(
                     Diagnostic.Create(
                        new DiagnosticDescriptor(
                           "TCSG001",
                           "Invalid class symbol",
                           "Could not retrieve symbol for class '{0}'",
                           "SourceGenerator",
                           DiagnosticSeverity.Warning,
                           true
                        ),
                        classDeclaration.GetLocation(),
                        classDeclaration.Identifier.Text
                     )
                  );

                  continue;
               }

               // Check if class is valid (has [Table] or inherits from DbTable)
               if (!IsValidTableClass(classSymbol))
                  continue;

               var columnNames = GetColumnNames(context, model, classDeclaration);
               var sourceCode = GenerateSource(classSymbol, columnNames);
               context.AddSource($"{classSymbol.Name}_Columns.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
         }
      );
   }

   private static bool IsValidTableClass(INamedTypeSymbol classSymbol)
   {
      // Check for [Table] attribute or DbTable base class
      var hasTableAttribute = classSymbol.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString() == TABLE_ATTRIBUTE_NAME);

      var inheritsDbTable = classSymbol.BaseType != null && classSymbol.BaseType.ToDisplayString() == DB_TABLE_TYPE_NAME;

      return hasTableAttribute || inheritsDbTable;
   }

   private static List<string> GetColumnNames(SourceProductionContext context, SemanticModel model, ClassDeclarationSyntax classDeclaration)
   {
      var columnNames = new List<string>();

      foreach (var member in classDeclaration.Members.OfType<PropertyDeclarationSyntax>())
      {
         var propertySymbol = model.GetDeclaredSymbol(member) as IPropertySymbol;

         var columnAttribute = propertySymbol?.GetAttributes().FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == COLUMN_ATTRIBUTE_NAME);

         if (columnAttribute?.AttributeClass is null || columnAttribute.AttributeClass?.Kind == SymbolKind.ErrorType)
         {
            context.ReportDiagnostic(
               Diagnostic.Create(
                  new DiagnosticDescriptor(
                     "TCSG002",
                     "Column attribute value missing",
                     "Property '{0}' has a [Column] attribute but no valid name could be extracted.",
                     "SourceGenerator",
                     DiagnosticSeverity.Warning,
                     true
                  ),
                  member.GetLocation(),
                  member.Identifier.Text
               )
            );
            continue;
         }
            

         var nameArgument = columnAttribute.ConstructorArguments.FirstOrDefault().Value as string;

         if (!string.IsNullOrEmpty(nameArgument))
            columnNames.Add(nameArgument!);
      }

      return columnNames;
   }

   private static string GenerateSource(INamedTypeSymbol classSymbol, List<string> columnNames)
   {
      var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
      var accessibility = classSymbol.DeclaredAccessibility.ToString().ToLower();
      var className = classSymbol.Name;

      var columnArray = string.Join(", ", columnNames.Select(name => $"\"{name}\""));

      return $$"""
         using System;

         namespace {{namespaceName}}
         {
             /// <auto-generated />
             {{accessibility}} partial class {{className}}
             {
                 internal static string[] ColumnNames { get; } = new[] { {{columnArray}} };
             }
         }
         """;
   }
}