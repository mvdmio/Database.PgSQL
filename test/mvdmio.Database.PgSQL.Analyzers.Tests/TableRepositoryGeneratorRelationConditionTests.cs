using AwesomeAssertions;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

/// <summary>
///    Covers a relation definition's <c>Condition</c>: the lift of its body into the emitted join, the rewrite of its
///    two parameters to the generated join lambda's own ("x" and "y"), a constant reaching the join as a literal
///    rather than a parameter, reaching through another relation property inside a condition, a condition calling
///    something the Query surface may not translate, and <c>PGSQL0032</c>.
/// </summary>
public class TableRepositoryGeneratorRelationConditionTests
{
   private const string CONDITIONED_RELATIONS = """
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

         private class PersonRelation : RelationDefinition<LinkTable, PersonTable>
         {
            public override IReadOnlyList<RelationKey> Keys => [
               Key(x => x.TargetId, y => y.PersonId),
            ];

            public override Expression<Func<LinkTable, PersonTable, bool>> Condition
               => (link, person) => link.Kind == LinkKind.Person;
         }
      }

      [Table("public.people")]
      public partial class PersonTable
      {
         [PrimaryKey]
         public long PersonId { get; set; }

         public string Name { get; set; } = string.Empty;

         private List<LinksRelation> Links { get; set; } = [];

         private class LinksRelation : RelationDefinition<PersonTable, LinkTable>
         {
            public override IReadOnlyList<RelationKey> Keys => [
               Key(x => x.PersonId, y => y.TargetId),
            ];

            public override Expression<Func<PersonTable, LinkTable, bool>> Condition
               => (person, link) => link.Kind == LinkKind.Person;
         }
      }
      """;

   [Fact]
   public void AConditionedRelation_ProducesNoDiagnostics()
   {
      var result = GeneratorHarness.RunGenerator(CONDITIONED_RELATIONS);

      result.Diagnostics.Should().BeEmpty();
   }

   [Fact]
   public void AConditionedRelation_ProducesCodeThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile(CONDITIONED_RELATIONS);
   }

   [Fact]
   public void TheConditionsBody_IsLiftedIntoTheJoinAlongsideThePairs_WithItsParametersRewritten()
   {
      var result = GeneratorHarness.RunGenerator(CONDITIONED_RELATIONS);
      var registration = GeneratorHarness.RegistrationSource(result);

      // The key pair and the condition are combined with &&, and the condition's own parameters ("link" and
      // "person") are rewritten to the join lambda's own ("x" and "y") rather than left as the developer wrote them.
      registration.Should().Contain(".Relation<global::Demo.PersonData>(x => x.Person, (x, y) => x.TargetId == y.PersonId && (x.Kind == global::LinqToDB.Sql.Constant(global::Demo.LinkKind.Person)))");
      registration.Should().Contain(".Relation<global::Demo.LinkData>(x => x.Links, (x, y) => x.PersonId == y.TargetId && (y.Kind == global::LinqToDB.Sql.Constant(global::Demo.LinkKind.Person)))");
   }

   [Fact]
   public void AConstantInTheCondition_ReachesTheEmittedJoinAsALiteral_NotAsAParameter()
   {
      var result = GeneratorHarness.RunGenerator(CONDITIONED_RELATIONS);
      var registration = GeneratorHarness.RegistrationSource(result);

      // The enum member is inlined verbatim (fully qualified, since the emitted file carries none of the developer's
      // own using directives) and wrapped in LinqToDB.Sql.Constant, which is what tells the query surface to inline
      // it into the join as a literal rather than parameterizing it for query-plan reuse.
      registration.Should().Contain("global::LinqToDB.Sql.Constant(global::Demo.LinkKind.Person)");
   }

   [Fact]
   public void OmittingTheCondition_BehavesExactlyAsInStep02()
   {
      const string SOURCE = """
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
         """;

      var result = GeneratorHarness.RunGenerator(SOURCE);
      var registration = GeneratorHarness.RegistrationSource(result);

      result.Diagnostics.Should().BeEmpty();
      registration.Should().Contain(".Relation<global::Demo.AuthorData>(x => x.Author, (x, y) => x.AuthorId == y.AuthorId)");
   }

   [Fact]
   public void AConditionReachingThroughAnotherRelationProperty_Resolves()
   {
      const string SOURCE = """
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

            public long? AuthorId { get; set; }

            // Public, so the Edition table's condition below — declared outside BookTable — can reach it. The nested
            // class is public too, because a public property cannot expose a less accessible type.
            public AuthorRelation? Author { get; set; }

            public class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
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

         [Table("public.editions")]
         public partial class EditionTable
         {
            [PrimaryKey]
            public long EditionId { get; set; }

            public long BookId { get; set; }

            private BookRelation? Book { get; set; }

            private class BookRelation : RelationDefinition<EditionTable, BookTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.BookId, y => y.BookId),
               ];

               // Touches Book.Author directly — a relation property on the other table rather than a mapped
               // column. A relation property on a generated data type is a member like any other, so this resolves.
               public override Expression<Func<EditionTable, BookTable, bool>> Condition
                  => (edition, book) => book.Author != null;
            }
         }
         """;

      var result = GeneratorHarness.RunGenerator(SOURCE);

      result.Diagnostics.Should().BeEmpty();
      GeneratorHarness.AssertGeneratedSourcesCompile(SOURCE);
   }

   [Fact]
   public void AConditionCallingSomethingTheQuerySurfaceMayNotTranslate_StillBuilds()
   {
      const string SOURCE = """
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

            public string Title { get; set; } = string.Empty;
            public long? AuthorId { get; set; }

            private AuthorRelation? Author { get; set; }

            private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorId, y => y.AuthorId),
               ];

               // A call the Query surface may refuse at run time — the library does not refuse it at build time,
               // because it has no test for what a Query front-end can and cannot translate.
               public override Expression<Func<BookTable, AuthorTable, bool>> Condition
                  => (book, author) => book.Title.GetHashCode() > 0;
            }
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """;

      var result = GeneratorHarness.RunGenerator(SOURCE);

      result.Diagnostics.Should().BeEmpty();
      GeneratorHarness.AssertGeneratedSourcesCompile(SOURCE);
   }

   [Fact]
   public void AConditionTouchingAMemberWithNoCounterpartOnTheGeneratedDataType_ProducesDiagnostic_AndDropsOnlyThatRelation()
   {
      const string SOURCE = """
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

            public long? AuthorId { get; set; }

            private AuthorRelation? Author { get; set; }

            private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorId, y => y.AuthorId),
               ];

               // ToString is a real, compiling member reached directly on the parameter, but it is neither a mapped
               // column nor a relation property, so it has no counterpart on AuthorTable's generated data type.
               public override Expression<Func<BookTable, AuthorTable, bool>> Condition
                  => (book, author) => author.ToString() == "Bilbo";
            }
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """;

      var result = GeneratorHarness.RunGenerator(SOURCE);

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0032").Subject;
      diagnostic.GetMessage().Should().Contain("ToString");
      diagnostic.GetMessage().Should().Contain("AuthorTable");

      // The relation is dropped, but the table itself still generates — with no relations left to mirror, it does
      // not even get a Relations.g.cs file.
      result.GeneratedSources.Should().Contain(x => x.HintName == "Demo_BookTable.Repository.g.cs");
      result.GeneratedSources.Should().NotContain(x => x.HintName == "Demo_BookTable.Relations.g.cs");
   }
}
