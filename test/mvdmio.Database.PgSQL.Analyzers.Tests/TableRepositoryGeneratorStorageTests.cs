using AwesomeAssertions;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

/// <summary>
///    Covers what a table definition says about how each of its columns is stored: the value the generated command binds,
///    the type it binds it as, and what the registration therefore states to the query surface.
/// </summary>
/// <remarks>
///    Both surfaces are asserted for every row of the matrix, because a claim reaching one and not the other is the defect
///    the claim exists to remove — an assertion covering only the binding would pass while the two disagreed again.
/// </remarks>
public class TableRepositoryGeneratorStorageTests
{
   /// <summary>
   ///    Every row of the storage matrix on one definition, so one run pins the whole rule set rather than one row at a
   ///    time. <c>State</c> and <c>Priority</c> are the same enum stored two ways on purpose.
   /// </summary>
   private const string EVERY_SHAPE = """
      using mvdmio.Database.PgSQL.Attributes;
      using NpgsqlTypes;
      using System;
      using System.Collections.Generic;

      namespace Demo;

      public enum WorkState
      {
         Open,
         Closed
      }

      [Table("public.rows")]
      public partial class RowTable
      {
         [PrimaryKey]
         [Generated]
         public long RowId { get; set; }

         public WorkState State { get; set; }

         [Column(StoredAs = NpgsqlDbType.Integer)]
         public WorkState Priority { get; set; }

         [Column(StoredAs = NpgsqlDbType.Smallint)]
         public WorkState Severity { get; set; }

         [Column(StoredAs = NpgsqlDbType.Bigint)]
         public WorkState Epoch { get; set; }

         [Column(StoredAs = NpgsqlDbType.Text)]
         public WorkState Phase { get; set; }

         public WorkState? ReviewState { get; set; }

         [Column(StoredAs = NpgsqlDbType.Jsonb)]
         public string Document { get; set; } = string.Empty;

         [Column(StoredAs = NpgsqlDbType.Json)]
         public string? Draft { get; set; }

         public string LegacyDocument { get; set; } = string.Empty;

         public Dictionary<string, string>? Metadata { get; set; }

         public sbyte OffsetHours { get; set; }

         public sbyte? OptionalOffset { get; set; }

         [Column(StoredAs = NpgsqlDbType.Uuid)]
         public Guid Reference { get; set; }
      }
      """;

   [Fact]
   public void EveryStorageShape_EmitsSourceThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile(EVERY_SHAPE);
   }

   [Fact]
   public void EveryStorageShape_ReportsNothing()
   {
      GeneratorHarness.RunGenerator(EVERY_SHAPE).Diagnostics.Should().BeEmpty();
   }

   [Theory]
   [InlineData("State", "data.State.ToString()", "an unclaimed enum is stored as the text of its member name")]
   [InlineData("Phase", "data.Phase.ToString()", "claiming text is claiming the default")]
   [InlineData("Priority", "(int)data.Priority", "an integer claim binds the number behind the member")]
   [InlineData("Severity", "(short)data.Severity", "a small-integer claim binds the number at that width")]
   [InlineData("Epoch", "(long)data.Epoch", "a big-integer claim binds the number at that width")]
   [InlineData("ReviewState", "data.ReviewState?.ToString()", "a nullable enum answers null for null rather than an empty string")]
   [InlineData("LegacyDocument", "data.LegacyDocument", "an unclaimed string binds as it stands, with no cast, so a text column holding JSON keeps working")]
   [InlineData("Metadata", "data.Metadata", "the one JSON shape that already worked keeps its process-wide conversion")]
   [InlineData("OffsetHours", "(short)data.OffsetHours", "a signed byte is widened whether or not anything is claimed")]
   [InlineData("OptionalOffset", "(short?)data.OptionalOffset", "the widening lifts through nullability")]
   public void Column_BindsTheValueItsStorageSettles(string propertyName, string expected, string because)
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(EVERY_SHAPE));

      repository.Should().Contain($"""["{propertyName}"] = {expected},""", because);
   }

   /// <summary>
   ///    The <c>jsonb</c> case, and the only one that states the type on the parameter rather than converting the value:
   ///    the string travels as it stands and PostgreSQL will not cast text to <c>jsonb</c> implicitly.
   /// </summary>
   [Theory]
   [InlineData("Document", "Jsonb")]
   [InlineData("Draft", "Json")]
   public void StringOnAJsonColumn_BindsThroughACustomQueryParameterCarryingTheClaim(string propertyName, string claim)
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(EVERY_SHAPE));

      repository.Should().Contain(
         $"""["{propertyName}"] = new global::mvdmio.Database.PgSQL.Dapper.QueryParameters.TypedQueryParameter(data.{propertyName}, global::NpgsqlTypes.NpgsqlDbType.{claim}),"""
      );
   }

   /// <summary>
   ///    A claim the driver would not infer from the value, on a type this library converts nothing for. Permitted and
   ///    stated, which is what "permitted rather than curated" means at the binding site.
   /// </summary>
   [Fact]
   public void ClaimOnATypeWithNoConversion_StatesTheTypeOnTheParameter()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(EVERY_SHAPE));

      repository.Should().Contain(
         """["Reference"] = new global::mvdmio.Database.PgSQL.Dapper.QueryParameters.TypedQueryParameter(data.Reference, global::NpgsqlTypes.NpgsqlDbType.Uuid),"""
      );
   }

   [Theory]
   [InlineData(
      ".Column<global::Demo.WorkState, string>(x => x.State, \"state\", global::NpgsqlTypes.NpgsqlDbType.Text, static x => x.ToString(), static x => global::System.Enum.Parse<global::Demo.WorkState>(x, true), isNotNull: true)",
      "the same claim that produced the binding is what the query surface is told, so the two cannot disagree"
   )]
   [InlineData(
      ".Column<global::Demo.WorkState, int>(x => x.Priority, \"priority\", global::NpgsqlTypes.NpgsqlDbType.Integer, static x => (int)x, static x => (global::Demo.WorkState)x, isNotNull: true)",
      "an integer claim reaches the query surface as the number behind the member"
   )]
   [InlineData(
      ".Column<global::Demo.WorkState?, string?>(x => x.ReviewState, \"review_state\", global::NpgsqlTypes.NpgsqlDbType.Text, static x => x == null ? null : x.Value.ToString(), static x => x == null ? null : (global::Demo.WorkState?)global::System.Enum.Parse<global::Demo.WorkState>(x, true))",
      "a nullable enum column reads back as null instead of failing to parse an absent member name"
   )]
   [InlineData(
      ".Column<sbyte, short>(x => x.OffsetHours, \"offset_hours\", global::NpgsqlTypes.NpgsqlDbType.Smallint, static x => (short)x, static x => (sbyte)x, isNotNull: true)",
      "the widening a signed byte gets on the Dapper surface is stated on the query surface too"
   )]
   [InlineData(
      ".Column(x => x.Document, \"document\", global::NpgsqlTypes.NpgsqlDbType.Jsonb, isNotNull: true)",
      "a string on a jsonb column needs the type stated and no conversion"
   )]
   [InlineData(
      ".Column(x => x.LegacyDocument, \"legacy_document\", isNotNull: true)",
      "an unclaimed string states nothing at all, which is what keeps a text column holding JSON unchanged"
   )]
   [InlineData(
      ".Column(x => x.Metadata, \"metadata\")",
      "the dictionary keeps the process-wide conversion rather than acquiring a per-column one"
   )]
   public void Registration_StatesWhatEachColumnsStorageSettles(string expected, string because)
   {
      var registration = GeneratorHarness.RegistrationSource(GeneratorHarness.RunGenerator(EVERY_SHAPE));

      registration.Should().Contain(expected, because);
   }

   /// <summary>
   ///    One enum, two columns, two representations. This is what a registry keyed by type cannot express, and the reason
   ///    the claim is stated per column.
   /// </summary>
   [Fact]
   public void TwoColumnsOfOneEnum_AreStoredIndependently()
   {
      var result = GeneratorHarness.RunGenerator(EVERY_SHAPE);
      var repository = GeneratorHarness.RepositorySource(result);
      var registration = GeneratorHarness.RegistrationSource(result);

      repository.Should().Contain("""["State"] = data.State.ToString(),""");
      repository.Should().Contain("""["Priority"] = (int)data.Priority,""");

      registration.Should().Contain("NpgsqlDbType.Text, static x => x.ToString()");
      registration.Should().Contain("NpgsqlDbType.Integer, static x => (int)x");
   }

   /// <summary>
   ///    A claim outside the exercised matrix, on a type the driver has a mapping for. Permitted, because refusal is
   ///    grounded in demonstrated failure rather than in the absence of a test.
   /// </summary>
   [Fact]
   public void ClaimOutsideTheExercisedMatrix_IsPermitted()
   {
      var result = GeneratorHarness.RunGenerator(EVERY_SHAPE);

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0022");
      GeneratorHarness.RegistrationSource(result).Should().Contain("global::NpgsqlTypes.NpgsqlDbType.Uuid");
   }

   [Fact]
   public void ClaimDemonstratedToFailForTheType_IsAnError()
   {
      var result = GeneratorHarness.RunGenerator(ColumnSource("""
         [Column(StoredAs = NpgsqlDbType.Integer)]
         public string Code { get; set; } = string.Empty;
         """));

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0022").Subject;

      diagnostic.GetMessage().Should().Contain("Integer").And.Contain("string");
      diagnostic.GetMessage().Should().Contain("Text, Json or Jsonb", "a diagnostic that refuses something has to name what is legal");
   }

   /// <summary>
   ///    The refused claim is dropped rather than carried, so the column binds the way an unclaimed one would and the
   ///    build reports one error instead of a cascade from types that were never emitted.
   /// </summary>
   [Fact]
   public void ClaimDemonstratedToFailForTheType_IsDroppedAndTheTableStillGenerates()
   {
      var source = ColumnSource("""
         [Column(StoredAs = NpgsqlDbType.Integer)]
         public string Code { get; set; } = string.Empty;
         """);

      var result = GeneratorHarness.RunGenerator(source);

      GeneratorHarness.RepositorySource(result).Should().Contain("""["Code"] = data.Code,""");
      GeneratorHarness.RegistrationSource(result).Should().Contain(""".Column(x => x.Code, "code", isNotNull: true)""");
   }

   [Theory]
   [InlineData("ushort")]
   [InlineData("uint")]
   [InlineData("ulong")]
   public void UnsignedIntegerColumn_IsRefusedAtBuildTime(string typeName)
   {
      var result = GeneratorHarness.RunGenerator(ColumnSource($"public {typeName} Count {{ get; set; }}"));

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0023").Subject;

      diagnostic.GetMessage().Should().Contain(typeName).And.Contain("int, long or decimal");
   }

   /// <summary>
   ///    The unmappable-type warning would say to register a conversion, and there is nothing to convert to. One
   ///    diagnostic, naming the real problem.
   /// </summary>
   [Fact]
   public void UnsignedIntegerColumn_DoesNotAlsoWarnThatTheQuerySurfaceCannotMapIt()
   {
      var result = GeneratorHarness.RunGenerator(ColumnSource("public uint Count { get; set; }"));

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0011");
   }

   [Fact]
   public void ClaimTheQuerySurfaceCannotRepresent_Warns()
   {
      var result = GeneratorHarness.RunGenerator(ColumnSource("""
         [Column(StoredAs = NpgsqlDbType.Inet)]
         public string Address { get; set; } = string.Empty;
         """));

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0024").Subject;

      diagnostic.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
      diagnostic.GetMessage().Should().Contain("Inet").And.Contain("Query()");
   }

   /// <summary>
   ///    The claim is still honoured where it can be — the divergence is made visible, not enforced by dropping the claim
   ///    from both surfaces.
   /// </summary>
   [Fact]
   public void ClaimTheQuerySurfaceCannotRepresent_StillReachesTheDapperSurface()
   {
      var result = GeneratorHarness.RunGenerator(ColumnSource("""
         [Column(StoredAs = NpgsqlDbType.Inet)]
         public string Address { get; set; } = string.Empty;
         """));

      GeneratorHarness.RepositorySource(result).Should().Contain("TypedQueryParameter(data.Address, global::NpgsqlTypes.NpgsqlDbType.Inet)");
   }

   /// <summary>One definition carrying every setter shape a column is now allowed to have.</summary>
   private const string EVERY_SETTER_SHAPE = """
      using mvdmio.Database.PgSQL.Attributes;
      using System;

      namespace Demo;

      [Table("public.rows")]
      public partial class RowTable
      {
         [PrimaryKey]
         [Generated]
         public long RowId { get; private set; }

         [Generated]
         public DateTime CreatedAt { get; private set; }

         public required string Label { get; init; }

         public string? Note { get; protected set; }

         public long Count { get; set; }
      }
      """;

   [Fact]
   public void EverySetterShape_IsALegalColumn()
   {
      var result = GeneratorHarness.RunGenerator(EVERY_SETTER_SHAPE);

      result.Diagnostics.Should().BeEmpty();
      GeneratorHarness.RepositorySource(result).Should().Contain("""["Label"] = data.Label,""").And.Contain("""["Note"] = data.Note,""");
   }

   [Fact]
   public void EverySetterShape_EmitsSourceThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile(EVERY_SETTER_SHAPE);
   }

   /// <summary>
   ///    The half of the definition's own encapsulation that is worth keeping: a caller of the generated data type cannot
   ///    assign a column the database populates. <c>required</c> and <c>init</c> are flattened, because these types have no
   ///    constructor that could satisfy the one and every other column has to stay assignable for a command to be built.
   /// </summary>
   [Fact]
   public void GeneratedDataType_MirrorsGeneratedColumnsAsNonPubliclySettable()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(EVERY_SETTER_SHAPE));
      var dataType = TypeBody(repository, "public partial class RowData");

      dataType.Should().Contain("public long RowId { get; private set; }");
      dataType.Should().Contain("public global::System.DateTime CreatedAt { get; private set; }");
      dataType.Should().Contain("public string Label { get; set; }");
      dataType.Should().Contain("public long Count { get; set; }");
   }

   /// <summary>
   ///    A command's properties stay publicly settable, generated or not: an update addresses its row by a primary key
   ///    that may itself be generated, so the caller has to be able to supply it.
   /// </summary>
   [Fact]
   public void GeneratedCommandTypes_KeepEveryColumnPubliclySettable()
   {
      var repository = GeneratorHarness.RepositorySource(GeneratorHarness.RunGenerator(EVERY_SETTER_SHAPE));

      TypeBody(repository, "public partial class UpdateRowCommand").Should().Contain("public long RowId { get; set; }");
      TypeBody(repository, "public partial class CreateRowCommand").Should().NotContain("private set;");
   }

   /// <summary>
   ///    Relaxing the setter rule must not turn a computed value into a column that does not exist, and the requirement
   ///    that a setter be there is what keeps that from happening.
   /// </summary>
   [Theory]
   [InlineData("public string Computed => \"x\";", "an expression-bodied member")]
   [InlineData("public string Computed { get { return \"x\"; } }", "a get-only member")]
   public void ComputedMember_StaysRefused(string member, string because)
   {
      var result = GeneratorHarness.RunGenerator(ColumnSource(member));

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0009", because);
      result.GeneratedSources.Should().BeEmpty();
   }

   /// <summary>A table definition carrying one extra member, for the tests that ask about exactly one column.</summary>
   private static string ColumnSource(string member)
   {
      return $$"""
         using mvdmio.Database.PgSQL.Attributes;
         using NpgsqlTypes;
         using System;

         namespace Demo;

         [Table("public.rows")]
         public partial class RowTable
         {
            [PrimaryKey]
            [Generated]
            public long RowId { get; set; }

            public long Count { get; set; }

            {{member}}
         }
         """;
   }

   /// <summary>
   ///    The emitted source of one generated type, so an assertion about a property's setter cannot be satisfied by the
   ///    same property on one of the other four types the same file declares.
   /// </summary>
   private static string TypeBody(string source, string declaration)
   {
      var start = source.IndexOf(declaration, StringComparison.Ordinal);
      start.Should().BeGreaterThanOrEqualTo(0, $"'{declaration}' has to be in the emitted source");

      var end = source.IndexOf("\n}", start, StringComparison.Ordinal);
      end.Should().BeGreaterThan(start);

      return source.Substring(start, end - start);
   }
}
