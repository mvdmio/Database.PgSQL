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
