using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    Generates CRUD repository types for classes marked with <c>TableAttribute</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TableRepositoryGenerator : IIncrementalGenerator
{
   private const string TABLE_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.TableAttribute";

   /// <inheritdoc/>
   public void Initialize(IncrementalGeneratorInitializationContext context)
   {
      var tableDefinitions = context.SyntaxProvider.ForAttributeWithMetadataName(
         TABLE_ATTRIBUTE_FULL_NAME,
         predicate: static (node, _) => node is ClassDeclarationSyntax,
         transform: static (syntaxContext, cancellationToken) => TableDefinitionParser.Parse(syntaxContext, cancellationToken)
      );

      var compilationAndDefinitions = context.CompilationProvider.Combine(tableDefinitions.Collect());

      context.RegisterSourceOutput(tableDefinitions, static (productionContext, result) =>
      {
         foreach (var diagnostic in result.Diagnostics)
         {
            productionContext.ReportDiagnostic(diagnostic);
         }

         if (result.Model is null)
            return;

         var source = TableRepositorySourceBuilder.Build(result.Model);
         var hintName = string.IsNullOrWhiteSpace(result.Model.NamespaceName)
            ? result.Model.TableClassName
            : $"{result.Model.NamespaceName}.{result.Model.TableClassName}";

         productionContext.AddSource($"{hintName.Replace('.', '_')}.Repository.g.cs", source);
      });

      context.RegisterSourceOutput(compilationAndDefinitions, static (productionContext, tuple) =>
      {
         var compilation = tuple.Left;
         var results = tuple.Right;
         var models = results.Where(x => x.Model is not null).Select(x => x.Model!).ToImmutableArray();

         if (models.Length == 0)
            return;

         var source = GeneratedAssemblyRegistrationSourceBuilder.Build(compilation.AssemblyName ?? string.Empty, models);
         productionContext.AddSource("GeneratedAssemblyRegistration.g.cs", source);
      });
   }
}
