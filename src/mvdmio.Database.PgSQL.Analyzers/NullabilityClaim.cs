using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    What one table definition says about whether a column can hold null: the answer the query surface is given, and —
///    when the definition says two things that cannot both be true — which contradiction that is.
/// </summary>
/// <remarks>
///    Read as one value rather than as two facts, because both come from the same three inputs and only make sense
///    together: a contradiction is what decides that the explicit claim is dropped, so reading them apart would let a
///    caller pair an answer with the wrong contradiction, or read the attribute twice and disagree with itself. What
///    the symbols say is <see cref="TableDefinitionSymbols" />'s job and what a contradiction earns is
///    <see cref="TableDefinitionParser" />'s; this file holds only the rules that turn one into the other.
/// </remarks>
internal readonly struct NullabilityClaim
{
   private const string _NULL_PROPERTY_NAME = "Null";
   private const string _NOT_NULL_PROPERTY_NAME = "NotNull";

   private NullabilityClaim(bool isNotNull, string? contradiction)
   {
      IsNotNull = isNotNull;
      Contradiction = contradiction;
   }

   /// <summary>
   ///    Whether the column cannot hold null. Nullable is the answer unless the property's type, its key membership or a
   ///    <c>[Column]</c> argument says otherwise.
   /// </summary>
   public bool IsNotNull { get; }

   /// <summary>
   ///    Which of the four ways the definition can say two things at once this is, or <see langword="null" /> when it
   ///    says one thing. Names the contradiction rather than the diagnostic it earns.
   /// </summary>
   public string? Contradiction { get; }

   public static NullabilityClaim Read(IPropertySymbol property, AttributeData? columnAttribute, bool isPrimaryKey)
   {
      var declaresNull = HasFlagSet(columnAttribute, _NULL_PROPERTY_NAME);
      var declaresNotNull = HasFlagSet(columnAttribute, _NOT_NULL_PROPERTY_NAME);
      var contradiction = ContradictionIn(property, declaresNull, declaresNotNull, isPrimaryKey);

      // A key member cannot hold null whatever else is said. Otherwise an explicit claim wins, unless it contradicts
      // itself — a dropped claim leaves the property's own type as the only thing still saying anything.
      if (isPrimaryKey)
         return new NullabilityClaim(true, contradiction);

      if (contradiction is null && (declaresNull || declaresNotNull))
         return new NullabilityClaim(declaresNotNull, contradiction);

      return new NullabilityClaim(TableDefinitionSymbols.TypeStatesNotNull(property), contradiction);
   }

   /// <remarks>
   ///    Ordered most specific first, because more than one can apply to the same property — <c>Null</c> on a key member
   ///    typed <c>long</c> is both a key contradiction and a value-type one, and the key is the more useful thing to
   ///    name.
   /// </remarks>
   private static string? ContradictionIn(IPropertySymbol property, bool declaresNull, bool declaresNotNull, bool isPrimaryKey)
   {
      if (declaresNull && declaresNotNull)
         return TableRepositoryDiagnostics.NULLABILITY_REASON_BOTH_DIRECTIONS;

      if (declaresNull && isPrimaryKey)
         return TableRepositoryDiagnostics.NULLABILITY_REASON_NULL_ON_A_KEY_MEMBER;

      // Not a contradiction in a nullable-oblivious file: the annotation that would carry the fact cannot be written
      // there, so the attribute is the only thing said about the column, and that is the case it exists for.
      if (declaresNotNull && TableDefinitionSymbols.TypeCanHoldNull(property.Type))
         return TableRepositoryDiagnostics.NULLABILITY_REASON_NOT_NULL_OVER_A_NULLABLE_TYPE;

      if (declaresNull && !TableDefinitionSymbols.TypeCanHoldNull(property.Type) && property.Type.IsValueType)
         return TableRepositoryDiagnostics.NULLABILITY_REASON_NULL_OVER_A_NON_NULLABLE_VALUE_TYPE;

      return null;
   }

   private static bool HasFlagSet(AttributeData? attribute, string propertyName)
   {
      if (attribute is null)
         return false;

      return attribute.NamedArguments.Any(x => string.Equals(x.Key, propertyName, StringComparison.Ordinal) && x.Value.Value is true);
   }
}
