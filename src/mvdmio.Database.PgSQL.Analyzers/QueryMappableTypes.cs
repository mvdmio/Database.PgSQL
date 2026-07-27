using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    Decides whether the query surface can translate a property type without a consumer-supplied conversion.
/// </summary>
/// <remarks>
///    This list mirrors the conversions the library registers on its shared mapping schema
///    (<c>QueryMappings.BuildSchema</c>). The analyzer cannot reference the library, so the two are kept in step by
///    hand: adding a conversion there means adding the type here.
/// </remarks>
internal static class QueryMappableTypes
{
   private static readonly HashSet<string> _mappableTypeNames = new(StringComparer.Ordinal) {
      "System.Guid",
      "System.DateTimeOffset",
      "System.DateOnly",
      "System.TimeOnly",
      "System.TimeSpan",
      "System.Uri",
      "System.Collections.Generic.Dictionary<string, string>"
   };

   /// <summary>Spells out namespaces, uses keywords for the special types, and leaves nullable annotations off.</summary>
   private static readonly SymbolDisplayFormat _typeNameFormat = new(
      globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
      typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
      genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
      miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
   );

   public static bool IsMappable(ITypeSymbol type)
   {
      if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
         return IsMappable(nullable.TypeArguments[0]);

      if (type.TypeKind == TypeKind.Enum)
         return true;

      if (type is IArrayTypeSymbol array)
         return array.Rank == 1 && array.ElementType.SpecialType == SpecialType.System_Byte;

      switch (type.SpecialType)
      {
         case SpecialType.System_Boolean:
         case SpecialType.System_Char:
         case SpecialType.System_SByte:
         case SpecialType.System_Byte:
         case SpecialType.System_Int16:
         case SpecialType.System_UInt16:
         case SpecialType.System_Int32:
         case SpecialType.System_UInt32:
         case SpecialType.System_Int64:
         case SpecialType.System_UInt64:
         case SpecialType.System_Single:
         case SpecialType.System_Double:
         case SpecialType.System_Decimal:
         case SpecialType.System_String:
         case SpecialType.System_DateTime:
            return true;
      }

      return _mappableTypeNames.Contains(type.ToDisplayString(_typeNameFormat));
   }
}
