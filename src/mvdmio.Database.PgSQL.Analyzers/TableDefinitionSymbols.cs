using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Text;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    Everything a table definition's Roslyn symbols say: which attributes a property carries, whether its shape and
///    nullability are usable, what a relation property's type states, and the names derived from all of it.
/// </summary>
/// <remarks>
///    Separated from <see cref="TableDefinitionParser" /> so that file is left with the decisions — which diagnostic a
///    fact earns and whether it abandons the table — rather than the symbol reading those decisions rest on.
/// </remarks>
internal static class TableDefinitionSymbols
{
   public const string TABLE_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.TableAttribute";
   public const string PRIMARY_KEY_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.PrimaryKeyAttribute";
   public const string UNIQUE_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.UniqueAttribute";
   public const string COLUMN_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.ColumnAttribute";
   public const string GENERATED_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.GeneratedAttribute";
   public const string RELATION_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.RelationAttribute";

   /// <summary>The open generic <c>RelationDefinition&lt;TDeclaring, TTarget&gt;</c> a relation definition class derives from.</summary>
   public const string RELATION_DEFINITION_FULL_NAME = "mvdmio.Database.PgSQL.Relations.RelationDefinition`2";

   /// <summary>The <c>[Column]</c> named argument that declares a tenancy column.</summary>
   private const string TENANCY_PROPERTY_NAME = "Tenancy";

   /// <summary>
   ///    The collection types a relation to many rows may be declared as. The generated mirror is always a concrete
   ///    list, so this only decides what the table definition itself is allowed to say.
   /// </summary>
   private static readonly HashSet<string> _toManyCollectionTypeNames = new(StringComparer.Ordinal) {
      "System.Collections.Generic.List<T>",
      "System.Collections.Generic.IList<T>",
      "System.Collections.Generic.ICollection<T>",
      "System.Collections.Generic.IEnumerable<T>",
      "System.Collections.Generic.IReadOnlyList<T>",
      "System.Collections.Generic.IReadOnlyCollection<T>"
   };

   private static readonly SymbolDisplayFormat _typeDisplayFormat = new(
      globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
      typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
      genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
      miscellaneousOptions:
      SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
      SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
      SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
   );

   public static bool HasAttribute(IPropertySymbol property, string fullName)
   {
      return property.GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() == fullName);
   }

   public static AttributeData RelationAttributeOf(IPropertySymbol property)
   {
      return property.GetAttributes().First(x => x.AttributeClass?.ToDisplayString() == RELATION_ATTRIBUTE_FULL_NAME);
   }

   /// <summary>
   ///    Whether a property that is not a mappable column still has to be validated, because an attribute on it says the
   ///    developer meant it to be one.
   /// </summary>
   public static bool ShouldValidateProperty(IPropertySymbol property)
   {
      return property.DeclaredAccessibility == Accessibility.Public || HasRelevantAttribute(property);
   }

   /// <summary>
   ///    Whether a property is a relation rather than a column candidate. Type-driven: a property whose type, or
   ///    whose collection element type for a relation to many, derives from
   ///    <c>RelationDefinition&lt;TDeclaring, TTarget&gt;</c> is a relation on its own, and writing <c>[Relation]</c>
   ///    on it besides is accepted but adds nothing. The old attribute-argument form still needs the attribute to say
   ///    so, because its target is stated by the property's own type with nothing further to read.
   /// </summary>
   public static bool IsRelationProperty(IPropertySymbol property, Compilation compilation)
   {
      if (HasAttribute(property, RELATION_ATTRIBUTE_FULL_NAME))
         return true;

      var type = property.Type;

      if (type is INamedTypeSymbol { IsGenericType: true } collection && _toManyCollectionTypeNames.Contains(collection.OriginalDefinition.ToDisplayString()))
         type = collection.TypeArguments[0];

      return TryGetRelationDefinitionBase(type, compilation, out _);
   }

   /// <remarks>
   ///    A setter has to exist, and its accessibility is not looked at. The requirement that one exist is what keeps a
   ///    computed member out — a get-only or expression-bodied property describes no column, and admitting it would turn
   ///    an expression into a column that is not there. How accessible it is says nothing about the column, because a
   ///    table definition is purely declarative and is never instantiated: <c>{ get; private set; }</c>,
   ///    <c>{ get; init; }</c> and <c>{ get; protected set; }</c> all describe the same column as <c>{ get; set; }</c>.
   /// </remarks>
   public static bool IsSupportedProperty(IPropertySymbol property)
   {
      return !property.IsStatic
             && property.DeclaredAccessibility == Accessibility.Public
             && property.Parameters.Length == 0
             && property.GetMethod?.DeclaredAccessibility == Accessibility.Public
             && property.SetMethod is not null;
   }

   /// <remarks>
   ///    Unlike <see cref="IsSupportedProperty" />, neither the property's own accessibility nor its accessors' is
   ///    looked at here. A relation property is purely declarative in the same sense the table definition holding it
   ///    is: nothing ever reads or writes it at run time, only its type identifies the relation. That is what lets it
   ///    be typed as a privately nested relation definition class — C# itself then requires the property to be no
   ///    more accessible than that type, exactly as it would for any other member, and this check does not relax that.
   /// </remarks>
   public static bool IsSupportedRelationPropertyShape(IPropertySymbol property)
   {
      return !property.IsStatic
             && property.Parameters.Length == 0
             && property.GetMethod is not null
             && property.SetMethod is not null;
   }

   public static bool IsPartial(INamedTypeSymbol classSymbol)
   {
      return classSymbol.DeclaringSyntaxReferences
         .Select(x => x.GetSyntax())
         .OfType<ClassDeclarationSyntax>()
         .Any(x => x.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
   }

   public static PropertyDefinitionModel CreatePropertyModel(IPropertySymbol property)
   {
      var columnAttribute = property.GetAttributes()
         .FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == COLUMN_ATTRIBUTE_FULL_NAME);

      var columnName = columnAttribute?.ConstructorArguments.FirstOrDefault().Value as string;
      var isPrimaryKey = HasAttribute(property, PRIMARY_KEY_ATTRIBUTE_FULL_NAME);
      var nullability = NullabilityClaim.Read(property, columnAttribute, isPrimaryKey);

      return new PropertyDefinitionModel(
         propertyName: property.Name,
         parameterName: ToCamelCase(property.Name),
         typeName: property.Type.ToDisplayString(_typeDisplayFormat),
         columnName: string.IsNullOrWhiteSpace(columnName) ? ToSnakeCase(property.Name) : columnName!,
         isPrimaryKey: isPrimaryKey,
         isUnique: HasAttribute(property, UNIQUE_ATTRIBUTE_FULL_NAME),
         isGenerated: HasAttribute(property, GENERATED_ATTRIBUTE_FULL_NAME),
         isTenancy: HasNamedFlagSet(columnAttribute, TENANCY_PROPERTY_NAME),
         isNullable: TypeCanHoldNull(property.Type),
         isDeclaredNotNull: nullability.IsNotNull,
         nullabilityContradiction: nullability.Contradiction,
         requiresNullForgivingInitializer: property.Type.IsReferenceType && property.NullableAnnotation != NullableAnnotation.Annotated,
         storage: ColumnStorage.Read(property.Type, columnAttribute)
      );
   }

   /// <summary>Whether a <c>[Column]</c> argument, named rather than positional, was set to <see langword="true" />.</summary>
   private static bool HasNamedFlagSet(AttributeData? attribute, string propertyName)
   {
      if (attribute is null)
         return false;

      return attribute.NamedArguments.Any(x => string.Equals(x.Key, propertyName, StringComparison.Ordinal) && x.Value.Value is true);
   }

   /// <summary>How generated code names a type: fully qualified, keywords for the special types, nullability included.</summary>
   public static string TypeDisplayName(ITypeSymbol type)
   {
      return type.ToDisplayString(_typeDisplayFormat);
   }

   /// <summary>
   ///    Whether the property's type can hold null, which a primary key member's may not.
   /// </summary>
   /// <remarks>
   ///    Both forms are checked because they are separate facts: a nullable value type is a constructed
   ///    <see cref="Nullable{T}" />, while a nullable reference type is only an annotation — and in a nullable-oblivious
   ///    file that annotation is absent, which is read here as not nullable because nothing else can be read from it.
   /// </remarks>
   public static bool TypeCanHoldNull(ITypeSymbol type)
   {
      return type.NullableAnnotation == NullableAnnotation.Annotated
             || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
   }

   /// <summary>
   ///    Whether the property's type states that its column cannot hold null. Not the negation of
   ///    <see cref="TypeCanHoldNull" />: this is the stricter question of whether the type says anything at all, and an
   ///    unannotated reference type in a nullable-oblivious file says nothing, so both answer false for it.
   /// </summary>
   /// <remarks>
   ///    A value type states it unless it is a <see cref="Nullable{T}" />. A reference type states it only through its
   ///    annotation, which is why the absence of one is read as saying nothing rather than as saying not-null.
   /// </remarks>
   public static bool TypeStatesNotNull(IPropertySymbol property)
   {
      if (property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
         return false;

      if (property.Type.IsValueType)
         return true;

      return property.NullableAnnotation == NullableAnnotation.NotAnnotated;
   }

   /// <summary>
   ///    Reads the relation's target and its cardinality off the property's type, which is the only place either is
   ///    stated.
   /// </summary>
   /// <remarks>
   ///    A property whose type — or whose collection element type, for a relation to many — derives from
   ///    <c>RelationDefinition&lt;TDeclaring, TTarget&gt;</c> is the type-driven form: the target and the declaring
   ///    type argument are read from that base type, and <paramref name="relationDefinition" /> is set so the caller
   ///    can go on to read its <c>Keys</c>. Otherwise the property's own type (or element type) is read as the target
   ///    directly, which is the old attribute-argument form.
   /// </remarks>
   public static bool TryGetRelationTarget(
      ITypeSymbol propertyType,
      Compilation compilation,
      out INamedTypeSymbol target,
      out bool isToMany,
      out INamedTypeSymbol? relationDefinition,
      out INamedTypeSymbol? declaringTypeArgument
   )
   {
      target = null!;
      isToMany = false;
      relationDefinition = null;
      declaringTypeArgument = null;

      if (propertyType is INamedTypeSymbol { IsGenericType: true } collection
          && _toManyCollectionTypeNames.Contains(collection.OriginalDefinition.ToDisplayString()))
      {
         isToMany = true;

         return IsTargetCandidate(collection.TypeArguments[0], compilation, out target, out relationDefinition, out declaringTypeArgument);
      }

      // A sequence this does not support is rejected as an unsupported type rather than read as a single target, so
      // the diagnostic names the real mistake instead of complaining that the collection is not a table definition.
      if (propertyType.SpecialType != SpecialType.System_String && IsSequence(propertyType))
         return false;

      return IsTargetCandidate(propertyType, compilation, out target, out relationDefinition, out declaringTypeArgument);
   }

   /// <summary>
   ///    Whether <paramref name="type" /> derives from <c>RelationDefinition&lt;TDeclaring, TTarget&gt;</c>, and if
   ///    so, that closed base type — from which the two type arguments are read.
   /// </summary>
   public static bool TryGetRelationDefinitionBase(ITypeSymbol type, Compilation compilation, out INamedTypeSymbol relationDefinitionBase)
   {
      relationDefinitionBase = null!;

      var relationDefinitionSymbol = compilation.GetTypeByMetadataName(RELATION_DEFINITION_FULL_NAME);
      if (relationDefinitionSymbol is null)
         return false;

      for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
      {
         if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, relationDefinitionSymbol))
         {
            relationDefinitionBase = current;
            return true;
         }
      }

      return false;
   }

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
   ///    target side); a bare reference to a type — an enum a constant is compared against, for instance — is
   ///    qualified the way every other generated reference is, because the emitted file carries none of the
   ///    developer's own <c>using</c> directives; and a compile-time constant subtree — an enum member, a literal —
   ///    is wrapped in <c>LinqToDB.Sql.Constant</c>, so the query surface inlines it into the join as a literal
   ///    instead of parameterizing it, and each kind gets its own query plan. Anything else, including a member
   ///    accessed on either parameter, is left exactly as written: that is what "copies verbatim" means for a mapped
   ///    column or another relation property, whose name is identical on the generated data type.
   /// </summary>
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

      /// <remarks>
      ///    Every recursive descent in this rewriter passes back through this method rather than through the
      ///    type-specific <c>VisitXxx</c> overrides, which is what lets one override catch every node — a compile-time
      ///    constant subtree is wrapped here, before either the type-qualifying or the parameter-renaming rule below
      ///    ever sees it, and the wrap recurses into its own children through this same method to apply those rules
      ///    inside it.
      /// </remarks>
      public override SyntaxNode? Visit(SyntaxNode? node)
      {
         // A literal null is left alone: "IS NULL"/"IS NOT NULL" is never parameterized by the query surface in the
         // first place, and Sql.Constant's type parameter cannot be inferred from a null argument alone.
         if (node is ExpressionSyntax expression
             && !(node.Parent is MemberAccessExpressionSyntax memberAccess && ReferenceEquals(memberAccess.Name, node))
             && _semanticModel.GetConstantValue(expression) is { HasValue: true, Value: not null })
         {
            var rewrittenInner = (ExpressionSyntax)base.Visit(node)!;

            return SyntaxFactory.InvocationExpression(
                  SyntaxFactory.ParseExpression("global::LinqToDB.Sql.Constant"),
                  SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(rewrittenInner.WithoutTrivia())))
               )
               .WithTriviaFrom(node);
         }

         return base.Visit(node);
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
            return SyntaxFactory.ParseName(TypeDisplayName(typeSymbol)).WithTriviaFrom(node);

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

   /// <summary>
   ///    Reads the foreign-key property names off the relation attribute, in declaration order. The parameter is
   ///    variadic, so a single name and several arrive the same way.
   /// </summary>
   public static ImmutableArray<string> GetForeignKeyPropertyNames(AttributeData relationAttribute)
   {
      var argument = relationAttribute.ConstructorArguments.FirstOrDefault();

      if (argument.Kind != TypedConstantKind.Array || argument.IsNull)
         return ImmutableArray<string>.Empty;

      return argument.Values
         .Select(x => x.Value as string ?? string.Empty)
         .ToImmutableArray();
   }

   /// <summary>
   ///    Where a mapped property was declared, so a diagnostic about it points at the property rather than at the class.
   /// </summary>
   public static Location PropertyLocation(
      ImmutableArray<IPropertySymbol> mappedProperties,
      PropertyDefinitionModel property,
      ClassDeclarationSyntax classSyntax
   )
   {
      var symbol = mappedProperties.FirstOrDefault(x => string.Equals(x.Name, property.PropertyName, StringComparison.Ordinal));

      return symbol?.Locations.FirstOrDefault() ?? classSyntax.Identifier.GetLocation();
   }

   /// <summary>The namespace-qualified name of a type, which is how a relation names its target.</summary>
   public static string GetFullName(INamedTypeSymbol type)
   {
      return type.ContainingNamespace.IsGlobalNamespace
         ? type.Name
         : $"{type.ContainingNamespace.ToDisplayString()}.{type.Name}";
   }

   /// <summary>Whether a generated type name is already taken by something it cannot merge with.</summary>
   public static bool HasGeneratedTypeNameCollision(INamedTypeSymbol classSymbol, string typeName)
   {
      return classSymbol.ContainingNamespace
         .GetTypeMembers(typeName)
         .Any(type => !CanMergeWithGeneratedType(type));
   }

   /// <summary>A table definition's class name with the <c>Table</c> suffix removed.</summary>
   public static string GetEntityName(string className)
   {
      return className.EndsWith("Table", StringComparison.Ordinal) && className.Length > "Table".Length
         ? className.Substring(0, className.Length - "Table".Length)
         : className;
   }

   /// <summary>Splits a <c>[Table]</c> value into its schema and table, defaulting the schema to <c>public</c>.</summary>
   public static bool TryParseTableName(string value, out string schemaName, out string tableName)
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

   private static bool IsTargetCandidate(
      ITypeSymbol candidate,
      Compilation compilation,
      out INamedTypeSymbol target,
      out INamedTypeSymbol? relationDefinition,
      out INamedTypeSymbol? declaringTypeArgument
   )
   {
      target = null!;
      relationDefinition = null;
      declaringTypeArgument = null;

      if (candidate is not INamedTypeSymbol { TypeKind: TypeKind.Class } named)
         return false;

      if (TryGetRelationDefinitionBase(named, compilation, out var relationDefinitionBase))
      {
         relationDefinition = named;
         declaringTypeArgument = relationDefinitionBase.TypeArguments[0] as INamedTypeSymbol;

         if (relationDefinitionBase.TypeArguments[1] is not INamedTypeSymbol targetTypeArgument)
            return false;

         target = targetTypeArgument;
         return true;
      }

      target = named;
      return true;
   }

   private static bool IsSequence(ITypeSymbol type)
   {
      return type is IArrayTypeSymbol
             || type.AllInterfaces.Any(x => x.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
   }

   private static bool HasRelevantAttribute(IPropertySymbol property)
   {
      return HasAttribute(property, PRIMARY_KEY_ATTRIBUTE_FULL_NAME)
             || HasAttribute(property, UNIQUE_ATTRIBUTE_FULL_NAME)
             || HasAttribute(property, COLUMN_ATTRIBUTE_FULL_NAME)
             || HasAttribute(property, GENERATED_ATTRIBUTE_FULL_NAME);
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
