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
      registrationSource.Should().Contain(".Column(x => x.FirstName, \"firstName\")");
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

         [Relation(nameof(AuthorId))]
         public AuthorTable? Author { get; set; }

         [Relation(nameof(EditorId))]
         public AuthorTable? Editor { get; set; }
      }

      [Table("public.authors")]
      public partial class AuthorTable
      {
         [PrimaryKey]
         [Generated]
         public long AuthorId { get; set; }

         public string Name { get; set; } = string.Empty;

         [Relation(nameof(BookTable.AuthorId))]
         public List<BookTable> Books { get; set; } = new();
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

      // A relation joining one pair of columns keeps the key-based registration it has always used, unchanged.
      registration.Should().Contain(".Relation<global::Demo.AuthorData, long?, long>(x => x.Author, x => x.AuthorId, x => x.AuthorId)");
      registration.Should().Contain(".Relation<global::Demo.AuthorData, long?, long>(x => x.Editor, x => x.EditorId, x => x.AuthorId)");
      registration.Should().Contain(".Relation<global::Demo.BookData, long, long?>(x => x.Books, x => x.AuthorId, x => x.AuthorId)");
   }

   [Fact]
   public void ValidRelations_ProduceCodeThatCompiles()
   {
      GeneratorHarness.AssertGeneratedSourcesCompile(VALID_RELATIONS);
   }

   /// <remarks>
   ///    A property typed as a concrete list matches both <c>Relation</c> overloads, so generated code states its type
   ///    arguments. A hand-written call has to resolve without them, and this holds the overload set to that.
   /// </remarks>
   [Fact]
   public void AHandWrittenRelationCall_ResolvesWithoutTypeArguments()
   {
      var source = $$"""
         {{VALID_RELATIONS}}

         public static class HandWritten
         {
            public static void Register(mvdmio.Database.PgSQL.Connectors.Linq.QueryEntityMappingBuilder<AuthorData> builder)
            {
               builder.Relation(x => x.Books, x => x.AuthorId, x => x.AuthorId);
            }
         }
         """;

      GeneratorHarness.AssertGeneratedSourcesCompile(source);
   }

   [Fact]
   public void RelationWithAnUnknownForeignKey_ProducesDiagnostic()
   {
      var result = GeneratorHarness.RunGenerator(RelationSource("""
         [Relation("NoSuchProperty")]
            public AuthorTable? Author { get; set; }
         """));

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0012");
      result.Diagnostics.Single(x => x.Id == "PGSQL0012").Severity.Should().Be(DiagnosticSeverity.Error);
      result.GeneratedSources.Should().NotBeEmpty("one invalid relation must not stop the table from generating");
   }

   [Fact]
   public void RelationWithAForeignKeyThatCannotMatchThePrimaryKey_ProducesDiagnostic()
   {
      var result = GeneratorHarness.RunGenerator(RelationSource("""
         [Relation(nameof(Title))]
            public AuthorTable? Author { get; set; }
         """));

      var mismatch = result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0013").Subject;

      // The position is named even on a single-column key, so one message shape covers both.
      mismatch.GetMessage().Should().Contain("at key position 1");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationToSomethingThatIsNotATableDefinition_ProducesDiagnostic()
   {
      var source = """
         using mvdmio.Database.PgSQL.Attributes;

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

            [Relation(nameof(AuthorId))]
            public Elsewhere? Author { get; set; }
         }
         """;

      var result = GeneratorHarness.RunGenerator(source);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0014");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationToOneRowThatIsNotNullable_ProducesDiagnostic()
   {
      var result = GeneratorHarness.RunGenerator(RelationSource("""
         [Relation(nameof(AuthorId))]
            public AuthorTable Author { get; set; } = new();
         """));

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0015");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationOnAnUnsupportedPropertyType_ProducesDiagnostic()
   {
      var result = GeneratorHarness.RunGenerator(RelationSource("""
         [Relation(nameof(AuthorId))]
            public System.Collections.Generic.HashSet<AuthorTable> Authors { get; set; } = new();
         """));

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0016");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   [Fact]
   public void RelationOnAnUnsupportedPropertyShape_ProducesDiagnostic()
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

            [Relation(nameof(AuthorId))]
            public AuthorTable? Author { get; private set; }
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
      var result = GeneratorHarness.RunGenerator(RelationSource("""
         [Relation(nameof(AuthorId))]
            [Column("author")]
            public AuthorTable? Author { get; set; }
         """));

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0018");
      result.GeneratedSources.Should().NotBeEmpty();
   }

   /// <summary>
   ///    A book table carrying whichever relation member the caller spells out, plus the author table it points at.
   /// </summary>
   private static string RelationSource(string member)
   {
      return $$"""
         using mvdmio.Database.PgSQL.Attributes;

         namespace Demo;

         [Table("public.books")]
         public partial class BookTable
         {
            [PrimaryKey]
            public long BookId { get; set; }

            public string Title { get; set; } = string.Empty;
            public long? AuthorId { get; set; }

            {{member}}
         }

         [Table("public.authors")]
         public partial class AuthorTable
         {
            [PrimaryKey]
            public long AuthorId { get; set; }

            public string Name { get; set; } = string.Empty;
         }
         """;
   }
}
