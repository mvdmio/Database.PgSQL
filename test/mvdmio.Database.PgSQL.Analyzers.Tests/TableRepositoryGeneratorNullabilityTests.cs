using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

/// <summary>
///    Covers what a table definition says about whether each of its columns can hold null, and what the registration it
///    produces therefore states to the query surface.
/// </summary>
/// <remarks>
///    Only the not-null direction is ever emitted: nullable is what the query surface assumes wherever a type can
///    express it, so a nullable column needs no argument and its absence is as much a part of these assertions as its
///    presence.
/// </remarks>
public class TableRepositoryGeneratorNullabilityTests
{
   /// <summary>
   ///    Every property shape from the nullability table on one definition, so one run pins the whole rule set rather
   ///    than one row of it at a time.
   /// </summary>
   private const string _EVERY_SHAPE = """
      using mvdmio.Database.PgSQL.Attributes;
      using System;

      namespace Demo;

      [Table("public.rows")]
      public partial class RowTable
      {
         [PrimaryKey]
         public long RowId { get; set; }

         [PrimaryKey]
         public string Kind { get; set; } = string.Empty;

         [PrimaryKey]
         [Column(NotNull = true)]
         public int Ordinal { get; set; }

         public long Count { get; set; }

         public long? OptionalCount { get; set; }

         public DateOnly Day { get; set; }

         public string Label { get; set; } = string.Empty;

         public string? Note { get; set; }

         [Column(Null = true)]
         public string Loose { get; set; } = string.Empty;

         [Column("tight_label", NotNull = true)]
         public string TightLabel { get; set; } = string.Empty;

         [Unique]
         public string? Alias { get; set; }

         [Generated]
         public long? ProjectRef { get; set; }
      }
      """;

   [Fact]
   public void EveryPropertyShape_RegistersTheNullabilityItsDefinitionStates()
   {
      var result = GeneratorHarness.RunGenerator(_EVERY_SHAPE);

      result.Diagnostics.Should().BeEmpty();

      var registration = GeneratorHarness.RegistrationSource(result);

      // A key member carries no nullability argument: the key argument already says it, and the mapping builder is what
      // acts on that, so every caller of it gets the rule rather than only generated code.
      registration.Should().ContainAll(
         """.Column(x => x.RowId, "row_id", isPrimaryKey: true)""",
         """.Column(x => x.Kind, "kind", isPrimaryKey: true)""",
         """.Column(x => x.Ordinal, "ordinal", isPrimaryKey: true)"""
      );

      registration.Should().ContainAll(
         """.Column(x => x.Count, "count", isNotNull: true)""",
         """.Column(x => x.Day, "day", isNotNull: true)""",
         """.Column(x => x.Label, "label", isNotNull: true)""",
         """.Column(x => x.TightLabel, "tight_label", isNotNull: true)"""
      );

      // Nullable, each for its own reason: a Nullable<T>, an annotated reference type, a claim withdrawing what the
      // annotation would say, a [Unique] column — a unique index permits any number of nulls — and a [Generated] one,
      // which is the polymorphic-discriminator shape that is null for every kind but its own.
      registration.Should().ContainAll(
         """.Column(x => x.OptionalCount, "optional_count")""",
         """.Column(x => x.Note, "note")""",
         """.Column(x => x.Loose, "loose")""",
         """.Column(x => x.Alias, "alias")""",
         """.Column(x => x.ProjectRef, "project_ref")"""
      );
   }

   [Fact]
   public void EveryPropertyShape_ProducesCodeThatCompiles()
   {
      // The only thing that proves the emitted nullability argument resolves against the overload the library ships.
      GeneratorHarness.AssertGeneratedSourcesCompile(_EVERY_SHAPE);
   }

   /// <summary>
   ///    A reference type in a nullable-oblivious file makes no claim, so it keeps the nullable default — and that is
   ///    exactly the case <c>[Column(NotNull = true)]</c> exists for, which is why stating it there is not a
   ///    contradiction.
   /// </summary>
   [Fact]
   public void NullableObliviousCompilation_ClaimsNothingForAReferenceTypeAndHonoursTheAttribute()
   {
      var result = GeneratorHarness.RunGenerator(
         """
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.rows")]
         public partial class RowTable
         {
            [PrimaryKey]
            public long RowId { get; set; }

            public string Label { get; set; }

            [Column(NotNull = true)]
            public string TightLabel { get; set; }

            public long Count { get; set; }
         }
         """,
         NullableContextOptions.Disable
      );

      result.Diagnostics.Should().BeEmpty();

      var registration = GeneratorHarness.RegistrationSource(result);

      registration.Should().Contain(""".Column(x => x.Label, "label")""");
      registration.Should().Contain(""".Column(x => x.TightLabel, "tight_label", isNotNull: true)""");

      // A value type says it through its type rather than through an annotation, so nothing changes for it here.
      registration.Should().Contain(""".Column(x => x.Count, "count", isNotNull: true)""");
   }

   [Theory]
   [InlineData(
      """
      [Column(NotNull = true)]
         public long? Value { get; set; }
      """,
      "NotNull says it cannot hold null, but its type can",
      """.Column(x => x.Value, "value")"""
   )]
   [InlineData(
      """
      [Column(NotNull = true)]
         public string? Value { get; set; }
      """,
      "NotNull says it cannot hold null, but its type can",
      """.Column(x => x.Value, "value")"""
   )]
   [InlineData(
      """
      [Column(Null = true)]
         public long Value { get; set; }
      """,
      "Null says it can hold null, but a non-nullable value type cannot",
      """.Column(x => x.Value, "value", isNotNull: true)"""
   )]
   [InlineData(
      """
      [Column(Null = true, NotNull = true)]
         public string Value { get; set; } = string.Empty;
      """,
      "Null and NotNull are both set, and they cannot both be true",
      """.Column(x => x.Value, "value", isNotNull: true)"""
   )]
   [InlineData(
      """
      [PrimaryKey]
         [Column(Null = true)]
         public long Value { get; set; }
      """,
      "Null says it can hold null, but a [PrimaryKey] member cannot",
      """.Column(x => x.Value, "value", isPrimaryKey: true)"""
   )]
   public void ContradictoryNullability_ProducesDiagnosticAndFallsBackToTheTypeAndTheKey(
      string propertyDeclaration,
      string expectedReason,
      string expectedColumnCall
   )
   {
      var result = GeneratorHarness.RunGenerator($$"""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.rows")]
         public partial class RowTable
         {
            [PrimaryKey]
            public long RowId { get; set; }

            {{propertyDeclaration}}

            public string Label { get; set; } = string.Empty;
         }
         """);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0021").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
      diagnostic.GetMessage().Should().ContainAll("RowTable", "Value", expectedReason);

      // Abandons nothing, unlike a malformed key: the claim is dropped and every generated signature stays well-defined,
      // so the consumer reads this one error rather than type-not-found errors across their own code.
      result.GeneratedSources.Should().NotBeEmpty();
      GeneratorHarness.RegistrationSource(result).Should().Contain(expectedColumnCall);
   }

   /// <summary>
   ///    A claim that restates something already true is not a contradiction, so it earns no diagnostic — covered by the
   ///    silent <c>[Column(NotNull = true)]</c> on both a key member and an already non-nullable column in
   ///    <see cref="EveryPropertyShape_RegistersTheNullabilityItsDefinitionStates" />, and here for the other direction.
   /// </summary>
   [Fact]
   public void RedundantButTrueNullability_ProducesNoDiagnostic()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.rows")]
         public partial class RowTable
         {
            [PrimaryKey]
            public long RowId { get; set; }

            [Column(Null = true)]
            public string? Note { get; set; }

            [Column(Null = true)]
            public long? OptionalCount { get; set; }
         }
         """);

      result.Diagnostics.Should().BeEmpty();
      GeneratorHarness.RegistrationSource(result).Should().ContainAll(
         """.Column(x => x.Note, "note")""",
         """.Column(x => x.OptionalCount, "optional_count")"""
      );
   }
}
