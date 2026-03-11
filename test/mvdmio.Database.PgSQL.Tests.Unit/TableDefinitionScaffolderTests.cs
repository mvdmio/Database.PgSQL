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
}
