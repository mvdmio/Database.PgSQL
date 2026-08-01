using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

/// <summary>
///    Covers what a relation's resolved key pairs have to claim, replacing the old foreign-key-arity check: that a
///    relation to one row pairs against something the target claims unique (<c>PGSQL0031</c>), that it never pairs
///    against a nullable <c>[Unique]</c> column (<c>PGSQL0035</c>), and that a conditioned relation and an
///    unconditioned one sharing the same key pairs are flagged as a forgotten condition (<c>PGSQL0034</c>). These
///    checks read the pairs the resolver produced, so each is exercised through the definition form.
/// </summary>
public class TableRepositoryGeneratorRelationKeyClaimsTests
{
   // PGSQL0031 — a relation to one row whose pairs contain nothing the target claims unique.

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
      // A natural key, not the primary key — a first-class way to relate per the spec.
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
      // A superset of a unique set is still unique and must pass — the whole primary key plus an extra column.
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
      // A claim, not a check: a Relation condition that happens to make the pairing unique at run time does not
      // suppress the warning, because the generator never evaluates it — it only reads what the pairs themselves
      // claim.
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
      // Reaching several rows is the point of a relation to many, so the uniqueness claim does not apply to it.
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

   // PGSQL0035 — a relation pairing against a target column marked [Unique] that is nullable.

   [Fact]
   public void RelationPairedAgainstANullableUniqueColumn_ReportsPGSQL0035_AndDropsOnlyThatRelation()
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
            public string? Code { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0035").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
      diagnostic.GetMessage().Should().Contain("AuthorTable").And.Contain("Code");

      // The relation is dropped, but the rest of the table still generates.
      result.GeneratedSources.Should().NotBeEmpty();

      var registration = GeneratorHarness.RegistrationSource(result);
      registration.Should().NotContain(".Relation<global::Demo.AuthorData>");
   }

   [Fact]
   public void RelationPairedAgainstANonNullableUniqueColumn_ReportsNoPGSQL0035()
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

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0035");
   }

   // PGSQL0034 — a conditioned relation and an unconditioned one over the same key pairs, declared on one table.

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
   public void TwoConditionedRelations_SharingTheirKeyPairs_ReportNoPGSQL0034()
   {
      // Both narrow by kind, so neither silently resolves every kind — the shape PGSQL0034 exists to permit.
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
      // Neither carries a condition, so neither is "forgetting" one relative to the other — nothing to warn about.
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
      // A conditioned relation and an unconditioned one exist on the same table, but they pair different columns —
      // not the same shape at all, so nothing is forgotten.
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

   // Tenancy across the pairs — PGSQL0027 permits the shape a conditioned relation exists to enable.

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
      // The shape the Settled section carves out: a per-tenant singleton whose whole primary key is the tenancy
      // column, reached by pairing that one column plus a condition — unique by construction, so PGSQL0031 stays
      // quiet, and pair-based, so PGSQL0027 stays quiet too.
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
}
