using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

public class TableRepositoryGeneratorRelationKeyClaimsTests
{
   [Fact]
   public void RelationToOneRow_PairedAgainstTheTargetsPrimaryKey_ReportsNoWarning()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            public long? AuthorId { get; set; }

            private AuthorRelation? Author { get; set; }

            private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorId, y => y.AuthorId),
               ];
            }
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """);

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0031");
   }

   [Fact]
   public void RelationToOneRow_PairedAgainstAUniqueColumn_ReportsNoWarning()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            public string? AuthorCode { get; set; }

            private AuthorRelation? Author { get; set; }

            private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorCode, y => y.Code),
               ];
            }
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            [Unique]
            public string Code { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;
         }
         """);

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0031");
   }

   [Fact]
   public void RelationToOneRow_PairedAgainstASupersetOfAUniqueSet_ReportsNoWarning()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            public long? AuthorId { get; set; }
            public string Genre { get; set; } = string.Empty;

            private AuthorRelation? Author { get; set; }

            private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorId, y => y.AuthorId),
                  Key(x => x.Genre, y => y.PrimaryGenre),
               ];
            }
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            public string PrimaryGenre { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
         }
         """);

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0031");
   }

   [Fact]
   public void RelationToOneRow_PairedAgainstNothingTheTargetClaimsUnique_ReportsPGSQL0031_AndStillGenerates()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            public string Genre { get; set; } = string.Empty;

            private AuthorRelation? Author { get; set; }

            private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.Genre, y => y.PrimaryGenre),
               ];
            }
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            public string PrimaryGenre { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
         }
         """);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0031").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
      diagnostic.GetMessage().Should().Contain("Author").And.Contain("AuthorTable");
      result.GeneratedSources.Should().NotBeEmpty();

      var registration = GeneratorHarness.RegistrationSource(result);
      registration.Should().Contain("x.Genre == y.PrimaryGenre");
   }

   [Fact]
   public void RelationToOneRow_WhoseConditionMakesThePairingUnique_StillReportsPGSQL0031()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System;
         using System.Collections.Generic;
         using System.Linq.Expressions;

         namespace Demo;

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            public string Genre { get; set; } = string.Empty;

            private AuthorRelation? Author { get; set; }

            private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.Genre, y => y.PrimaryGenre),
               ];

               public override Expression<Func<BookTable, AuthorTable, bool>> Condition
                  => (book, author) => author.Name == "Unique Enough";
            }
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            public string PrimaryGenre { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
         }
         """);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0031");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationToManyRows_PairedAgainstNothingUnique_ReportsNoPGSQL0031()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            public string PrimaryGenre { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;

            private List<BooksRelation> Books { get; set; } = [];

            private class BooksRelation : RelationDefinition<AuthorTable, BookTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.PrimaryGenre, y => y.Genre),
               ];
            }
         }

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            public string Genre { get; set; } = string.Empty;
         }
         """);

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0031");
   }

   private static string PairedColumns(string declaringColumn, string targetColumn)
   {
      return $$"""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            {{declaringColumn}}

            private AuthorRelation? Author { get; set; }

            private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorCode, y => y.Code),
               ];
            }
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            {{targetColumn}}

            public string Name { get; set; } = string.Empty;
         }
         """;
   }

   [Fact]
   public void RelationPairedAgainstANullableUniqueColumn_ReportsNoPGSQL0035_AndRegistersTheRelation()
   {
      var result = GeneratorHarness.RunGenerator(
         PairedColumns(
            "public string AuthorCode { get; set; } = string.Empty;",
            "[Unique] public string? Code { get; set; }"
         )
      );

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0035");

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0031");

      var registration = GeneratorHarness.RegistrationSource(result);
      registration.Should().Contain(".Relation<global::Demo.AuthorData>");
   }

   [Fact]
   public void RelationPairedAgainstANonNullableUniqueColumn_ReportsNoPGSQL0035()
   {
      var result = GeneratorHarness.RunGenerator(
         PairedColumns(
            "public string? AuthorCode { get; set; }",
            "[Unique] public string Code { get; set; } = string.Empty;"
         )
      );

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0035");
   }

   [Fact]
   public void RelationPairedAgainstTwoColumnsThatCanBothHoldNull_ReportsPGSQL0035_AndDropsOnlyThatRelation()
   {
      var result = GeneratorHarness.RunGenerator(
         PairedColumns(
            "[Column(Null = true)] public string AuthorCode { get; set; } = string.Empty;",
            "[Column(Null = true)] public string Code { get; set; } = string.Empty;"
         )
      );

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0035").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
      diagnostic.GetMessage().Should().Contain("AuthorCode").And.Contain("AuthorTable").And.Contain("Code");

      result.GeneratedSources.Should().NotBeEmpty();

      var registration = GeneratorHarness.RegistrationSource(result);
      registration.Should().NotContain(".Relation<global::Demo.AuthorData>");
   }

   [Fact]
   public void RelationPairedAgainstTwoColumnsThatCanBothHoldNull_WhereTheTargetIsUnique_ReportsPGSQL0035()
   {
      var result = GeneratorHarness.RunGenerator(
         PairedColumns(
            "public string? AuthorCode { get; set; }",
            "[Unique] public string? Code { get; set; }"
         )
      );

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0035").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
      diagnostic.GetMessage().Should().Contain("AuthorCode").And.Contain("AuthorTable").And.Contain("Code");
   }

   [Fact]
   public void RelationPairedAgainstTwoNullableTypedColumns_IsNotClearedByANotNullClaim_ButIsClearedByANonNullableType()
   {
      var claimed = GeneratorHarness.RunGenerator(
         PairedColumns(
            "[Column(NotNull = true)] public string? AuthorCode { get; set; }",
            "public string? Code { get; set; }"
         )
      );

      claimed.Diagnostics.Should().Contain(x => x.Id == "PGSQL0021");
      claimed.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0035")
         .Subject.GetMessage().Should().Contain("a type that cannot hold null");

      var cleared = GeneratorHarness.RunGenerator(
         PairedColumns(
            "public string AuthorCode { get; set; } = string.Empty;",
            "public string? Code { get; set; }"
         )
      );

      cleared.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0035");
   }

   [Fact]
   public void RelationPairedAgainstTwoUnannotatedStringsInANullableObliviousFile_ReportsPGSQL0035_AndClearsWithNotNullClaim()
   {
      const string TARGET_COLUMN = "public string Code { get; set; } = string.Empty;";

      var offending = GeneratorHarness.RunGenerator(
         PairedColumns("public string AuthorCode { get; set; } = string.Empty;", TARGET_COLUMN),
         NullableContextOptions.Disable
      );

      offending.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0035");

      var cleared = GeneratorHarness.RunGenerator(
         PairedColumns("[Column(NotNull = true)] public string AuthorCode { get; set; } = string.Empty;", TARGET_COLUMN),
         NullableContextOptions.Disable
      );

      cleared.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0035");

      cleared.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0021");
   }

   [Fact]
   public void RelationWithTwoOffendingPairs_ReportsPGSQL0035Twice()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            [Column(Null = true)]
            public string AuthorCode { get; set; } = string.Empty;

            [Column(Null = true)]
            public string Genre { get; set; } = string.Empty;

            private AuthorRelation? Author { get; set; }

            private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorCode, y => y.Code),
                  Key(x => x.Genre, y => y.PrimaryGenre),
               ];
            }
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            [Column(Null = true)]
            public string Code { get; set; } = string.Empty;

            [Column(Null = true)]
            public string PrimaryGenre { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;
         }
         """);

      result.Diagnostics.Where(x => x.Id == "PGSQL0035").Should().HaveCount(2);
   }

   [Fact]
   public void RelationPairedAgainstTwoColumnsThatCanBothHoldNull_WithARelationConditionExcludingNulls_StillReportsPGSQL0035()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System;
         using System.Collections.Generic;
         using System.Linq.Expressions;

         namespace Demo;

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            [Column(Null = true)]
            public string AuthorCode { get; set; } = string.Empty;

            private AuthorRelation? Author { get; set; }

            private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorCode, y => y.Code),
               ];

               public override Expression<Func<BookTable, AuthorTable, bool>> Condition
                  => (book, author) => author.Code != null;
            }
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            [Column(Null = true)]
            public string Code { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;
         }
         """);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0035");
   }

   private const string FORGOTTEN_CONDITION = """
      using mvdmio.Database.PgSQL.Attributes;
      using mvdmio.Database.PgSQL.Relations;
      using System;
      using System.Collections.Generic;
      using System.Linq.Expressions;

      namespace Demo;

      public enum LinkKind
      {
         Person,
         Asset
      }

      [Table("public.links")]
      public partial class LinkTable
      {
         [PrimaryKey]
         public long LinkId { get; set; }

         public LinkKind Kind { get; set; }
         public long TargetId { get; set; }

         private PersonRelation? Person { get; set; }
         private AllTargetsRelation? AnyTarget { get; set; }

         private class PersonRelation : RelationDefinition<LinkTable, PersonTable>
         {
            public override IReadOnlyList<RelationKey> Keys => [
               Key(x => x.TargetId, y => y.PersonId),
            ];

            public override Expression<Func<LinkTable, PersonTable, bool>> Condition
               => (link, person) => link.Kind == LinkKind.Person;
         }

         // Pairs the exact same columns as PersonRelation above, but forgot to narrow by kind.
         private class AllTargetsRelation : RelationDefinition<LinkTable, PersonTable>
         {
            public override IReadOnlyList<RelationKey> Keys => [
               Key(x => x.TargetId, y => y.PersonId),
            ];
         }
      }

      [Table("public.people")]
      public partial class PersonTable
      {
         [PrimaryKey]
         public long PersonId { get; set; }

         public string Name { get; set; } = string.Empty;
      }
      """;

   [Fact]
   public void UnconditionedRelation_SharingItsKeyPairsWithAConditionedOne_ReportsPGSQL0034()
   {
      var result = GeneratorHarness.RunGenerator(FORGOTTEN_CONDITION);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0034").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
      diagnostic.GetMessage().Should().Contain("LinkTable").And.Contain("AnyTarget");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void UnconditionedRelation_SharingItsKeyPairsWithAConditionedOne_ProducesCodeThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile(FORGOTTEN_CONDITION);
   }

   [Fact]
   public void UnconditionedRelation_SharingItsKeyPairsWithAConditionedOneToADifferentTarget_ReportsPGSQL0034()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System;
         using System.Collections.Generic;
         using System.Linq.Expressions;

         namespace Demo;

         public enum LinkKind
         {
            Person,
            Asset
         }

         [Table("public.links")]
         public partial class LinkTable
         {
            [PrimaryKey]
            public long LinkId { get; set; }

            public LinkKind Kind { get; set; }
            public long TargetId { get; set; }

            private PersonRelation? Person { get; set; }
            private AnyAssetRelation? AnyAsset { get; set; }

            private class PersonRelation : RelationDefinition<LinkTable, PersonTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.TargetId, y => y.PersonId),
               ];

               public override Expression<Func<LinkTable, PersonTable, bool>> Condition
                  => (link, person) => link.Kind == LinkKind.Person;
            }

            private class AnyAssetRelation : RelationDefinition<LinkTable, AssetTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.TargetId, y => y.AssetId),
               ];
            }
         }

         [Table("public.people")]
         public partial class PersonTable
         {
            [PrimaryKey]
            public long PersonId { get; set; }

            public string Name { get; set; } = string.Empty;
         }

         [Table("public.assets")]
         public partial class AssetTable
         {
            [PrimaryKey]
            public long AssetId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0034").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
      diagnostic.GetMessage().Should().Contain("LinkTable").And.Contain("AnyAsset");
   }

   [Fact]
   public void TwoConditionedRelations_SharingTheirKeyPairs_ReportNoPGSQL0034()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System;
         using System.Collections.Generic;
         using System.Linq.Expressions;

         namespace Demo;

         public enum LinkKind
         {
            Person,
            Asset
         }

         [Table("public.links")]
         public partial class LinkTable
         {
            [PrimaryKey]
            public long LinkId { get; set; }

            public LinkKind Kind { get; set; }
            public long TargetId { get; set; }

            private PersonRelation? Person { get; set; }
            private AssetRelation? Asset { get; set; }

            private class PersonRelation : RelationDefinition<LinkTable, PersonTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.TargetId, y => y.PersonId),
               ];

               public override Expression<Func<LinkTable, PersonTable, bool>> Condition
                  => (link, person) => link.Kind == LinkKind.Person;
            }

            private class AssetRelation : RelationDefinition<LinkTable, PersonTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.TargetId, y => y.PersonId),
               ];

               public override Expression<Func<LinkTable, PersonTable, bool>> Condition
                  => (link, asset) => link.Kind == LinkKind.Asset;
            }
         }

         [Table("public.people")]
         public partial class PersonTable
         {
            [PrimaryKey]
            public long PersonId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """);

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0034");
   }

   [Fact]
   public void TwoUnconditionedRelations_SharingTheirKeyPairs_ReportNoPGSQL0034()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.links")]
         public partial class LinkTable
         {
            [PrimaryKey]
            public long LinkId { get; set; }

            public long TargetId { get; set; }

            private FirstRelation? First { get; set; }
            private SecondRelation? Second { get; set; }

            private class FirstRelation : RelationDefinition<LinkTable, PersonTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.TargetId, y => y.PersonId),
               ];
            }

            private class SecondRelation : RelationDefinition<LinkTable, PersonTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.TargetId, y => y.PersonId),
               ];
            }
         }

         [Table("public.people")]
         public partial class PersonTable
         {
            [PrimaryKey]
            public long PersonId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """);

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0034");
   }

   [Fact]
   public void UnconditionedRelation_PairingDifferentColumns_ReportsNoPGSQL0034()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System;
         using System.Collections.Generic;
         using System.Linq.Expressions;

         namespace Demo;

         public enum LinkKind
         {
            Person,
            Asset
         }

         [Table("public.links")]
         public partial class LinkTable
         {
            [PrimaryKey]
            public long LinkId { get; set; }

            public LinkKind Kind { get; set; }
            public long TargetId { get; set; }
            public long OtherId { get; set; }

            private PersonRelation? Person { get; set; }
            private OtherRelation? Other { get; set; }

            private class PersonRelation : RelationDefinition<LinkTable, PersonTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.TargetId, y => y.PersonId),
               ];

               public override Expression<Func<LinkTable, PersonTable, bool>> Condition
                  => (link, person) => link.Kind == LinkKind.Person;
            }

            private class OtherRelation : RelationDefinition<LinkTable, PersonTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.OtherId, y => y.PersonId),
               ];
            }
         }

         [Table("public.people")]
         public partial class PersonTable
         {
            [PrimaryKey]
            public long PersonId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """);

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0034");
   }

   [Fact]
   public void ConditionedRelation_PairingTheTenancyColumnOnBothSides_ReportsNoPGSQL0027()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System;
         using System.Collections.Generic;
         using System.Linq.Expressions;

         namespace Demo;

         public enum LinkKind
         {
            Person,
            Asset
         }

         [Table("public.links")]
         public partial class LinkTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            [PrimaryKey]
            public long LinkId { get; set; }

            public LinkKind Kind { get; set; }
            public long TargetId { get; set; }

            private PersonRelation? Person { get; set; }

            private class PersonRelation : RelationDefinition<LinkTable, PersonTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AccountId, y => y.AccountId),
                  Key(x => x.TargetId, y => y.PersonId),
               ];

               public override Expression<Func<LinkTable, PersonTable, bool>> Condition
                  => (link, person) => link.Kind == LinkKind.Person;
            }
         }

         [Table("public.people")]
         public partial class PersonTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            [PrimaryKey]
            public long PersonId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """);

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0027");
   }

   [Fact]
   public void RelationPairingTheTenancyColumnAlonePlusACondition_WhereTheTargetsWholePrimaryKeyIsTheTenancyColumn_ReportsNoWarning()
   {
      var result = GeneratorHarness.RunGenerator("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System;
         using System.Collections.Generic;
         using System.Linq.Expressions;

         namespace Demo;

         [Table("public.documents")]
         public partial class DocumentTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            [PrimaryKey]
            public long DocumentId { get; set; }

            public string Title { get; set; } = string.Empty;

            private ProfileRelation? Profile { get; set; }

            private class ProfileRelation : RelationDefinition<DocumentTable, ProfileTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AccountId, y => y.AccountId),
               ];

               public override Expression<Func<DocumentTable, ProfileTable, bool>> Condition
                  => (document, profile) => profile.IsActive;
            }
         }

         [Table("public.profiles")]
         public partial class ProfileTable
         {
            [Column(Tenancy = true)]
            [PrimaryKey]
            public long AccountId { get; set; }

            public bool IsActive { get; set; }
         }
         """);

      result.Diagnostics.Should().BeEmpty();
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void EveryKeyClaimsShape_EmitsSourceThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            public long? AuthorId { get; set; }

            private AuthorRelation? Author { get; set; }

            private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorId, y => y.AuthorId),
               ];
            }
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """);
   }

   [Fact]
   public void EveryValueTypeNullabilityCombination_BindsAgainstTheOneKeyOverload_AndCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile("""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            public long AuthorIdNotNull { get; set; }
            public long? AuthorIdNullable { get; set; }

            private NotNullToNotNullRelation? NotNullToNotNull { get; set; }
            private NullableToNotNullRelation? NullableToNotNull { get; set; }
            private NotNullToNullableRelation? NotNullToNullable { get; set; }
            private NullableToNullableRelation? NullableToNullable { get; set; }

            private class NotNullToNotNullRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorIdNotNull, y => y.KeyNotNull),
               ];
            }

            private class NullableToNotNullRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorIdNullable, y => y.KeyNotNull),
               ];
            }

            private class NotNullToNullableRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorIdNotNull, y => y.KeyNullable),
               ];
            }

            private class NullableToNullableRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorIdNullable, y => y.KeyNullable),
               ];
            }
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            public long KeyNotNull { get; set; }
            public long? KeyNullable { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """);
   }
}
