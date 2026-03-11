using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Schema.Models;
using mvdmio.Database.PgSQL.Tool.Scaffolding;

namespace mvdmio.Database.PgSQL.Tests.Unit;

public class TableDefinitionScaffolderTests
{
   [Fact]
   public void Generate_WithSinglePrimaryKeyAndUniqueColumn_CreatesRepositoryReadyTableClass()
   {
      var result = TableDefinitionScaffolder.Generate(
         "Demo.Tables",
         [
            new TableInfo
            {
               Schema = "public",
               Name = "users",
               Columns =
               [
                  new ColumnInfo { Name = "user_id", DataType = "bigint", IsNullable = false, IsIdentity = true, IdentityGeneration = "ALWAYS" },
                  new ColumnInfo { Name = "user_name", DataType = "text", IsNullable = false, IsIdentity = false },
                  new ColumnInfo { Name = "firstName", DataType = "text", IsNullable = false, IsIdentity = false }
               ]
            }
         ],
         [
            new ConstraintInfo { Schema = "public", TableName = "users", ConstraintName = "pk_users", ConstraintType = "p", Definition = "PRIMARY KEY (user_id)" },
            new ConstraintInfo { Schema = "public", TableName = "users", ConstraintName = "uq_users_user_name", ConstraintType = "u", Definition = "UNIQUE (user_name)" }
         ]
      );

      result.Warnings.Should().BeEmpty();
      result.Files.Should().ContainSingle();
      result.Files[0].FileName.Should().Be("UsersTable.cs");
      result.Files[0].Content.Should().Contain("namespace Demo.Tables;");
      result.Files[0].Content.Should().Contain("[Table(\"public.users\")]");
      result.Files[0].Content.Should().Contain("[PrimaryKey]");
      result.Files[0].Content.Should().Contain("[Unique]");
      result.Files[0].Content.Should().Contain("[Generated]");
      result.Files[0].Content.Should().Contain("[Column(\"firstName\")]");
      result.Files[0].Content.Should().Contain("public long UserId { get; set; }");
      result.Files[0].Content.Should().Contain("public string UserName { get; set; } = string.Empty;");
   }

   [Fact]
   public void Generate_WithoutSinglePrimaryKey_AddsWarningAndSkipsTableAttribute()
   {
      var result = TableDefinitionScaffolder.Generate(
         "Demo.Tables",
         [
            new TableInfo
            {
               Schema = "audit",
               Name = "entries",
               Columns =
               [
                  new ColumnInfo { Name = "entry_id", DataType = "uuid", IsNullable = false, IsIdentity = false },
                  new ColumnInfo { Name = "message", DataType = "text", IsNullable = false, IsIdentity = false }
               ]
            }
         ],
         []
      );

      result.Warnings.Should().ContainSingle();
      result.Files[0].Content.Should().NotContain("[Table(");
      result.Files[0].Content.Should().Contain("Repository generation is skipped");
   }

   [Fact]
   public void Generate_WithDuplicateTableNamesAcrossSchemas_PrefixesClassNamesToKeepThemUnique()
   {
      var result = TableDefinitionScaffolder.Generate(
         "Demo.Tables",
         [
            new TableInfo
            {
               Schema = "public",
               Name = "users",
               Columns = [new ColumnInfo { Name = "id", DataType = "bigint", IsNullable = false, IsIdentity = false }]
            },
            new TableInfo
            {
               Schema = "audit",
               Name = "users",
               Columns = [new ColumnInfo { Name = "id", DataType = "bigint", IsNullable = false, IsIdentity = false }]
            }
         ],
         []
      );

      result.Files.Should().HaveCount(2);
      result.Files.Select(x => x.FileName).Should().BeEquivalentTo(["PublicUsersTable.cs", "AuditUsersTable.cs"]);
   }

   [Fact]
   public void Generate_WithNullableArrayColumn_GeneratesNullableArrayPropertyWithoutInitializer()
   {
      var result = TableDefinitionScaffolder.Generate(
         "Demo.Tables",
         [
            new TableInfo
            {
               Schema = "public",
               Name = "attachments",
               Columns =
               [
                  new ColumnInfo { Name = "file_ids", DataType = "uuid[]", IsNullable = true, IsIdentity = false }
               ]
            }
         ],
         []
      );

      result.Files.Should().ContainSingle();
      result.Files[0].Content.Should().Contain("public Guid[]? FileIds { get; set; }");
      result.Files[0].Content.Should().NotContain("public Guid[]? FileIds { get; set; } =");
   }

   [Fact]
   public void Generate_WithQuotedConstraintIdentifiers_RecognizesPrimaryAndUniqueColumns()
   {
      var result = TableDefinitionScaffolder.Generate(
         "Demo.Tables",
         [
            new TableInfo
            {
               Schema = "public",
               Name = "users",
               Columns =
               [
                  new ColumnInfo { Name = "user_id", DataType = "uuid", IsNullable = false, IsIdentity = false },
                  new ColumnInfo { Name = "display_name", DataType = "text", IsNullable = false, IsIdentity = false }
               ]
            }
         ],
         [
            new ConstraintInfo { Schema = "public", TableName = "users", ConstraintName = "pk_users", ConstraintType = "p", Definition = "PRIMARY KEY (\"user_id\")" },
            new ConstraintInfo { Schema = "public", TableName = "users", ConstraintName = "uq_users_display_name", ConstraintType = "u", Definition = "UNIQUE (\"display_name\")" }
         ]
      );

      result.Warnings.Should().BeEmpty();
      result.Files[0].Content.Should().Contain("[PrimaryKey]");
      result.Files[0].Content.Should().Contain("[Unique]");
      result.Files[0].Content.Should().NotContain("[Column(\"user_id\")]");
      result.Files[0].Content.Should().NotContain("[Column(\"display_name\")]");
      result.Files[0].Content.Should().Contain("public Guid UserId { get; set; }");
      result.Files[0].Content.Should().Contain("public string DisplayName { get; set; } = string.Empty;");
   }

   [Fact]
   public void Generate_WithVariousDatabaseTypes_MapsClrTypesAndInitializers()
   {
      var result = TableDefinitionScaffolder.Generate(
         "Demo.Tables",
         [
            new TableInfo
            {
               Schema = "public",
               Name = "type_samples",
               Columns =
               [
                  new ColumnInfo { Name = "amount", DataType = "numeric(10,2)", IsNullable = false, IsIdentity = false },
                  new ColumnInfo { Name = "is_active", DataType = "boolean", IsNullable = false, IsIdentity = false },
                  new ColumnInfo { Name = "event_date", DataType = "date", IsNullable = false, IsIdentity = false },
                  new ColumnInfo { Name = "created_at", DataType = "timestamp without time zone", IsNullable = false, IsIdentity = false },
                  new ColumnInfo { Name = "alarm_time", DataType = "time without time zone", IsNullable = true, IsIdentity = false },
                  new ColumnInfo { Name = "published_at", DataType = "time with time zone", IsNullable = false, IsIdentity = false },
                  new ColumnInfo { Name = "duration", DataType = "interval", IsNullable = false, IsIdentity = false },
                  new ColumnInfo { Name = "payload", DataType = "bytea", IsNullable = false, IsIdentity = false },
                  new ColumnInfo { Name = "tags", DataType = "text[]", IsNullable = false, IsIdentity = false },
                  new ColumnInfo { Name = "score", DataType = "real", IsNullable = false, IsIdentity = false },
                  new ColumnInfo { Name = "ratio", DataType = "double precision", IsNullable = false, IsIdentity = false }
               ]
            }
         ],
         []
      );

      result.Files[0].Content.Should().Contain("public decimal Amount { get; set; }");
      result.Files[0].Content.Should().Contain("public bool IsActive { get; set; }");
      result.Files[0].Content.Should().Contain("public DateOnly EventDate { get; set; }");
      result.Files[0].Content.Should().Contain("public DateTime CreatedAt { get; set; }");
      result.Files[0].Content.Should().Contain("public TimeOnly? AlarmTime { get; set; }");
      result.Files[0].Content.Should().Contain("public DateTimeOffset PublishedAt { get; set; }");
      result.Files[0].Content.Should().Contain("public TimeSpan Duration { get; set; }");
      result.Files[0].Content.Should().Contain("public byte[] Payload { get; set; } = Array.Empty<byte>();");
      result.Files[0].Content.Should().Contain("public string[] Tags { get; set; } = default!;");
      result.Files[0].Content.Should().Contain("public float Score { get; set; }");
      result.Files[0].Content.Should().Contain("public double Ratio { get; set; }");
   }
}
