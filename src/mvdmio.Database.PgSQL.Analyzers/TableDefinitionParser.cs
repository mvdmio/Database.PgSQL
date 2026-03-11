using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Text;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class TableDefinitionParser
{
   private const string TABLE_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.TableAttribute";
   private const string PRIMARY_KEY_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.PrimaryKeyAttribute";
   private const string UNIQUE_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.UniqueAttribute";
   private const string COLUMN_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.ColumnAttribute";
   private const string GENERATED_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.GeneratedAttribute";

   private static readonly SymbolDisplayFormat _typeDisplayFormat = new(
      globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
      typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
      genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
      miscellaneousOptions:
      SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
      SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
      SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
   );

   internal sealed class ParseResult
   {
      public ParseResult(TableDefinitionModel? model, ImmutableArray<Diagnostic> diagnostics)
      {
         Model = model;
         Diagnostics = diagnostics;
      }

      public TableDefinitionModel? Model { get; }
      public ImmutableArray<Diagnostic> Diagnostics { get; }
   }

   public static ParseResult Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
   {
      cancellationToken.ThrowIfCancellationRequested();

      var classSymbol = (INamedTypeSymbol)context.TargetSymbol;
      var classSyntax = (ClassDeclarationSyntax)context.TargetNode;
      var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

      var tableAttribute = classSymbol.GetAttributes().FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == TABLE_ATTRIBUTE_FULL_NAME);
      var tableName = tableAttribute?.ConstructorArguments.FirstOrDefault().Value as string;

      if (string.IsNullOrWhiteSpace(tableName) || !TryParseTableName(tableName!, out var schemaName, out var unqualifiedTableName))
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.InvalidTableName,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            tableName ?? string.Empty
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      if (!IsPartial(classSymbol))
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.TableClassMustBePartial,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var allProperties = classSymbol.GetMembers()
         .OfType<IPropertySymbol>()
         .ToImmutableArray();

      var invalidProperties = allProperties
         .Where(ShouldValidateProperty)
         .Where(x => !IsSupportedProperty(x))
         .ToImmutableArray();

      if (invalidProperties.Length > 0)
      {
         foreach (var property in invalidProperties)
         {
            diagnostics.Add(Diagnostic.Create(
               TableRepositoryDiagnostics.UnsupportedPropertyShape,
               property.Locations.FirstOrDefault() ?? classSyntax.Identifier.GetLocation(),
               classSymbol.Name,
               property.Name
            ));
         }

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var properties = allProperties
         .Where(IsSupportedProperty)
         .Select(CreatePropertyModel)
         .ToImmutableArray();

      var primaryKeys = properties.Where(x => x.IsPrimaryKey).ToImmutableArray();
      if (primaryKeys.Length != 1)
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.TableClassMustHaveSinglePrimaryKey,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            primaryKeys.Length
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var duplicateColumn = properties
         .GroupBy(x => x.ColumnName, StringComparer.OrdinalIgnoreCase)
         .FirstOrDefault(x => x.Count() > 1);

      if (duplicateColumn is not null)
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.DuplicateMappedColumnName,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            duplicateColumn.Key
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var primaryKey = primaryKeys[0];
      var lookupProperties = properties.Where(x => x.IsPrimaryKey || x.IsUnique).ToImmutableArray();
      var duplicateLookup = lookupProperties
         .Select(x => $"GetBy{x.PropertyName}Async")
         .GroupBy(x => x, StringComparer.Ordinal)
         .FirstOrDefault(x => x.Count() > 1);

      if (duplicateLookup is not null)
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.DuplicateLookupMethodName,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            duplicateLookup.Key.Replace("GetBy", string.Empty).Replace("Async", string.Empty)
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var mutableUpdateProperties = properties.Where(x => !x.IsPrimaryKey && !x.IsGenerated).ToImmutableArray();
      if (mutableUpdateProperties.Length == 0)
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.NoUpdatableColumns,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var entityName = GetEntityName(classSymbol.Name);
      var generatedTypeNames = new[]
      {
         $"{entityName}Data",
         $"Create{entityName}Command",
         $"Update{entityName}Command",
         $"I{entityName}Repository",
         $"{entityName}Repository"
      };

      var generatedNameCollision = generatedTypeNames.FirstOrDefault(typeName => HasGeneratedTypeNameCollision(classSymbol, typeName));
      if (generatedNameCollision is not null)
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.GeneratedTypeNameCollision,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            generatedNameCollision
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var accessibility = classSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
      var model = new TableDefinitionModel(
         classSymbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : classSymbol.ContainingNamespace.ToDisplayString(),
         accessibility,
         classSymbol.Name,
         entityName,
         $"{entityName}Data",
         $"Create{entityName}Command",
         $"Update{entityName}Command",
         $"I{entityName}Repository",
         $"{entityName}Repository",
         schemaName,
         unqualifiedTableName,
         primaryKey,
         properties,
         properties.Where(x => !x.IsGenerated).ToImmutableArray(),
         ImmutableArray.Create(primaryKey).AddRange(mutableUpdateProperties),
         lookupProperties,
         mutableUpdateProperties
      );

      return new ParseResult(model, diagnostics.ToImmutable());
   }

   private static bool IsPartial(INamedTypeSymbol classSymbol)
   {
      return classSymbol.DeclaringSyntaxReferences
         .Select(x => x.GetSyntax())
         .OfType<ClassDeclarationSyntax>()
         .Any(x => x.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
   }

   private static bool IsSupportedProperty(IPropertySymbol property)
   {
      return !property.IsStatic
             && property.DeclaredAccessibility == Accessibility.Public
             && property.Parameters.Length == 0
             && property.GetMethod?.DeclaredAccessibility == Accessibility.Public
             && property.SetMethod?.DeclaredAccessibility == Accessibility.Public;
   }

   private static bool ShouldValidateProperty(IPropertySymbol property)
   {
      return property.DeclaredAccessibility == Accessibility.Public || HasRelevantAttribute(property);
   }

   private static PropertyDefinitionModel CreatePropertyModel(IPropertySymbol property)
   {
      var columnName = property.GetAttributes()
         .FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == COLUMN_ATTRIBUTE_FULL_NAME)?
         .ConstructorArguments.FirstOrDefault().Value as string;

      return new PropertyDefinitionModel(
         property.Name,
         ToCamelCase(property.Name),
         property.Type.ToDisplayString(_typeDisplayFormat),
         string.IsNullOrWhiteSpace(columnName) ? ToSnakeCase(property.Name) : columnName!,
         HasAttribute(property, PRIMARY_KEY_ATTRIBUTE_FULL_NAME),
         HasAttribute(property, UNIQUE_ATTRIBUTE_FULL_NAME),
         HasAttribute(property, GENERATED_ATTRIBUTE_FULL_NAME),
         property.Type.IsReferenceType && property.NullableAnnotation != NullableAnnotation.Annotated
      );
   }

   private static bool HasAttribute(IPropertySymbol property, string fullName)
   {
      return property.GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() == fullName);
   }

   private static bool HasRelevantAttribute(IPropertySymbol property)
   {
      return HasAttribute(property, PRIMARY_KEY_ATTRIBUTE_FULL_NAME)
             || HasAttribute(property, UNIQUE_ATTRIBUTE_FULL_NAME)
             || HasAttribute(property, COLUMN_ATTRIBUTE_FULL_NAME)
             || HasAttribute(property, GENERATED_ATTRIBUTE_FULL_NAME);
   }

   private static bool HasGeneratedTypeNameCollision(INamedTypeSymbol classSymbol, string typeName)
   {
      return classSymbol.ContainingNamespace
         .GetTypeMembers(typeName)
         .Any(type => !CanMergeWithGeneratedType(type));
   }

   private static bool CanMergeWithGeneratedType(INamedTypeSymbol type)
   {
      if (type.TypeKind != TypeKind.Class)
         return false;

      return type.DeclaringSyntaxReferences
         .Select(x => x.GetSyntax())
         .OfType<ClassDeclarationSyntax>()
         .All(x => x.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
   }

   private static string GetEntityName(string className)
   {
      return className.EndsWith("Table", StringComparison.Ordinal) && className.Length > "Table".Length
         ? className.Substring(0, className.Length - "Table".Length)
         : className;
   }

   private static bool TryParseTableName(string value, out string schemaName, out string tableName)
   {
      var parts = value.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
         .Select(x => x.Trim())
         .ToArray();
      if (parts.Length == 1)
      {
         schemaName = "public";
         tableName = parts[0];
         return !string.IsNullOrWhiteSpace(tableName);
      }

      if (parts.Length == 2)
      {
         schemaName = parts[0];
         tableName = parts[1];
         return !string.IsNullOrWhiteSpace(schemaName) && !string.IsNullOrWhiteSpace(tableName);
      }

      schemaName = string.Empty;
      tableName = string.Empty;
      return false;
   }

   private static string ToCamelCase(string value)
   {
      if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
         return value;

      return char.ToLowerInvariant(value[0]) + value.Substring(1);
   }

   private static string ToSnakeCase(string value)
   {
      if (string.IsNullOrEmpty(value))
         return value;

      var builder = new StringBuilder(value.Length + 5);
      for (var i = 0; i < value.Length; i++)
      {
         var current = value[i];
         if (char.IsUpper(current))
         {
            if (i > 0)
               builder.Append('_');

            builder.Append(char.ToLowerInvariant(current));
            continue;
         }

         builder.Append(current);
      }

      return builder.ToString();
   }
}
