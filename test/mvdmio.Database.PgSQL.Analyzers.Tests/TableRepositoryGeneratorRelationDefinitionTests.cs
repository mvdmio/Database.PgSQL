using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

/// <summary>
///    Covers a relation declared as a class deriving from <c>RelationDefinition&lt;,&gt;</c>: the type-driven
///    relation-property split, a nested and an externally declared definition class, a relation to one row and to
///    many rows, the nullable-left key overload, pair order, and the three diagnostics this shape owns
///    (<c>PGSQL0028</c>, <c>PGSQL0029</c>, <c>PGSQL0030</c>) alongside the reused <c>PGSQL0014</c> and
///    <c>PGSQL0015</c>.
/// </summary>
public class TableRepositoryGeneratorRelationDefinitionTests
{
   private const string VALID_RELATIONS = """
      using mvdmio.Database.PgSQL.Attributes;
      using mvdmio.Database.PgSQL.Relations;
      using System.Collections.Generic;

      namespace Demo;

      [Table("public.books")]
      public partial class BookTable
      {
         [PrimaryKey]
         [Generated]
         public long BookId { get; set; }

         public string Title { get; set; } = string.Empty;
         public long? AuthorId { get; set; }

         // Nested privately inside the table definition it belongs to. The property is private too — C# itself
         // requires that, since a public member cannot expose a less accessible type — and nothing needs it to be
         // any more accessible, because a table definition is purely declarative and nothing ever reads this
         // property's value at run time.
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
         [Generated]
         public long AuthorId { get; set; }

         public string Name { get; set; } = string.Empty;

         public List<BooksRelation> Books { get; set; } = new();
      }

      // Declared outside the table definition it belongs to.
      public class BooksRelation : RelationDefinition<AuthorTable, BookTable>
      {
         public override IReadOnlyList<RelationKey> Keys => [
            Key(x => x.AuthorId, y => y.AuthorId),
         ];
      }
      """;

   [Fact]
   public void ValidRelations_ProduceNoDiagnostics_AndMirrorTheRelationsOntoTheDataTypes()
   {
      var result = GeneratorHarness.RunGenerator(VALID_RELATIONS);

      result.Diagnostics.Should().BeEmpty();

      var bookRelations = GeneratorHarness.GeneratedSource(result, "Demo_BookTable.Relations.g.cs");
      var authorRelations = GeneratorHarness.GeneratedSource(result, "Demo_AuthorTable.Relations.g.cs");
      var registration = GeneratorHarness.RegistrationSource(result);

      bookRelations.Should().Contain("public global::Demo.AuthorData? Author { get; set; }");
      authorRelations.Should().Contain("public global::System.Collections.Generic.List<global::Demo.BookData> Books { get; set; } = new();");

      // Emission is unchanged from the predicate-based association step 01 settled on, whatever the declaration form.
      registration.Should().Contain(".Relation<global::Demo.AuthorData>(x => x.Author, (x, y) => x.AuthorId == y.AuthorId)");
      registration.Should().Contain(".Relation<global::Demo.BookData>(x => x.Books, (x, y) => x.AuthorId == y.AuthorId)");
   }

   [Fact]
   public void ValidRelations_ProduceCodeThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile(VALID_RELATIONS);
   }

   [Fact]
   public void RelationAttributeOnARelationDefinitionTypedProperty_IsStillAccepted()
   {
      var source = """
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

            [Relation]
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

      var result = GeneratorHarness.RunGenerator(source);

      result.Diagnostics.Should().BeEmpty();
      result.GeneratedSources.Should().NotBeEmpty();
   }

   /// <summary>A book carrying whichever pair order the caller writes, for the composite-pair order-independence test.</summary>
   private static string CompositeRelationSource(string firstPair, string secondPair)
   {
      return $$"""
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.links")]
         public partial class LinkTable
         {
            [PrimaryKey]
            public long TenantId { get; set; }

            [PrimaryKey]
            public long LinkId { get; set; }

            public long TargetId { get; set; }

            // Private too, to match the nested private class — see the remark on VALID_RELATIONS above.
            private TargetRelation? Target { get; set; }

            private class TargetRelation : RelationDefinition<LinkTable, TargetTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  {{firstPair}},
                  {{secondPair}},
               ];
            }
         }

         [Table("public.targets")]
         public partial class TargetTable
         {
            [PrimaryKey]
            public long TenantId { get; set; }

            [PrimaryKey]
            public long TargetId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """;
   }

   [Fact]
   public void ThePairsWrittenOrder_DoesNotChangeTheEmittedJoin()
   {
      const string TENANT_PAIR = "Key(x => x.TenantId, y => y.TenantId)";
      const string TARGET_PAIR = "Key(x => x.TargetId, y => y.TargetId)";

      var tenantFirst = GeneratorHarness.RunGenerator(CompositeRelationSource(TENANT_PAIR, TARGET_PAIR));
      var targetFirst = GeneratorHarness.RunGenerator(CompositeRelationSource(TARGET_PAIR, TENANT_PAIR));

      tenantFirst.Diagnostics.Should().BeEmpty();
      targetFirst.Diagnostics.Should().BeEmpty();

      var tenantFirstRegistration = GeneratorHarness.RegistrationSource(tenantFirst);
      var targetFirstRegistration = GeneratorHarness.RegistrationSource(targetFirst);

      // Both equalities are present whichever pair was written first, because the pairs are combined with && and
      // carry no meaning from their own order.
      tenantFirstRegistration.Should().Contain("x.TenantId == y.TenantId");
      tenantFirstRegistration.Should().Contain("x.TargetId == y.TargetId");
      targetFirstRegistration.Should().Contain("x.TenantId == y.TenantId");
      targetFirstRegistration.Should().Contain("x.TargetId == y.TargetId");
   }

   [Fact]
   public void ThePairsWrittenOrder_ProducesCodeThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile(
         CompositeRelationSource("Key(x => x.TenantId, y => y.TenantId)", "Key(x => x.TargetId, y => y.TargetId)")
      );
   }

   [Fact]
   public void RelationDefinitionDeclaringTableMismatch_ProducesDiagnostic()
   {
      var source = """
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

            public WrongRelation? Author { get; set; }
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            public string Name { get; set; } = string.Empty;
         }

         // TDeclaring names AuthorTable, but the property carrying it is declared on BookTable.
         public class WrongRelation : RelationDefinition<AuthorTable, AuthorTable>
         {
            public override IReadOnlyList<RelationKey> Keys => [
               Key(x => x.AuthorId, y => y.AuthorId),
            ];
         }
         """;

      var result = GeneratorHarness.RunGenerator(source);

      var mismatch = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0028").Subject;
      mismatch.GetMessage().Should().Contain("AuthorTable");
      result.GeneratedSources.Should().NotBeEmpty("one invalid relation must not stop the table from generating");
   }

   [Fact]
   public void RelationDefinitionWithNoKeys_ProducesDiagnostic()
   {
      var source = """
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

            public EmptyRelation? Author { get; set; }

            private class EmptyRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [];
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

      var result = GeneratorHarness.RunGenerator(source);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0029");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationDefinitionKeyThatIsNotAColumnReference_ProducesDiagnostic()
   {
      var source = """
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

            public BadRelation? Author { get; set; }

            private class BadRelation : RelationDefinition<BookTable, AuthorTable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorId ?? 0, y => y.AuthorId),
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

      var result = GeneratorHarness.RunGenerator(source);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0030");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationDefinitionTargetingSomethingThatIsNotATableDefinition_ProducesDiagnostic()
   {
      var source = """
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System.Collections.Generic;

         namespace Demo;

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            public long? SomeId { get; set; }

            public SomeRelation? Something { get; set; }

            private class SomeRelation : RelationDefinition<BookTable, NotATable>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.SomeId, y => y.Id),
               ];
            }
         }

         public class NotATable
         {
            public long Id { get; set; }
         }
         """;

      var result = GeneratorHarness.RunGenerator(source);

      var notATable = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0014").Subject;
      notATable.GetMessage().Should().Contain("NotATable");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationDefinitionToOneRowThatIsNotNullable_ProducesDiagnostic()
   {
      var source = """
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

            public AuthorRelation Author { get; set; } = null!;

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

      var result = GeneratorHarness.RunGenerator(source);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0015");
      result.GeneratedSources.Should().NotBeEmpty();
   }
}
