using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    Reads what a <c>RelationDefinition&lt;,&gt;</c> class says off its own syntax: the column pairs its <c>Keys</c>
///    override lists, and the Relation condition its <c>Condition</c> override states, already rewritten for inlining
///    into the emitted join.
/// </summary>
/// <remarks>
///    Syntax reading rather than symbol reading, which is why it sits apart from <see cref="TableDefinitionSymbols" />:
///    a relation definition is never instantiated and never called, so what it declares exists only as the trees its
///    two overrides are written as. Nothing here decides anything — a side this cannot read comes back as
///    <see langword="null" /> for <see cref="TableDefinitionParser" /> to report.
/// </remarks>
internal static class RelationDefinitionReader
{
   /// <summary>
   ///    Reads the column pairs off a relation definition's <c>Keys</c> override, in the order they are written. Each
   ///    pair's side is <see langword="null" /> when it is not a direct reference to a property of the expected
   ///    parameter — the caller reports that as a diagnostic rather than this method, which only reads what the syntax
   ///    says.
   /// </summary>
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

   /// <summary>
   ///    Reads a relation definition's <c>Condition</c> override off its syntax, if it states one. Returns
   ///    <see langword="null" /> when the override is absent — an ordinary relation, which is the default the base type
   ///    gives every relation definition that does not state one.
   /// </summary>
   /// <remarks>
   ///    The override's body is a two-parameter lambda over <c>TDeclaring</c> and <c>TTarget</c>. Its own two parameters
   ///    are rewritten here to <c>x</c> and <c>y</c> — the names the emitted join lambda uses — so the caller can inline
   ///    the result verbatim; every other identifier, including a constant such as an enum member, is left exactly as
   ///    written. Every member accessed directly on either parameter is also collected, for the caller to check against
   ///    each table's generated data type.
   /// </remarks>
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

   /// <summary>
   ///    The two parameters and body expression of a relation condition's lambda, however the body is written — an
   ///    expression-bodied lambda, or a block with a single return statement.
   /// </summary>
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

   /// <summary>
   ///    Rewrites a relation condition's body for inlining into the emitted join: a reference to either of its two
   ///    lambda parameters becomes the name the emitted join lambda uses ("x" for the declaring side, "y" for the
   ///    target side), and a bare reference to a type — an enum a constant is compared against, for instance — is
   ///    qualified the way every other generated reference is, because the emitted file carries none of the
   ///    developer's own <c>using</c> directives. Anything else, including a member accessed on either parameter and
   ///    every constant, is left exactly as written: that is what "copies verbatim" means for a mapped column or
   ///    another relation property, whose name is identical on the generated data type.
   /// </summary>
   /// <remarks>
   ///    Nothing here wraps a constant to force it into the rendered join as a literal, because nothing needs to: the
   ///    query surface already renders a constant in an association predicate as a literal. The one exception is a
   ///    column carrying a value conversion, where the comparison binds the converted value as a parameter instead,
   ///    and no wrapper changes that — see ADR 0010.
   /// </remarks>
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
         // The right-hand side of a member access never needs qualifying on its own — whatever sits on its left
         // already carries, or will carry, everything needed to resolve it.
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

   /// <summary>The expression a property's getter returns, however it is written — an arrow body on the property, an arrow-bodied getter, or a getter with a single return statement.</summary>
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

   /// <summary>The element expressions of a collection literal, an array or list creation, or a plain initializer — whichever shape a <c>Keys</c> override happens to be written as.</summary>
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

   /// <summary>
   ///    Whether <paramref name="expression" /> is a single-parameter lambda whose body is a direct property access on
   ///    that parameter, and if so, the property's name. Anything else — a nested access, a method call, an indexer, a
   ///    reference to something other than the lambda's own parameter — is not a column reference.
   /// </summary>
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
