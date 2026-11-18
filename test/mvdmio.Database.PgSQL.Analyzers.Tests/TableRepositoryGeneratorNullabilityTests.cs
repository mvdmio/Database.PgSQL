using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

public class TableRepositoryGeneratorNullabilityTests
{
   private const string EVERY_SHAPE = """
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
      var result = GeneratorHarness.RunGenerator(EVERY_SHAPE);

      result.Diagnostics.Should().BeEmpty();

      var registration = GeneratorHarness.RegistrationSource(result);

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
      GeneratorHarness.AssertGeneratedSourcesCompile(EVERY_SHAPE);
   }

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

      result.GeneratedSources.Should().NotBeEmpty();
      GeneratorHarness.RegistrationSource(result).Should().Contain(expectedColumnCall);
   }

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
