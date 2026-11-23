using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class QueryMappableTypes
{
   private static readonly HashSet<string> _nativeTypeNames = new(StringComparer.Ordinal) {
      "bool",
      "short",
      "int",
      "long",
      "float",
      "double",
      "decimal",
      "string",
      "System.DateTime",
      "System.Guid",
      "System.DateTimeOffset",
      "System.DateOnly",
      "System.TimeOnly",
      "System.TimeSpan"
   };

   private static readonly HashSet<string> _scalarOnlyTypeNames = new(StringComparer.Ordinal) {
      "byte",
      "char",
      "System.Uri",
      "System.Collections.Generic.Dictionary<string, string>"
   };

   private static readonly SymbolDisplayFormat _typeNameFormat = new(
      globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
      typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
      genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
      miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
   );

   public static bool IsMappable(ITypeSymbol type, ColumnStorage storage)
   {
      return storage.StatesConversion || IsRegisteredForItsType(type);
   }

   private static bool IsRegisteredForItsType(ITypeSymbol type)
   {
      if (type is IArrayTypeSymbol array)
         return array.Rank == 1 && (array.ElementType.SpecialType == SpecialType.System_Byte || IsNative(array.ElementType));

      return IsNative(type) || _scalarOnlyTypeNames.Contains(NameOf(type));
   }

   private static bool IsNative(ITypeSymbol type)
   {
      return _nativeTypeNames.Contains(NameOf(type));
   }

   private static string NameOf(ITypeSymbol type)
   {
      var unwrapped = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
         ? nullable.TypeArguments[0]
         : type;

      return unwrapped.ToDisplayString(_typeNameFormat);
   }
}
