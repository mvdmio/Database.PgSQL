﻿using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.SourceGenerators.Attributes;

namespace mvdmio.Database.PgSQL.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public class DbRecordSourceGenerator : IIncrementalGenerator
{
   private static readonly string _tableAttributeName = typeof(TableAttribute).FullName!;
   private static readonly string _columnAttributeName = typeof(ColumnAttribute).FullName!;

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
      var combinedProvider = context.CompilationProvider.Combine(classDeclarations.Collect()) // Collect class declarations into an array
         .Select((data, _) => (Compilation: data.Left,                                        // Compilation from CompilationProvider
               ClassDeclarations: data.Right                                                  // Array of class declarations
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
               var tableAttribute = GetTableAttribute(classSymbol);
               if (tableAttribute is null)
                  continue;

               var columns = GetColumns(context, model, classDeclaration);
               var sourceCode = GenerateSource(classSymbol, columns);
               context.AddSource($"{classSymbol.Name}_Columns.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
         }
      );
   }

   private static TableAttribute? GetTableAttribute(INamedTypeSymbol classSymbol)
   {
      var tableAttribute = classSymbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == _tableAttributeName);

      if (tableAttribute is null)
         return null;

      var tableName = tableAttribute.ConstructorArguments[0].Value?.ToString();
      var schema = tableAttribute.ConstructorArguments[1].Value?.ToString() ?? "public";

      if (tableName is null)
         return null;

      return new TableAttribute(tableName, schema);
   }

   private static List<ColumnInfo> GetColumns(SourceProductionContext context, SemanticModel model, ClassDeclarationSyntax classDeclaration)
   {
      var columnAttributes = new List<ColumnInfo>();

      foreach (var member in classDeclaration.Members.OfType<PropertyDeclarationSyntax>())
      {
         var propertySymbol = model.GetDeclaredSymbol(member) as IPropertySymbol;

         var attributeData = propertySymbol?.GetAttributes().FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == _columnAttributeName);

         if (attributeData?.AttributeClass is null || attributeData.AttributeClass?.Kind == SymbolKind.ErrorType)
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

         var name = attributeData.ConstructorArguments[0].Value as string;
         var isPrimaryKey = attributeData.ConstructorArguments[1].Value as bool? ?? false;
         var isGenerated = attributeData.ConstructorArguments[2].Value as bool? ?? false;

         if (name is null)
            continue;

         var columnAttribute = new ColumnAttribute(name, isPrimaryKey, isGenerated);
         columnAttributes.Add(new ColumnInfo(propertySymbol!, attributeData, columnAttribute));
      }

      return columnAttributes;
   }

   private static string GenerateSource(INamedTypeSymbol classSymbol, List<ColumnInfo> columns)
   {
      var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
      var accessibility = classSymbol.DeclaredAccessibility.ToString().ToLower();
      var className = classSymbol.Name;

      var columnAttributes = columns.Select(x => x.Attribute);

      return $$"""
               using System;
               using mvdmio.Database.PgSQL;

               namespace {{namespaceName}}
               {
                   /// <auto-generated />
                   {{accessibility}} partial class {{className}} : DbRecord
                   {
                      public override object? GetValue(string column)
                      {
                           return column switch
                           {
                              {{string.Join(",\n               ", columns.Select(c => $"\"{c.Attribute.Name}\" => {c.Property.Name}"))}},
                              _ => throw new ArgumentException($"Column '{column}' not found in '{nameof({{className}})}'")
                           };
                      }
                   }
               }
               """;
   }

   private class ColumnInfo
   {
      public IPropertySymbol Property { get; }
      public AttributeData AttributeData { get; }
      public ColumnAttribute Attribute { get; }

      public ColumnInfo(IPropertySymbol property, AttributeData attributeData, ColumnAttribute attribute)
      {
         Property = property;
         AttributeData = attributeData;
         Attribute = attribute;
      }
   }
}