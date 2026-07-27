using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

public class TableRepositoryGeneratorTests
{
   private const string _RUNTIME_STUBS = """
      namespace mvdmio.Database.PgSQL.Attributes
      {
         [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
         public sealed class TableAttribute : System.Attribute
         {
            public TableAttribute(string name) { }
         }

         [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
         public sealed class PrimaryKeyAttribute : System.Attribute { }

         [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
         public sealed class UniqueAttribute : System.Attribute { }

         [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
         public sealed class ColumnAttribute : System.Attribute
         {
            public ColumnAttribute(string name) { }
         }

         [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
         public sealed class GeneratedAttribute : System.Attribute { }
      }

      namespace mvdmio.Database.PgSQL
      {
         public class DatabaseConnection
         {
            public Connectors.DapperDatabaseConnector Dapper { get; } = new Connectors.DapperDatabaseConnector();
            public Connectors.Linq.LinqDatabaseConnector Linq { get; } = new Connectors.Linq.LinqDatabaseConnector();
         }

         public static class ServiceCollectionExtensions
         {
            public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddDatabase(this Microsoft.Extensions.DependencyInjection.IServiceCollection services) => services;
         }
      }

      namespace mvdmio.Database.PgSQL.Connectors
      {
         public sealed class DapperDatabaseConnector
         {
            public System.Threading.Tasks.Task<T> QuerySingleAsync<T>(string sql, System.Collections.Generic.IDictionary<string, object?>? parameters = null, System.TimeSpan? commandTimeout = null, System.Threading.CancellationToken ct = default) => throw null!;
            public System.Threading.Tasks.Task<T?> QuerySingleOrDefaultAsync<T>(string sql, System.Collections.Generic.IDictionary<string, object?>? parameters = null, System.TimeSpan? commandTimeout = null, System.Threading.CancellationToken ct = default) => throw null!;
            public System.Threading.Tasks.Task<System.Collections.Generic.IEnumerable<T>> QueryAsync<T>(string sql, System.Collections.Generic.IDictionary<string, object?>? parameters = null, System.TimeSpan? commandTimeout = null, System.Threading.CancellationToken ct = default) => throw null!;
            public System.Threading.Tasks.Task<int> ExecuteAsync(string sql, System.Collections.Generic.IDictionary<string, object?>? parameters = null, System.TimeSpan? commandTimeout = null, System.Threading.CancellationToken ct = default) => throw null!;
         }
      }

      namespace mvdmio.Database.PgSQL.Connectors.Linq
      {
         public sealed class LinqDatabaseConnector
         {
            public System.Linq.IQueryable<TEntity> Query<TEntity>(System.TimeSpan? commandTimeout = null) where TEntity : class => throw null!;
         }

         public sealed class QueryEntityMappingBuilder<TEntity>
            where TEntity : class
         {
            public QueryEntityMappingBuilder<TEntity> Column<TProperty>(System.Linq.Expressions.Expression<System.Func<TEntity, TProperty>> property, string columnName, bool isPrimaryKey = false) => throw null!;
         }

         public static class QueryMappings
         {
            public static void Register<TEntity>(string schemaName, string tableName, System.Action<QueryEntityMappingBuilder<TEntity>> configure) where TEntity : class { }
         }
      }
      """;

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

      var result = RunGenerator(source);

      result.Diagnostics.Should().BeEmpty();
      result.GeneratedSources.Should().HaveCount(2);

      var generatedSource = result.GeneratedSources.Single(x => x.HintName.EndsWith("Repository.g.cs", StringComparison.Ordinal)).SourceText.ToString();
      var registrationSource = result.GeneratedSources.Single(x => x.HintName == "GeneratedAssemblyRegistration.g.cs").SourceText.ToString();
      generatedSource.Should().Contain("public partial class UserData");
      generatedSource.Should().Contain("public partial class CreateUserCommand");
      generatedSource.Should().Contain("public partial class UpdateUserCommand");
      generatedSource.Should().Contain("public partial interface IUserRepository");
      generatedSource.Should().Contain("public partial class UserRepository");
      generatedSource.Should().Contain("public partial class UserRepository : IUserRepository");
      registrationSource.Should().Contain("namespace GeneratorTests;");
      registrationSource.Should().Contain("AddGeneratorTests(this IServiceCollection services)");
      registrationSource.Should().Contain("services.TryAddScoped<global::Demo.IUserRepository, global::Demo.UserRepository>();");
      generatedSource.Should().Contain("GetByUserIdAsync");
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

      var result = RunGenerator(source);

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

      var result = RunGenerator(source);

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

      var result = RunGenerator(source);

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

      var result = RunGenerator(source);

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

      var result = RunGenerator(source);

      result.Diagnostics.Should().ContainSingle(x => x.Id == "PGSQL0010");
      result.GeneratedSources.Should().BeEmpty();
   }

   private static GeneratorRunResult RunGenerator(string source)
   {
      var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
      var syntaxTrees = new[]
      {
         CSharpSyntaxTree.ParseText(SourceText.From(source), parseOptions),
         CSharpSyntaxTree.ParseText(SourceText.From(_RUNTIME_STUBS), parseOptions)
      };

      var compilation = CSharpCompilation.Create(
         assemblyName: "GeneratorTests",
         syntaxTrees: syntaxTrees,
         references: GetMetadataReferences(),
         options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
      );

      GeneratorDriver driver = CSharpGeneratorDriver.Create(new TableRepositoryGenerator());
      driver = driver.RunGenerators(compilation);
      return driver.GetRunResult().Results.Single();
   }

   private static IEnumerable<MetadataReference> GetMetadataReferences()
   {
      var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
      return trustedAssemblies.Select(path => MetadataReference.CreateFromFile(path));
   }
}
