using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers;

internal readonly struct StorageConversion
{
   private StorageConversion(bool isEnumMemberName, string storedTypeName)
   {
      IsEnumMemberName = isEnumMemberName;
      StoredTypeName = storedTypeName;
   }

   public bool IsEnumMemberName { get; }

   public string StoredTypeName { get; }

   public static StorageConversion ToMemberName()
   {
      return new StorageConversion(isEnumMemberName: true, storedTypeName: "string");
   }

   public static StorageConversion ToCast(string storedTypeName)
   {
      return new StorageConversion(isEnumMemberName: false, storedTypeName: storedTypeName);
   }
}

internal readonly struct ColumnStorage
{
   private const string TEXT = "Text";
   private const string SMALLINT = "Smallint";
   private const string INTEGER = "Integer";
   private const string BIGINT = "Bigint";

   private const string STORED_AS_PROPERTY_NAME = "StoredAs";

   public const string CLAIM_TYPE_FULL_NAME = "global::NpgsqlTypes.NpgsqlDbType";

   private static readonly HashSet<string> _representableClaims = new(StringComparer.Ordinal) {
      "Bigint", "Bit", "Boolean", "Bytea", "Char", "Date", "Double", "Integer", "Interval", "Json", "Jsonb", "Money",
      "Numeric", "Real", "Smallint", "Text", "Time", "TimeTz", "Timestamp", "TimestampTz", "Uuid", "Varbit", "Varchar",
      "Xml"
   };

   private static readonly HashSet<string> _claimsRefusedOnStrings = new(StringComparer.Ordinal) { SMALLINT, INTEGER, BIGINT };

   private ColumnStorage(
      string? mappedAs,
      string? boundAs,
      StorageConversion? conversion,
      string valueTypeName,
      string? refusedClaim,
      string? refusalAlternatives,
      bool isUnwritableType
   )
   {
      MappedAs = mappedAs;
      BoundAs = boundAs;
      Conversion = conversion;
      ValueTypeName = valueTypeName;
      RefusedClaim = refusedClaim;
      RefusalAlternatives = refusalAlternatives;
      IsUnwritableType = isUnwritableType;
   }

   public string? MappedAs { get; }

   public string? BoundAs { get; }

   public StorageConversion? Conversion { get; }

   public string ValueTypeName { get; }

   public string? RefusedClaim { get; }

   public string? RefusalAlternatives { get; }

   public bool IsUnwritableType { get; }

   public bool HasNoQueryRepresentation => MappedAs is not null && !_representableClaims.Contains(MappedAs);

   public bool StatesConversion => Conversion is not null;

   public static ColumnStorage Read(ITypeSymbol propertyType, AttributeData? columnAttribute)
   {
      var claim = ClaimedMember(columnAttribute);
      var valueType = UnwrapNullable(propertyType);
      var valueTypeName = TableDefinitionSymbols.TypeDisplayName(valueType);

      if (IsUnwritable(valueType))
         return Unwritable(valueTypeName);

      if (valueType.TypeKind == TypeKind.Enum)
         return ForEnum(claim, valueTypeName);

      if (valueType.SpecialType == SpecialType.System_String)
         return ForString(claim, valueTypeName);

      if (valueType.SpecialType == SpecialType.System_SByte)
         return ForSByte(claim, valueTypeName);

      return new ColumnStorage(
         mappedAs: claim,
         boundAs: claim,
         conversion: null,
         valueTypeName: valueTypeName,
         refusedClaim: null,
         refusalAlternatives: null,
         isUnwritableType: false
      );
   }

   private static ColumnStorage ForEnum(string? claim, string valueTypeName)
   {
      var conversion = claim switch
      {
         SMALLINT => StorageConversion.ToCast("short"),
         INTEGER => StorageConversion.ToCast("int"),
         BIGINT => StorageConversion.ToCast("long"),
         _ => StorageConversion.ToMemberName()
      };

      var isInferredFromTheConvertedValue = claim is null or TEXT or SMALLINT or INTEGER or BIGINT;

      return new ColumnStorage(
         mappedAs: claim ?? TEXT,
         boundAs: isInferredFromTheConvertedValue ? null : claim,
         conversion: conversion,
         valueTypeName: valueTypeName,
         refusedClaim: null,
         refusalAlternatives: null,
         isUnwritableType: false
      );
   }

   private static ColumnStorage ForString(string? claim, string valueTypeName)
   {
      if (claim is not null && _claimsRefusedOnStrings.Contains(claim))
         return Refused(claim, TableRepositoryDiagnostics.STORAGE_ALTERNATIVES_FOR_A_STRING, valueTypeName);

      return new ColumnStorage(
         mappedAs: claim,
         boundAs: claim is null or TEXT ? null : claim,
         conversion: null,
         valueTypeName: valueTypeName,
         refusedClaim: null,
         refusalAlternatives: null,
         isUnwritableType: false
      );
   }

   private static ColumnStorage ForSByte(string? claim, string valueTypeName)
   {
      return new ColumnStorage(
         mappedAs: claim ?? SMALLINT,
         boundAs: claim is null or SMALLINT ? null : claim,
         conversion: StorageConversion.ToCast("short"),
         valueTypeName: valueTypeName,
         refusedClaim: null,
         refusalAlternatives: null,
         isUnwritableType: false
      );
   }

   private static ColumnStorage Refused(string claim, string alternatives, string valueTypeName)
   {
      return new ColumnStorage(
         mappedAs: null,
         boundAs: null,
         conversion: null,
         valueTypeName: valueTypeName,
         refusedClaim: claim,
         refusalAlternatives: alternatives,
         isUnwritableType: false
      );
   }

   private static ColumnStorage Unwritable(string valueTypeName)
   {
      return new ColumnStorage(
         mappedAs: null,
         boundAs: null,
         conversion: null,
         valueTypeName: valueTypeName,
         refusedClaim: null,
         refusalAlternatives: null,
         isUnwritableType: true
      );
   }

   private static bool IsUnwritable(ITypeSymbol valueType)
   {
      return valueType.SpecialType is SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64;
   }

   private static string? ClaimedMember(AttributeData? columnAttribute)
   {
      if (columnAttribute is null)
         return null;

      var argument = columnAttribute.NamedArguments
         .FirstOrDefault(x => string.Equals(x.Key, STORED_AS_PROPERTY_NAME, StringComparison.Ordinal));

      if (argument.Key is null || argument.Value.Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } claimType || argument.Value.Value is null)
         return null;

      return claimType.GetMembers()
         .OfType<IFieldSymbol>()
         .FirstOrDefault(x => x.HasConstantValue && Equals(x.ConstantValue, argument.Value.Value))
         ?.Name;
   }

   private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
   {
      if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
         return nullable.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.NotAnnotated);

      return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
   }
}
