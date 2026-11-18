using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers;

internal readonly struct NullabilityClaim
{
   private const string NULL_PROPERTY_NAME = "Null";
   private const string NOT_NULL_PROPERTY_NAME = "NotNull";

   private NullabilityClaim(bool isNotNull, string? contradiction)
   {
      IsNotNull = isNotNull;
      Contradiction = contradiction;
   }

   public bool IsNotNull { get; }

   public string? Contradiction { get; }

   public static NullabilityClaim Read(IPropertySymbol property, AttributeData? columnAttribute, bool isPrimaryKey)
   {
      var declaresNull = HasFlagSet(columnAttribute, NULL_PROPERTY_NAME);
      var declaresNotNull = HasFlagSet(columnAttribute, NOT_NULL_PROPERTY_NAME);
      var contradiction = ContradictionIn(property, declaresNull, declaresNotNull, isPrimaryKey);

      if (isPrimaryKey)
         return new NullabilityClaim(true, contradiction);

      if (contradiction is null && (declaresNull || declaresNotNull))
         return new NullabilityClaim(declaresNotNull, contradiction);

      return new NullabilityClaim(TableDefinitionSymbols.TypeStatesNotNull(property), contradiction);
   }

   private static string? ContradictionIn(IPropertySymbol property, bool declaresNull, bool declaresNotNull, bool isPrimaryKey)
   {
      if (declaresNull && declaresNotNull)
         return TableRepositoryDiagnostics.NULLABILITY_REASON_BOTH_DIRECTIONS;

      if (declaresNull && isPrimaryKey)
         return TableRepositoryDiagnostics.NULLABILITY_REASON_NULL_ON_A_KEY_MEMBER;

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
