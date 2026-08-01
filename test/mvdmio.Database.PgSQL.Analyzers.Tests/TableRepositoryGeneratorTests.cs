using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

public class TableRepositoryGeneratorTests
{
   [Fact]
   public void ValidTable_GeneratesCrudTypes()
   {
      var source = """
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.users")]
         public partial class UserTable
         {
            [PrimaryKey]
            [Generated]
            public long UserId { get; set; }

            [Unique]
            public string UserName { get; set; } = string.Empty;

            [Column("firstName")]
            public string FirstName { get; set; } = string.Empty;
         }
         """;

      var result = GeneratorHarness.RunGenerator(source);

      result.Diagnostics.Should().BeEmpty();
      result.GeneratedSources.Should().HaveCount(2);

      var generatedSource = GeneratorHarness.RepositorySource(result);
      var registrationSource = GeneratorHarness.RegistrationSource(result);
      generatedSource.Should().Contain("public partial class UserData");
      generatedSource.Should().Contain("public partial class CreateUserCommand");
      generatedSource.Should().Contain("public partial class UpdateUserCommand");
      generatedSource.Should().Contain("public partial interface IUserRepository");
      generatedSource.Should().Contain("public partial class UserRepository");
      generatedSource.Should().Contain("public partial class UserRepository : IUserRepository");
      registrationSource.Should().Contain("namespace GeneratorTests;");
      registrationSource.Should().Contain("AddGeneratorTests(this IServiceCollection services)");
      registrationSource.Should().Contain("services.TryAddScoped<global::Demo.IUserRepository, global::Demo.UserRepository>();");

      // The primary key's lookup and delete are named after the key rather than after UserId, and a single-column key
      // gets the same name a composite one does — see TableRepositoryGeneratorCompositeKeyTests.
      generatedSource.Should().Contain("GetByPrimaryKeyAsync(long userId, CancellationToken ct = default)");
      generatedSource.Should().Contain("DeleteByPrimaryKeyAsync(long userId, CancellationToken ct = default)");
      generatedSource.Should().NotContain("GetByUserIdAsync");
      generatedSource.Should().NotContain("DeleteByUserIdAsync");

      generatedSource.Should().Contain("GetByUserNameAsync");
      generatedSource.Should().Contain("DeleteByUserNameAsync");
      generatedSource.Should().Contain("INSERT INTO \"public\".\"users\" (\"user_name\", \"firstName\")");
      generatedSource.Should().Contain("RETURNING \"user_id\" AS \"UserId\", \"user_name\" AS \"UserName\", \"firstName\" AS \"FirstName\"");
      generatedSource.Should().Contain("IQueryable<UserData> Query(TimeSpan? commandTimeout = null);");
      generatedSource.Should().Contain("return _db.Linq.Query<UserData>(commandTimeout);");
      registrationSource.Should().Contain("[global::System.Runtime.CompilerServices.ModuleInitializer]");
      registrationSource.Should().Contain("QueryMappings.Register<global::Demo.UserData>(");
      registrationSource.Should().Contain(".Column(x => x.UserId, \"user_id\", isPrimaryKey: true)");
      registrationSource.Should().Contain(".Column(x => x.FirstName, \"firstName\", isNotNull: true)");
   }

   [Fact]
   public void ValidTable_ProducesCodeThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile("""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.users")]
         public partial class UserTable
         {
            [PrimaryKey]
            [Generated]
            public long UserId { get; set; }

            [Unique]
            public string UserName { get; set; } = string.Empty;
         }
         """);
   }

   [Fact]
   public void MappablePropertyTypes_ProduceNoWarning()
   {
      var source = """
         using mvdmio.Database.PgSQL.Attributes;
         using System;
         using System.Collections.Generic;

         namespace Demo;

         public enum Status { Active, Archived }

         [Table("public.everything")]
         public partial class EverythingTable
         {
            [PrimaryKey]
            public Guid Id { get; set; }

            public bool Flag { get; set; }
            public byte Tiny { get; set; }
            public sbyte SignedTiny { get; set; }
            public short Small { get; set; }
            public ushort UnsignedSmall { get; set; }
            public int Number { get; set; }
            public uint UnsignedNumber { get; set; }
            public long Big { get; set; }
            public ulong UnsignedBig { get; set; }
            public float Single { get; set; }
            public double Double { get; set; }
            public decimal Money { get; set; }
            public char Letter { get; set; }
            public string Text { get; set; } = string.Empty;
            public string? OptionalText { get; set; }
            public byte[] Blob { get; set; } = Array.Empty<byte>();
            public DateTime Moment { get; set; }
            public DateTimeOffset OffsetMoment { get; set; }
            public DateOnly Day { get; set; }
            public TimeOnly Time { get; set; }
            public TimeSpan Duration { get; set; }
            public int? OptionalNumber { get; set; }
            public DateOnly? OptionalDay { get; set; }
            public Status State { get; set; }
            public Status? OptionalState { get; set; }
            public Uri? Link { get; set; }
            public Dictionary<string, string>? Metadata { get; set; }
         }
         """;

      var result = GeneratorHarness.RunGenerator(source);

      result.Diagnostics.Should().NotContain(x => x.Id == "PGSQL0011");
   }

   [Fact]
   public void UnmappablePropertyType_ProducesWarning()
   {
      var source = """
         using mvdmio.Database.PgSQL.Attributes;
         using System.Collections.Generic;

         namespace Demo;

         public class Address
         {
            public string City { get; set; } = string.Empty;
         }

         [Table("public.users")]
         public partial class UserTable
         {
            [PrimaryKey]
            public long UserId { get; set; }

            public string UserName { get; set; } = string.Empty;
            public Address? HomeAddress { get; set; }
            public List<string> Tags { get; set; } = new();
            public Dictionary<string, int>? Counters { get; set; }
         }
         """;

      var result = GeneratorHarness.RunGenerator(source);

      var warnings = result.Diagnostics.Where(x => x.Id == "PGSQL0011").ToList();

      warnings.Should().HaveCount(3);
      warnings.Should().AllSatisfy(x => x.Severity.Should().Be(DiagnosticSeverity.Warning));
      warnings.Select(x => x.GetMessage()).Should().Contain(x => x.Contains("HomeAddress", StringComparison.Ordinal));
      warnings.Select(x => x.GetMessage()).Should().Contain(x => x.Contains("Tags", StringComparison.Ordinal));
      warnings.Select(x => x.GetMessage()).Should().Contain(x => x.Contains("Counters", StringComparison.Ordinal));
      result.GeneratedSources.Should().NotBeEmpty("the warning must not stop generation");
   }

   [Fact]
   public void NonPartialTable_ProducesDiagnostic()
   {
      var source = """
         using mvdmio.Database.PgSQL.Attributes;

         [Table("public.users")]
         public class UserTable
         {
            [PrimaryKey]
            public long UserId { get; set; }

            public string UserName { get; set; } = string.Empty;
         }
         """;

      var result = GeneratorHarness.RunGenerator(source);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0003");
      result.GeneratedSources.Should().BeEmpty();
   }

   [Fact]
   public void UnsupportedPropertyShape_ProducesDiagnostic()
   {
      var source = """
         using mvdmio.Database.PgSQL.Attributes;

         [Table("public.users")]
         public partial class UserTable
         {
            [PrimaryKey]
            public long UserId { get; set; }

            public string this[int index]
            {
               get => string.Empty;
               set { }
            }
         }
         """;

      var result = GeneratorHarness.RunGenerator(source);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0009");
      result.GeneratedSources.Should().BeEmpty();
   }

   [Fact]
   public void GeneratedTypeNameCollision_ProducesDiagnostic()
   {
      var source = """
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         public class UserRepository
         {
         }

         [Table("public.users")]
         public partial class UserTable
         {
            [PrimaryKey]
            public long UserId { get; set; }

            public string UserName { get; set; } = string.Empty;
         }
         """;

      var result = GeneratorHarness.RunGenerator(source);

      var collision = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0010").Subject;

      collision.GetMessage().Should().Contain("non-partial type");
      result.GeneratedSources.Should().BeEmpty();
   }

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
         public long? EditorId { get; set; }

         private AuthorRelation? Author { get; set; }
         private EditorRelation? Editor { get; set; }

         private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
         {
            public override IReadOnlyList<RelationKey> Keys => [
               Key(x => x.AuthorId, y => y.AuthorId),
            ];
         }

         private class EditorRelation : RelationDefinition<BookTable, AuthorTable>
         {
            public override IReadOnlyList<RelationKey> Keys => [
               Key(x => x.EditorId, y => y.AuthorId),
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

         private List<BooksRelation> Books { get; set; } = new();

         private class BooksRelation : RelationDefinition<AuthorTable, BookTable>
         {
            public override IReadOnlyList<RelationKey> Keys => [
               Key(x => x.AuthorId, y => y.AuthorId),
            ];
         }
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

      bookRelations.Should().Contain("public partial class BookData");
      bookRelations.Should().Contain("public global::Demo.AuthorData? Author { get; set; }");
      bookRelations.Should().Contain("public global::Demo.AuthorData? Editor { get; set; }");
      authorRelations.Should().Contain("public global::System.Collections.Generic.List<global::Demo.BookData> Books { get; set; } = new();");

      // A relation joining one pair of columns registers through the predicate overload, the same as a composite one.
      registration.Should().Contain(".Relation<global::Demo.AuthorData>(x => x.Author, (x, y) => x.AuthorId == y.AuthorId)");
      registration.Should().Contain(".Relation<global::Demo.AuthorData>(x => x.Editor, (x, y) => x.EditorId == y.AuthorId)");
      registration.Should().Contain(".Relation<global::Demo.BookData>(x => x.Books, (x, y) => x.AuthorId == y.AuthorId)");
   }

   [Fact]
   public void ValidRelations_ProduceCodeThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile(VALID_RELATIONS);
   }

   [Fact]
   public void RelationToSomethingThatIsNotATableDefinition_ProducesDiagnostic()
   {
      var source = """
         using mvdmio.Database.PgSQL.Attributes;
         using mvdmio.Database.PgSQL.Relations;
         using System.Collections.Generic;

         namespace Demo;

         public class Elsewhere
         {
            public long Id { get; set; }
         }

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            public long? AuthorId { get; set; }

            private AuthorRelation? Author { get; set; }

            private class AuthorRelation : RelationDefinition<BookTable, Elsewhere>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.AuthorId, y => y.Id),
               ];
            }
         }
         """;

      var result = GeneratorHarness.RunGenerator(source);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0014");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationToOneRowThatIsNotNullable_ProducesDiagnostic()
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

            private AuthorRelation Author { get; set; } = new();

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

   /// <remarks>
   ///    Reaching PGSQL0016 takes an unusual shape now that the relation split is entirely type-driven: the relation
   ///    property's own type is a perfectly good relation definition, so what has to be malformed is what its
   ///    <c>TTarget</c> type argument resolves to. An array satisfies the <c>where TTarget : class</c> constraint —
   ///    arrays are reference types — but is not a named type, so it can never be a table definition.
   /// </remarks>
   [Fact]
   public void RelationTargetingSomethingThatIsNotANamedType_ProducesDiagnostic()
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

            public long BookCount { get; set; }

            private AuthorRelation? Author { get; set; }

            private class AuthorRelation : RelationDefinition<BookTable, AuthorTable[]>
            {
               public override IReadOnlyList<RelationKey> Keys => [
                  Key(x => x.BookCount, y => y.LongLength),
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

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0016");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationOnAnUnsupportedPropertyShape_ProducesDiagnostic()
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

            private AuthorRelation? Author => null;

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

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0017");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationCombinedWithAColumnAttribute_ProducesDiagnostic()
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

            [Column("author")]
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

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0018");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationAttributeOnAPropertyThatIsNotARelation_ProducesDiagnostic()
   {
      var source = """
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            public long? AuthorId { get; set; }

            [Relation]
            public AuthorTable? Author { get; set; }
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

      var diagnostic = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0033").Subject;

      diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
      diagnostic.GetMessage().Should().Contain("Author");
      // Author is still an ordinary column candidate — an unmapped, unattributed AuthorTable-typed reference type
      // that the query surface cannot map — so the table still generates around it.
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationAttributeOnAnUnsupportedCollectionOfARelationDefinition_ProducesDiagnostic()
   {
      // A HashSet is not a supported to-many collection type, so even wrapping a genuine relation definition does
      // not make this a relation — the marker attribute is simply wrong here, the same as it would be on any other
      // non-relation property.
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
            private HashSet<AuthorRelation> Authors { get; set; } = new();

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

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0033");
      result.GeneratedSources.Should().NotBeEmpty();
   }
}
