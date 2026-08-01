using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

/// <summary>
///    Runs the table repository generator over an in-memory compilation, which is the seam every generator test uses:
///    what a consumer observes is the diagnostics reported and the source emitted.
/// </summary>
/// <remarks>
///    The runtime stubs mirror the shipped surface the generated code binds against rather than referencing the library,
///    so a generator test needs no package restore and states exactly which overloads generated code is allowed to
///    resolve to. Keeping them here means the two test classes cannot disagree about what that surface is.
/// </remarks>
internal static class GeneratorHarness
{
   private static readonly CSharpParseOptions _parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

   public const string RUNTIME_STUBS = """
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
            public ColumnAttribute() { }
            public ColumnAttribute(string name) { }

            public bool Null { get; set; }
            public bool NotNull { get; set; }
            public NpgsqlTypes.NpgsqlDbType StoredAs { get; set; }
            public bool Tenancy { get; set; }
         }

         [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
         public sealed class GeneratedAttribute : System.Attribute { }

         [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
         public sealed class RelationAttribute : System.Attribute
         {
            public RelationAttribute(params string[] foreignKeyPropertyNames) { }
         }
      }

      namespace mvdmio.Database.PgSQL.Relations
      {
         public abstract class RelationDefinition<TDeclaring, TTarget>
            where TDeclaring : class
            where TTarget : class
         {
            public abstract System.Collections.Generic.IReadOnlyList<RelationKey> Keys { get; }

            protected static RelationKey Key<TValue>(
               System.Linq.Expressions.Expression<System.Func<TDeclaring, TValue>> declaringProperty,
               System.Linq.Expressions.Expression<System.Func<TTarget, TValue>> targetProperty
            ) => new RelationKey();

            protected static RelationKey Key<TValue>(
               System.Linq.Expressions.Expression<System.Func<TDeclaring, TValue?>> declaringProperty,
               System.Linq.Expressions.Expression<System.Func<TTarget, TValue>> targetProperty
            ) where TValue : struct => new RelationKey();
         }

         public sealed class RelationKey
         {
            internal RelationKey() { }
         }
      }

      namespace NpgsqlTypes
      {
         // A subset, with the driver's own values, because the generator resolves a claimed member's name from the
         // constant the attribute carries. Only members a test claims have to be here.
         public enum NpgsqlDbType
         {
            Bigint = 1,
            Integer = 9,
            Smallint = 18,
            Text = 19,
            Json = 35,
            Jsonb = 36,
            Inet = 24,
            Uuid = 27
         }
      }

      namespace Microsoft.Extensions.DependencyInjection
      {
         public interface IServiceCollection { }
      }

      namespace Microsoft.Extensions.DependencyInjection.Extensions
      {
         public static class ServiceCollectionDescriptorExtensions
         {
            public static void TryAddScoped<TService, TImplementation>(this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
               where TService : class
               where TImplementation : class, TService { }
         }
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

      namespace mvdmio.Database.PgSQL.Dapper.QueryParameters
      {
         public sealed class TypedQueryParameter
         {
            public TypedQueryParameter(object? value, NpgsqlTypes.NpgsqlDbType dbType) { }
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
            public QueryEntityMappingBuilder<TEntity> Column<TProperty>(System.Linq.Expressions.Expression<System.Func<TEntity, TProperty>> property, string columnName, bool isPrimaryKey = false, bool isNotNull = false) => throw null!;

            public QueryEntityMappingBuilder<TEntity> Column<TProperty>(System.Linq.Expressions.Expression<System.Func<TEntity, TProperty>> property, string columnName, NpgsqlTypes.NpgsqlDbType storedAs, bool isPrimaryKey = false, bool isNotNull = false) => throw null!;

            public QueryEntityMappingBuilder<TEntity> Column<TProperty, TStored>(
               System.Linq.Expressions.Expression<System.Func<TEntity, TProperty>> property,
               string columnName,
               NpgsqlTypes.NpgsqlDbType storedAs,
               System.Linq.Expressions.Expression<System.Func<TProperty, TStored>> toStored,
               System.Linq.Expressions.Expression<System.Func<TStored, TProperty>> fromStored,
               bool isPrimaryKey = false,
               bool isNotNull = false
            ) => throw null!;

            public QueryEntityMappingBuilder<TEntity> Relation<TTarget, TThisKey, TTargetKey>(
               System.Linq.Expressions.Expression<System.Func<TEntity, TTarget?>> property,
               System.Linq.Expressions.Expression<System.Func<TEntity, TThisKey>> thisKey,
               System.Linq.Expressions.Expression<System.Func<TTarget, TTargetKey>> targetKey
            ) where TTarget : class => throw null!;

            public QueryEntityMappingBuilder<TEntity> Relation<TTarget, TThisKey, TTargetKey>(
               System.Linq.Expressions.Expression<System.Func<TEntity, System.Collections.Generic.IEnumerable<TTarget>>> property,
               System.Linq.Expressions.Expression<System.Func<TEntity, TThisKey>> thisKey,
               System.Linq.Expressions.Expression<System.Func<TTarget, TTargetKey>> targetKey
            ) where TTarget : class => throw null!;

            public QueryEntityMappingBuilder<TEntity> Relation<TTarget>(
               System.Linq.Expressions.Expression<System.Func<TEntity, TTarget?>> property,
               System.Linq.Expressions.Expression<System.Func<TEntity, TTarget, bool>> predicate
            ) where TTarget : class => throw null!;

            public QueryEntityMappingBuilder<TEntity> Relation<TTarget>(
               System.Linq.Expressions.Expression<System.Func<TEntity, System.Collections.Generic.IEnumerable<TTarget>>> property,
               System.Linq.Expressions.Expression<System.Func<TEntity, TTarget, bool>> predicate
            ) where TTarget : class => throw null!;
         }

         public static class QueryMappings
         {
            public static void Register<TEntity>(string schemaName, string tableName, System.Action<QueryEntityMappingBuilder<TEntity>> configure) where TEntity : class { }
         }
      }
      """;

   /// <summary>Runs the generator and returns everything it reported and emitted.</summary>
   /// <param name="source">The table definitions to run over.</param>
   /// <param name="nullableContextOptions">
   ///    How the compilation treats nullable reference types. Only a test about the nullable-oblivious case passes
   ///    anything but the default, because that case is the one where a reference type's annotation cannot be read.
   /// </param>
   public static GeneratorRunResult RunGenerator(string source, NullableContextOptions nullableContextOptions = NullableContextOptions.Enable)
   {
      return CreateDriver().RunGenerators(CreateCompilation(source, nullableContextOptions)).GetRunResult().Results.Single();
   }

   /// <summary>The source emitted under the given hint name.</summary>
   public static string GeneratedSource(GeneratorRunResult result, string hintName)
   {
      return result.GeneratedSources.Single(x => x.HintName == hintName).SourceText.ToString();
   }

   /// <summary>The source emitted for the repository of the only table definition in the compilation.</summary>
   public static string RepositorySource(GeneratorRunResult result)
   {
      return result.GeneratedSources.Single(x => x.HintName.EndsWith("Repository.g.cs", StringComparison.Ordinal)).SourceText.ToString();
   }

   /// <summary>The assembly-wide registration source, which carries the query mappings and the relations.</summary>
   public static string RegistrationSource(GeneratorRunResult result)
   {
      return GeneratedSource(result, "GeneratedAssemblyRegistration.g.cs");
   }

   /// <summary>
   ///    Compiles the generator's output alongside the source that produced it, which is the only thing that proves the
   ///    emitted mapping calls resolve against the overloads the library actually ships.
   /// </summary>
   public static void AssertGeneratedSourcesCompile(string source)
   {
      CreateDriver().RunGeneratorsAndUpdateCompilation(CreateCompilation(source, NullableContextOptions.Enable), out var updated, out _);

      var errors = updated.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).ToList();

      errors.Should().BeEmpty(string.Join(Environment.NewLine, errors.Select(x => x.ToString())));
   }

   /// <remarks>
   ///    The driver parses the sources it adds itself, and refuses to add them to a compilation parsed at another
   ///    language version, so it is handed the same options.
   /// </remarks>
   private static GeneratorDriver CreateDriver()
   {
      return CSharpGeneratorDriver.Create(
         generators: [new TableRepositoryGenerator().AsSourceGenerator()],
         additionalTexts: null,
         parseOptions: _parseOptions,
         optionsProvider: null
      );
   }

   /// <remarks>
   ///    Nullable reference types default to on, the way a consumer's project has them: a relation to one row states its
   ///    cardinality partly through nullability, which cannot be read at all in a nullable-oblivious compilation. The
   ///    stubs are parsed under whatever the test asks for too, which costs only warnings inside them.
   /// </remarks>
   private static CSharpCompilation CreateCompilation(string source, NullableContextOptions nullableContextOptions)
   {
      var syntaxTrees = new[]
      {
         CSharpSyntaxTree.ParseText(SourceText.From(source), _parseOptions),
         CSharpSyntaxTree.ParseText(SourceText.From(RUNTIME_STUBS), _parseOptions)
      };

      return CSharpCompilation.Create(
         assemblyName: "GeneratorTests",
         syntaxTrees: syntaxTrees,
         references: GetMetadataReferences(),
         options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(nullableContextOptions)
      );
   }

   private static IEnumerable<MetadataReference> GetMetadataReferences()
   {
      var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
      return trustedAssemblies.Select(path => MetadataReference.CreateFromFile(path));
   }
}
