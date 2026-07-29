using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    How the value the generated code binds differs from the property it came from, and what it becomes.
/// </summary>
/// <remarks>
///    One value rather than a kind beside a nullable type name, because a conversion always has a target: a cast with no
///    type to cast to would emit <c>()x</c>, and a target type with nothing converting to it means nothing. Absence of a
///    conversion is the absence of one of these, so the pair cannot fall out of step.
/// </remarks>
internal readonly struct StorageConversion
{
   private StorageConversion(bool isEnumMemberName, string storedTypeName)
   {
      IsEnumMemberName = isEnumMemberName;
      StoredTypeName = storedTypeName;
   }

   /// <summary>
   ///    Whether the value becomes the text of its enum member's name, read back by a case-insensitive parse. Otherwise it
   ///    is a cast — an enum to the number behind it, or a narrow integer widened to one the driver can write.
   /// </summary>
   public bool IsEnumMemberName { get; }

   /// <summary>The type the conversion produces, as generated code names it.</summary>
   public string StoredTypeName { get; }

   /// <summary>An enum bound as the text of its member name.</summary>
   public static StorageConversion ToMemberName()
   {
      return new StorageConversion(isEnumMemberName: true, storedTypeName: "string");
   }

   /// <summary>A value bound as the numeric type it is cast to.</summary>
   public static StorageConversion ToCast(string storedTypeName)
   {
      return new StorageConversion(isEnumMemberName: false, storedTypeName: storedTypeName);
   }
}

/// <summary>
///    What one column's storage claim settles: how the generated command converts the value and which type it binds it
///    as, and what the query surface mapping is told about the column.
/// </summary>
/// <remarks>
///    Read as one value rather than as separate facts, because both surfaces are answered from the same two inputs — the
///    property's type and the claim on it — and answering them apart is how the two came to disagree about an enum in the
///    first place. This is the one place those rules live; the two source builders only turn the answer into text.
///    <para>
///       The claim's absence is read from the attribute's named arguments rather than from a sentinel value, exactly how
///       the nullability claim tells an unstated <c>Null</c> from a <c>false</c> one.
///    </para>
/// </remarks>
internal readonly struct ColumnStorage
{
   private const string TEXT = "Text";
   private const string SMALLINT = "Smallint";
   private const string INTEGER = "Integer";
   private const string BIGINT = "Bigint";

   private const string STORED_AS_PROPERTY_NAME = "StoredAs";

   /// <summary>How generated code names the enum a claim is spelled with.</summary>
   public const string CLAIM_TYPE_FULL_NAME = "global::NpgsqlTypes.NpgsqlDbType";

   /// <summary>
   ///    The claims the query surface's provider can represent, which is the table in the library's
   ///    <c>QueryStorageTypes</c>. The analyzer cannot reference the library, so the two are kept in step by hand: adding
   ///    an entry there means adding the member here. A claim outside this set is honoured on the Dapper surface and left
   ///    unstated on the query surface, which is what <see cref="TableRepositoryDiagnostics.UnrepresentableStorageClaim" />
   ///    warns about.
   /// </summary>
   private static readonly HashSet<string> _representableClaims = new(StringComparer.Ordinal) {
      "Bigint", "Bit", "Boolean", "Bytea", "Char", "Date", "Double", "Integer", "Interval", "Json", "Jsonb", "Money",
      "Numeric", "Real", "Smallint", "Text", "Time", "TimeTz", "Timestamp", "TimestampTz", "Uuid", "Varbit", "Varchar",
      "Xml"
   };

   /// <summary>
   ///    The integral claims a <c>string</c> cannot be bound under. Npgsql refuses the parameter outright — "Can't write
   ///    CLR type System.String" — because nothing in this library converts a string to a number, and nothing should:
   ///    the claim states how the column is stored, not that a conversion is wanted.
   /// </summary>
   /// <remarks>
   ///    Every entry is here because a test in this repository demonstrates it failing, which is the only thing that puts
   ///    a claim in the refused set. A reader should not take the shortness of this list for unfinished work.
   /// </remarks>
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

   /// <summary>
   ///    The claim the query surface mapping is told, or <see langword="null" /> when it is told nothing and the provider
   ///    keeps its own reading of the property's type. Not the same as the claim as written: an unclaimed enum is mapped
   ///    as text and an unclaimed <c>sbyte</c> as a small integer, because those are the defaults this library promises.
   /// </summary>
   public string? MappedAs { get; }

   /// <summary>
   ///    The claim the generated command binds the parameter as, or <see langword="null" /> when the driver already
   ///    infers it from the value — which is the common case, because a conversion usually lands on a type that infers
   ///    correctly.
   /// </summary>
   public string? BoundAs { get; }

   /// <summary>
   ///    How the bound value differs from the property, or <see langword="null" /> when it is bound as it stands.
   /// </summary>
   public StorageConversion? Conversion { get; }

   /// <summary>
   ///    The property's own type with nullability stripped, which is what a conversion back from the column has to name.
   /// </summary>
   public string ValueTypeName { get; }

   /// <summary>
   ///    The claim that cannot be honoured for this property's type, or <see langword="null" /> when nothing is refused.
   ///    A refused claim is reported and then dropped, leaving the column bound the way an unclaimed one would be.
   /// </summary>
   public string? RefusedClaim { get; }

   /// <summary>
   ///    The claims that would have been honoured for this property's type, or <see langword="null" /> when nothing is
   ///    refused. Named here rather than at the diagnostic's call site so the rule and the way out of it stay together.
   /// </summary>
   public string? RefusalAlternatives { get; }

   /// <summary>Whether the property's type cannot be written at all, whatever it claims.</summary>
   public bool IsUnwritableType { get; }

   /// <summary>Whether the claim reaches the Dapper surface and not the query surface.</summary>
   public bool HasNoQueryRepresentation => MappedAs is not null && !_representableClaims.Contains(MappedAs);

   /// <summary>
   ///    Whether the query surface is told enough about this column that no process-wide conversion has to exist for its
   ///    type — which is what takes the enum and the narrow-integer cases out of the analyzer's hand-kept mirror.
   /// </summary>
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

      // Nothing is known about what the driver infers for the property's own type, so any claim is stated rather than
      // compared against an inference this could get wrong.
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

   /// <remarks>
   ///    Text unless the claim names an integral type, which is the default this library documents and the one that
   ///    survives inserting a member in the middle of a declaration. A claim that is neither text nor integral still
   ///    binds the member name — permitted, untested, and stated on the parameter so the driver does not have to guess.
   /// </remarks>
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

   /// <remarks>
   ///    An unclaimed string states nothing at all, on either surface. That is load-bearing rather than incidental: a
   ///    <c>text</c> column holding JSON that a hand-written query casts must keep binding as text.
   /// </remarks>
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

   /// <remarks>
   ///    Widened whether or not anything is claimed. Npgsql maps <c>sbyte</c> to <c>int2</c> natively; what fails is the
   ///    <c>DbType.SByte</c> Dapper infers, which the driver has no mapping for. Binding a <c>short</c> is the whole fix.
   /// </remarks>
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

   /// <summary>
   ///    A refused claim: reported, then dropped, so the column is bound the way an unclaimed one would be. Nothing about
   ///    storage is stated on either surface, which is what "dropped" means here.
   /// </summary>
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

   /// <summary>
   ///    A property type no PostgreSQL type accepts. Says nothing about storage either, because there is nothing it could
   ///    truthfully say — the build fails on the type itself.
   /// </summary>
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

   /// <summary>
   ///    The unsigned integers Npgsql registers no mapping for at all — not by inference and not with an explicit type.
   ///    <c>uint</c> exists there only for object-identifier types and <c>ulong</c> only for transaction-id and
   ///    log-sequence types, neither of which is a number a column holds.
   /// </summary>
   private static bool IsUnwritable(ITypeSymbol valueType)
   {
      return valueType.SpecialType is SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64;
   }

   /// <summary>
   ///    The name of the claimed enum member, resolved from the constant the attribute carries. Absent means unclaimed;
   ///    a value no member has is read the same way, because a claim generated code cannot name is a claim it cannot act
   ///    on.
   /// </summary>
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

   /// <remarks>
   ///    Both forms of nullability are stripped, because a conversion back from the column names the type that holds a
   ///    value rather than the one that may not: <c>TaskState?</c> is a constructed <see cref="Nullable{T}" /> while
   ///    <c>string?</c> is only an annotation, and neither belongs in <see cref="ValueTypeName" />.
   /// </remarks>
   private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
   {
      if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
         return nullable.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.NotAnnotated);

      return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
   }
}
