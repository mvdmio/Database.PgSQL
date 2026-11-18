namespace mvdmio.Database.PgSQL.Tests.Packaging.Fixture;

internal static class ConsumerProject
{
   public const string ASSEMBLY_NAME = "PackagingConsumer";

   public static readonly string[] TargetFrameworks = ["net10.0", "net11.0"];

   public static string Csproj(string packageVersion)
   {
      return $"""
         <Project Sdk="Microsoft.NET.Sdk">
           <PropertyGroup>
             <OutputType>Exe</OutputType>
             <TargetFrameworks>{string.Join(";", TargetFrameworks)}</TargetFrameworks>
             <ImplicitUsings>enable</ImplicitUsings>
             <Nullable>enable</Nullable>
             <LangVersion>latest</LangVersion>
             <AssemblyName>{ASSEMBLY_NAME}</AssemblyName>
             <RootNamespace>{ASSEMBLY_NAME}</RootNamespace>
             <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
           </PropertyGroup>
           <ItemGroup>
             <PackageReference Include="mvdmio.Database.PgSQL" Version="{packageVersion}" />
           </ItemGroup>
         </Project>
         """;
   }

   public static string NuGetConfig(string feedDirectory)
   {
      return $"""
         <?xml version="1.0" encoding="utf-8"?>
         <configuration>
           <packageSources>
             <clear />
             <add key="packaging-test" value="{feedDirectory}" />
             <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
           </packageSources>
         </configuration>
         """;
   }

   public const string PROGRAM = """"
      using LinqToDB;
      using mvdmio.Database.PgSQL;
      using mvdmio.Database.PgSQL.Attributes;
      using NpgsqlTypes;

      namespace PackagingConsumer;

      public enum ConsumerState
      {
         Open,
         Closed
      }

      // Copied from the library README's "Generated Repositories" walkthrough, so the documented sample is a thing that
      // compiles against the published package rather than a thing a reader has to test.
      [Table("public.users")]
      public partial class UserTable
      {
         [PrimaryKey]
         [Generated]
         public long UserId { get; set; }

         [Unique]
         public string UserName { get; set; } = string.Empty;

         [Column("first_name")]
         public string FirstName { get; set; } = string.Empty;

         public DateTimeOffset? LastLoginAt { get; set; }
      }

      [Table("public.packaging_consumer")]
      public partial class ConsumerTable
      {
         [PrimaryKey]
         [Generated]
         public long ConsumerId { get; private set; }

         [Generated]
         public DateTime CreatedAt { get; private set; }

         public ConsumerState State { get; set; }

         [Column(StoredAs = NpgsqlDbType.Jsonb)]
         public required string Document { get; init; }
      }

      public static class Program
      {
         public static async Task<int> Main(string[] args)
         {
            await using var db = new DatabaseConnection(args[0]);

            await db.Dapper.ExecuteAsync(
               """
               CREATE TABLE IF NOT EXISTS public.packaging_consumer (
                  consumer_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  created_at  TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                  state       TEXT NOT NULL,
                  document    JSONB NOT NULL
               )
               """
            );

            var repository = new ConsumerRepository(db);

            var created = await repository.CreateAsync(
               new CreateConsumerCommand { State = ConsumerState.Closed, Document = """{"kind": "invoice"}""" }
            );

            var storedState = await db.Dapper.QuerySingleAsync<string>(
               "SELECT state FROM public.packaging_consumer WHERE consumer_id = :consumerId",
               new Dictionary<string, object?> { ["consumerId"] = created.ConsumerId }
            );

            var matched = repository.Query().Where(x => x.State == ConsumerState.Closed).ToList();

            Console.WriteLine($"readmeSample={await RunTheReadmeSampleAsync(db)}");
            Console.WriteLine($"generatedKey={created.ConsumerId > 0}");
            Console.WriteLine($"generatedTimestamp={created.CreatedAt != default}");
            Console.WriteLine($"storedState={storedState}");
            Console.WriteLine($"document={created.Document}");
            Console.WriteLine($"queryMatched={matched.Count(x => x.ConsumerId == created.ConsumerId)}");

            return 0;
         }

         /// The README's own snippets, run rather than merely compiled.
         private static async Task<bool> RunTheReadmeSampleAsync(DatabaseConnection db)
         {
            await db.Dapper.ExecuteAsync(
               """
               CREATE TABLE IF NOT EXISTS public.users (
                  user_id       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  user_name     TEXT NOT NULL UNIQUE,
                  first_name    TEXT NOT NULL,
                  last_login_at TIMESTAMPTZ NULL
               )
               """
            );

            var repository = new UserRepository(db);

            var created = await repository.CreateAsync(new CreateUserCommand { UserName = "alice", FirstName = "Alice" });

            var all = await repository.GetAllAsync();
            var byId = await repository.GetByPrimaryKeyAsync(created.UserId);
            var byName = await repository.GetByUserNameAsync("alice");

            var updated = await repository.UpdateAsync(new UpdateUserCommand {
               UserId = created.UserId,
               UserName = "alice",
               FirstName = "Alicia",
               LastLoginAt = DateTimeOffset.UtcNow
            });

            var page = await repository.Query()
               .Where(x => x.UserName == "alice")
               .OrderBy(x => x.UserName)
               .Skip(0)
               .Take(20)
               .ToListAsync();

            var deleted = await repository.DeleteByPrimaryKeyAsync(created.UserId);

            return all.Any()
               && byId?.UserId == created.UserId
               && byName?.FirstName == "Alice"
               && updated.FirstName == "Alicia"
               && page.Count == 1
               && deleted;
         }
      }
      """";
}
