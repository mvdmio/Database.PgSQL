using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class RelationDefinitionReader
{
   public static ImmutableArray<RelationKeyPairDeclaration> ReadRelationKeyPairDeclarations(INamedTypeSymbol relationDefinitionType, Compilation compilation)
   {
      var keysProperty = relationDefinitionType.GetMembers("Keys").OfType<IPropertySymbol>().FirstOrDefault(x => x.IsOverride);
      var syntaxReference = keysProperty?.DeclaringSyntaxReferences.FirstOrDefault();

      if (syntaxReference is null)
         return ImmutableArray<RelationKeyPairDeclaration>.Empty;

      var syntax = syntaxReference.GetSyntax();
      var bodyExpression = GetPropertyBodyExpression(syntax);

      if (bodyExpression is null)
         return ImmutableArray<RelationKeyPairDeclaration>.Empty;

      var elements = GetCollectionElements(bodyExpression);

      if (elements is null)
         return ImmutableArray<RelationKeyPairDeclaration>.Empty;

      var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
      var builder = ImmutableArray.CreateBuilder<RelationKeyPairDeclaration>();

      foreach (var element in elements)
      {
         if (element is not InvocationExpressionSyntax { ArgumentList.Arguments.Count: 2 } invocation
             || semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "Key" })
         {
            builder.Add(new RelationKeyPairDeclaration(null, null, element.GetLocation()));
            continue;
         }

         var declaringName = ReadColumnReference(invocation.ArgumentList.Arguments[0].Expression, semanticModel);
         var targetName = ReadColumnReference(invocation.ArgumentList.Arguments[1].Expression, semanticModel);

         builder.Add(new RelationKeyPairDeclaration(declaringName, targetName, invocation.GetLocation()));
      }

      return builder.ToImmutable();
   }

   public static RelationConditionDeclaration? ReadRelationCondition(INamedTypeSymbol relationDefinitionType, Compilation compilation)
   {
      var conditionProperty = relationDefinitionType.GetMembers("Condition").OfType<IPropertySymbol>().FirstOrDefault(x => x.IsOverride);
      var syntaxReference = conditionProperty?.DeclaringSyntaxReferences.FirstOrDefault();

      if (syntaxReference is null)
         return null;

      var syntax = syntaxReference.GetSyntax();
      var bodyExpression = GetPropertyBodyExpression(syntax);

      if (bodyExpression is null)
         return null;

      if (GetLambdaParametersAndBody(bodyExpression) is not var (declaringParameter, targetParameter, lambdaBody))
         return null;

      var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
      var declaringParameterSymbol = semanticModel.GetDeclaredSymbol(declaringParameter);
      var targetParameterSymbol = semanticModel.GetDeclaredSymbol(targetParameter);

      var memberAccesses = ImmutableArray.CreateBuilder<RelationConditionMemberAccess>();

      foreach (var memberAccess in lambdaBody.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
      {
         if (memberAccess.Expression is not IdentifierNameSyntax identifier)
            continue;

         var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
         var isDeclaringSide = SymbolEqualityComparer.Default.Equals(symbol, declaringParameterSymbol);
         var isTargetSide = !isDeclaringSide && SymbolEqualityComparer.Default.Equals(symbol, targetParameterSymbol);

         if (!isDeclaringSide && !isTargetSide)
            continue;

         memberAccesses.Add(new RelationConditionMemberAccess(isDeclaringSide, memberAccess.Name.Identifier.Text, memberAccess.GetLocation()));
      }

      var rewriter = new RelationConditionParameterRewriter(semanticModel, declaringParameterSymbol, targetParameterSymbol);
      var rewrittenBody = (ExpressionSyntax)rewriter.Visit(lambdaBody)!;
      var bodyText = $"({rewrittenBody.WithoutTrivia().ToFullString()})";

      return new RelationConditionDeclaration(bodyText, memberAccesses.ToImmutable(), bodyExpression.GetLocation());
   }

   private static (ParameterSyntax Declaring, ParameterSyntax Target, ExpressionSyntax Body)? GetLambdaParametersAndBody(ExpressionSyntax expression)
   {
      if (expression is not ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 2 } lambda)
         return null;

      var body = lambda.ExpressionBody
         ?? lambda.Block?.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault()?.Expression;

      if (body is null)
         return null;

      return (lambda.ParameterList.Parameters[0], lambda.ParameterList.Parameters[1], body);
   }

   private sealed class RelationConditionParameterRewriter : CSharpSyntaxRewriter
   {
      private readonly SemanticModel _semanticModel;
      private readonly ISymbol? _declaringParameter;
      private readonly ISymbol? _targetParameter;

      public RelationConditionParameterRewriter(SemanticModel semanticModel, ISymbol? declaringParameter, ISymbol? targetParameter)
      {
         _semanticModel = semanticModel;
         _declaringParameter = declaringParameter;
         _targetParameter = targetParameter;
      }

      public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
      {
         if (node.Parent is MemberAccessExpressionSyntax memberAccess && ReferenceEquals(memberAccess.Name, node))
            return node;

         var symbol = _semanticModel.GetSymbolInfo(node).Symbol;

         if (SymbolEqualityComparer.Default.Equals(symbol, _declaringParameter))
            return SyntaxFactory.IdentifierName("x").WithTriviaFrom(node);

         if (SymbolEqualityComparer.Default.Equals(symbol, _targetParameter))
            return SyntaxFactory.IdentifierName("y").WithTriviaFrom(node);

         if (symbol is ITypeSymbol typeSymbol)
            return SyntaxFactory.ParseName(TableDefinitionSymbols.TypeDisplayName(typeSymbol)).WithTriviaFrom(node);

         return base.VisitIdentifierName(node);
      }
   }

   private static ExpressionSyntax? GetPropertyBodyExpression(SyntaxNode syntax)
   {
      if (syntax is not PropertyDeclarationSyntax property)
         return null;

      if (property.ExpressionBody is { Expression: { } arrowExpression })
         return arrowExpression;

      var getter = property.AccessorList?.Accessors.FirstOrDefault(x => x.IsKind(SyntaxKind.GetAccessorDeclaration));

      if (getter?.ExpressionBody is { Expression: { } getterArrowExpression })
         return getterArrowExpression;

      return getter?.Body?.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault()?.Expression;
   }

   private static IEnumerable<ExpressionSyntax>? GetCollectionElements(ExpressionSyntax expression)
   {
      return expression switch
      {
         CollectionExpressionSyntax collection => collection.Elements.OfType<ExpressionElementSyntax>().Select(x => x.Expression),
         ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
         ImplicitArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
         ObjectCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
         ImplicitObjectCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
         InitializerExpressionSyntax initializer => initializer.Expressions,
         _ => null
      };
   }

   private static string? ReadColumnReference(ExpressionSyntax expression, SemanticModel semanticModel)
   {
      ParameterSyntax? parameter;
      ExpressionSyntax? body;

      switch (expression)
      {
         case SimpleLambdaExpressionSyntax simple:
            parameter = simple.Parameter;
            body = simple.ExpressionBody;
            break;
         case ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized:
            parameter = parenthesized.ParameterList.Parameters[0];
            body = parenthesized.ExpressionBody;
            break;
         default:
            return null;
      }

      if (body is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax targetIdentifier } memberAccess)
         return null;

      var parameterSymbol = semanticModel.GetDeclaredSymbol(parameter);
      var identifierSymbol = semanticModel.GetSymbolInfo(targetIdentifier).Symbol;

      if (parameterSymbol is null || !SymbolEqualityComparer.Default.Equals(parameterSymbol, identifierSymbol))
         return null;

      return semanticModel.GetSymbolInfo(memberAccess).Symbol is IPropertySymbol property ? property.Name : null;
   }
}
