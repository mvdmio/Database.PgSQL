using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    Decides whether the query surface can translate a property type without a consumer-supplied conversion.
/// </summary>
/// <remarks>
///    This list mirrors the conversions the library registers process-wide on its shared mapping schema
///    (<c>QueryMappings.BuildSchema</c>). The analyzer cannot reference the library, so the two are kept in step by
///    hand: adding a conversion there means adding the type here.
///    <para>
///       Only the process-wide conversions are here. Enums and the narrow numeric types used to be too, and are not any
///       more: a <see cref="ColumnStorage" /> carries their conversion per column, stated into the generated mapping, so
///       there is nothing left for this list to mirror for them. What remains mirrors <see cref="Uri" /> and
///       <c>Dictionary&lt;string, string&gt;</c>, whose conversions are still registered for the type rather than for the
///       column.
///    </para>
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

   /// <summary>
   ///    Whether the query surface can translate the column without a consumer-supplied conversion.
   /// </summary>
   /// <remarks>
   ///    A column whose <paramref name="storage" /> carries a conversion is answered by that and never reaches the list:
   ///    the generated mapping states the conversion for that column, so no registration has to exist for its type.
   /// </remarks>
   public static bool IsMappable(ITypeSymbol type, ColumnStorage storage)
   {
      return storage.StatesConversion || IsRegisteredForItsType(type);
   }

   private static bool IsRegisteredForItsType(ITypeSymbol type)
   {
      if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
         return IsRegisteredForItsType(nullable.TypeArguments[0]);

      if (type is IArrayTypeSymbol array)
         return array.Rank == 1 && array.ElementType.SpecialType == SpecialType.System_Byte;

      switch (type.SpecialType)
      {
         case SpecialType.System_Boolean:
         case SpecialType.System_Char:
         case SpecialType.System_Byte:
         case SpecialType.System_Int16:
         case SpecialType.System_Int32:
         case SpecialType.System_Int64:
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
