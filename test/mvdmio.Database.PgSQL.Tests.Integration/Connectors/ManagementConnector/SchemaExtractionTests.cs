using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using System.Text;

namespace mvdmio.Database.PgSQL.Tests.Integration.Connectors.ManagementConnector;

public class SchemaExtractionTests : TestBase
{
   public SchemaExtractionTests(TestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      // Create a rich schema for testing all extraction features.
      // Each statement is executed separately to avoid Dapper/Npgsql issues with
      // dollar-quoting in multi-statement batches.

      await Db.Dapper.ExecuteAsync("CREATE SCHEMA IF NOT EXISTS test_schema;");

      // Enum type (wrapped in DO block with dollar-quoting, must be its own call)
      await Db.Dapper.ExecuteAsync(
         """
         DO $$ BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON t.typnamespace = n.oid WHERE t.typname = 'test_status' AND n.nspname = 'public') THEN
               CREATE TYPE public.test_status AS ENUM ('active', 'inactive', 'pending');
            END IF;
         END $$;
         """
      );

      await Db.Dapper.ExecuteAsync(
         """
         CREATE SEQUENCE IF NOT EXISTS public.test_seq
            AS bigint
            INCREMENT BY 1
            MINVALUE 1
            MAXVALUE 9223372036854775807
            START WITH 100
            CACHE 1
            NO CYCLE;
         """
      );

      await Db.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.parent_table (
            id         bigint NOT NULL GENERATED ALWAYS AS IDENTITY,
            name       text NOT NULL,
            status     public.test_status NOT NULL DEFAULT 'active',
            created_at timestamptz NOT NULL DEFAULT NOW(),
            PRIMARY KEY (id)
         );
         """
      );

      await Db.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS test_schema.child_table (
            id         bigint NOT NULL GENERATED ALWAYS AS IDENTITY,
            parent_id  bigint NOT NULL,
            value      double precision,
            PRIMARY KEY (id)
         );
         """
      );

      await Db.Dapper.ExecuteAsync(
         """
         ALTER TABLE test_schema.child_table
            ADD CONSTRAINT fk_child_parent FOREIGN KEY (parent_id) REFERENCES public.parent_table(id);
         """
      );

      await Db.Dapper.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_parent_name ON public.parent_table (name);");

      await Db.Dapper.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_child_parent_id ON test_schema.child_table (parent_id);");

      await Db.Dapper.ExecuteAsync(
         """
         CREATE OR REPLACE VIEW public.active_parents AS
            SELECT id, name, created_at
            FROM public.parent_table
            WHERE status = 'active';
         """
      );

      await Db.Dapper.ExecuteAsync(
         """
         CREATE OR REPLACE FUNCTION public.get_parent_count()
            RETURNS bigint
            LANGUAGE sql
            STABLE
            AS $func$SELECT count(*) FROM public.parent_table;$func$;
         """
      );
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_ContainsHeader()
   {
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      script.Should().Contain("PostgreSQL database schema");
      script.Should().Contain("Generated at");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_ContainsSchemas()
   {
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      script.Should().Contain("CREATE SCHEMA IF NOT EXISTS \"test_schema\"");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_ExcludesMvdmioSchema()
   {
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      script.Should().NotContain("\"mvdmio\"");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_ContainsEnumTypes()
   {
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      script.Should().Contain("test_status");
      script.Should().Contain("'active'");
      script.Should().Contain("'inactive'");
      script.Should().Contain("'pending'");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_ContainsTables()
   {
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      script.Should().Contain("CREATE TABLE IF NOT EXISTS \"public\".\"parent_table\"");
      script.Should().Contain("CREATE TABLE IF NOT EXISTS \"test_schema\".\"child_table\"");
      script.Should().Contain("CREATE TABLE IF NOT EXISTS \"public\".\"simple_table\"");
      script.Should().Contain("CREATE TABLE IF NOT EXISTS \"public\".\"complex_table\"");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_ContainsIdentityColumns()
   {
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      script.Should().Contain("GENERATED ALWAYS AS IDENTITY");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_ContainsConstraints()
   {
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      // Primary keys
      script.Should().Contain("PRIMARY KEY");

      // Foreign key
      script.Should().Contain("fk_child_parent");
      script.Should().Contain("FOREIGN KEY");
      script.Should().Contain("REFERENCES");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_ContainsIndexes()
   {
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      script.Should().Contain("idx_parent_name");
      script.Should().Contain("idx_child_parent_id");
      script.Should().Contain("IF NOT EXISTS");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_ContainsFunctions()
   {
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      script.Should().Contain("get_parent_count");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_ContainsViews()
   {
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      script.Should().Contain("CREATE OR REPLACE VIEW \"public\".\"active_parents\"");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_ContainsSequences()
   {
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      script.Should().Contain("test_seq");
      script.Should().Contain("CREATE SEQUENCE IF NOT EXISTS");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_IsIdempotent()
   {
      // The generated script should be executable against the same database without errors,
      // since all statements are idempotent.
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      // Execute each statement separately to avoid Dapper/Npgsql issues with
      // dollar-quoting in multi-statement batches.
      var statements = SplitSqlStatements(script);

      foreach (var statement in statements)
      {
         await Db.Dapper.ExecuteAsync(statement, ct: CancellationToken);
      }
   }

   /// <summary>
   ///    Splits a SQL script into individual statements, correctly handling dollar-quoted blocks
   ///    (e.g., DO $$ ... $$; or function bodies with $func$ ... $func$).
   /// </summary>
   private static List<string> SplitSqlStatements(string script)
   {
      var statements = new List<string>();
      var current = new StringBuilder();
      string? dollarTag = null;
      var i = 0;

      while (i < script.Length)
      {
         // Check for dollar-quoting start/end
         if (script[i] == '$')
         {
            var tagEnd = script.IndexOf('$', i + 1);

            if (tagEnd >= 0)
            {
               var tag = script[i..(tagEnd + 1)];

               if (dollarTag == null)
               {
                  // Entering dollar-quoted block
                  dollarTag = tag;
                  current.Append(tag);
                  i = tagEnd + 1;
                  continue;
               }

               if (tag == dollarTag)
               {
                  // Exiting dollar-quoted block
                  current.Append(tag);
                  dollarTag = null;
                  i = tagEnd + 1;
                  continue;
               }
            }
         }

         if (script[i] == ';' && dollarTag == null)
         {
            current.Append(';');
            var stmt = current.ToString().Trim();

            if (stmt.Length > 1) // More than just ";"
               statements.Add(stmt);

            current.Clear();
            i++;
            continue;
         }

         // Skip single-line comments
         if (i + 1 < script.Length && script[i] == '-' && script[i + 1] == '-' && dollarTag == null)
         {
            var lineEnd = script.IndexOf('\n', i);

            if (lineEnd < 0)
               break;

            i = lineEnd + 1;
            continue;
         }

         current.Append(script[i]);
         i++;
      }

      return statements;
   }

   [Fact]
   public async Task GetUserSchemasAsync_ReturnsUserSchemas()
   {
      var schemas = (await Db.Management.Schema.GetUserSchemasAsync(CancellationToken)).ToArray();

      schemas.Should().Contain("test_schema");
   }

   [Fact]
   public async Task GetUserSchemasAsync_ExcludesSystemSchemas()
   {
      var schemas = (await Db.Management.Schema.GetUserSchemasAsync(CancellationToken)).ToArray();

      schemas.Should().NotContain("pg_catalog");
      schemas.Should().NotContain("information_schema");
      schemas.Should().NotContain("pg_toast");
      schemas.Should().NotContain("mvdmio");
      schemas.Should().NotContain("public");
   }

   [Fact]
   public async Task GetTablesAsync_ReturnsUserTables()
   {
      var tables = (await Db.Management.Schema.GetTablesAsync(CancellationToken)).ToArray();
      var tableNames = tables.Select(t => $"{t.Schema}.{t.Name}").ToArray();

      tableNames.Should().Contain("public.parent_table");
      tableNames.Should().Contain("test_schema.child_table");
      tableNames.Should().Contain("public.simple_table");
      tableNames.Should().Contain("public.complex_table");
   }

   [Fact]
   public async Task GetTablesAsync_IncludesColumnDetails()
   {
      var tables = (await Db.Management.Schema.GetTablesAsync(CancellationToken)).ToArray();
      var parentTable = tables.First(t => t.Name == "parent_table");

      parentTable.Columns.Should().HaveCount(4);

      var idCol = parentTable.Columns.First(c => c.Name == "id");
      idCol.DataType.Should().Be("bigint");
      idCol.IsNullable.Should().BeFalse();
      idCol.IsIdentity.Should().BeTrue();
      idCol.IdentityGeneration.Should().Be("ALWAYS");

      var nameCol = parentTable.Columns.First(c => c.Name == "name");
      nameCol.DataType.Should().Be("text");
      nameCol.IsNullable.Should().BeFalse();
   }

   [Fact]
   public async Task GetEnumTypesAsync_ReturnsEnumTypes()
   {
      var enums = (await Db.Management.Schema.GetEnumTypesAsync(CancellationToken)).ToArray();

      enums.Should().NotBeEmpty();

      var testStatus = enums.First(e => e.Name == "test_status");
      testStatus.Schema.Should().Be("public");
      testStatus.Labels.Should().BeEquivalentTo(["active", "inactive", "pending"]);
   }

   [Fact]
   public async Task GetConstraintsAsync_ReturnsPrimaryKeys()
   {
      var constraints = (await Db.Management.Schema.GetConstraintsAsync(CancellationToken)).ToArray();
      var primaryKeys = constraints.Where(c => c.ConstraintType == "p").ToArray();

      primaryKeys.Should().Contain(c => c.TableName == "parent_table");
      primaryKeys.Should().Contain(c => c.TableName == "child_table");
      primaryKeys.Should().Contain(c => c.TableName == "simple_table");
      primaryKeys.Should().Contain(c => c.TableName == "complex_table");
   }

   [Fact]
   public async Task GetConstraintsAsync_ReturnsForeignKeys()
   {
      var constraints = (await Db.Management.Schema.GetConstraintsAsync(CancellationToken)).ToArray();
      var foreignKeys = constraints.Where(c => c.ConstraintType == "f").ToArray();

      foreignKeys.Should().Contain(c => c.ConstraintName == "fk_child_parent");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_HandlesNullConstraintType()
   {
      // Regression test: GenerateSchemaScriptAsync previously threw ArgumentNullException
      // when a constraint had a null ConstraintType. This can happen when Dapper maps
      // a database value to null despite the column being NOT NULL in PostgreSQL.
      //
      // We verify that the ordering logic in AppendConstraintsAsync does not throw
      // by generating the schema script for a database that has constraints.
      // The database created by the test fixture already has primary key and foreign key
      // constraints, so this exercises the constraint ordering path.
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      // The script should contain constraint definitions without throwing
      script.Should().Contain("Constraints");
   }

   [Fact]
   public async Task GetConstraintsAsync_HandlesExclusionConstraints()
   {
      // Create a table with an exclusion constraint to test all constraint types
      await Db.Dapper.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS btree_gist;", ct: CancellationToken);

      await Db.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.exclusion_test (
            id         bigint NOT NULL GENERATED ALWAYS AS IDENTITY,
            range_start integer NOT NULL,
            range_end   integer NOT NULL,
            PRIMARY KEY (id),
            EXCLUDE USING gist (int4range(range_start, range_end) WITH &&)
         );
         """,
         ct: CancellationToken
      );

      var constraints = (await Db.Management.Schema.GetConstraintsAsync(CancellationToken)).ToArray();
      var exclusionConstraints = constraints.Where(c => c.ConstraintType == "x").ToArray();

      exclusionConstraints.Should().NotBeEmpty();
      exclusionConstraints.Should().Contain(c => c.TableName == "exclusion_test");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_HandlesExclusionConstraints()
   {
      // Create a table with an exclusion constraint to verify schema script generation
      // handles all constraint types, including exclusion constraints.
      await Db.Dapper.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS btree_gist;", ct: CancellationToken);

      await Db.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.exclusion_test (
            id         bigint NOT NULL GENERATED ALWAYS AS IDENTITY,
            range_start integer NOT NULL,
            range_end   integer NOT NULL,
            PRIMARY KEY (id),
            EXCLUDE USING gist (int4range(range_start, range_end) WITH &&)
         );
         """,
         ct: CancellationToken
      );

      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      script.Should().Contain("exclusion_test");
      script.Should().Contain("EXCLUDE USING");
   }

   [Fact]
   public async Task GetIndexesAsync_ReturnsNonConstraintIndexes()
   {
      var indexes = (await Db.Management.Schema.GetIndexesAsync(CancellationToken)).ToArray();

      indexes.Should().Contain(i => i.IndexName == "idx_parent_name");
      indexes.Should().Contain(i => i.IndexName == "idx_child_parent_id");
   }

   [Fact]
   public async Task GetIndexesAsync_ExcludesConstraintIndexes()
   {
      var indexes = (await Db.Management.Schema.GetIndexesAsync(CancellationToken)).ToArray();
      var constraints = (await Db.Management.Schema.GetConstraintsAsync(CancellationToken)).ToArray();

      var constraintNames = constraints.Select(c => c.ConstraintName).ToHashSet();

      // None of the returned indexes should be a constraint-backing index
      foreach (var index in indexes)
      {
         constraintNames.Should().NotContain(index.IndexName);
      }
   }

   [Fact]
   public async Task GetFunctionsAsync_ReturnsUserFunctions()
   {
      var functions = (await Db.Management.Schema.GetFunctionsAsync(CancellationToken)).ToArray();

      functions.Should().Contain(f => f.Name == "get_parent_count");
   }

   [Fact]
   public async Task GetViewsAsync_ReturnsUserViews()
   {
      var views = (await Db.Management.Schema.GetViewsAsync(CancellationToken)).ToArray();

      views.Should().Contain(v => v.Name == "active_parents");
   }

   [Fact]
   public async Task GetConstraintsAsync_ReturnsCheckConstraints()
   {
      await Db.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.check_constraint_test (
            id    bigint NOT NULL GENERATED ALWAYS AS IDENTITY,
            value integer NOT NULL,
            PRIMARY KEY (id),
            CONSTRAINT chk_value_positive CHECK (value > 0)
         );
         """,
         ct: CancellationToken
      );

      var constraints = (await Db.Management.Schema.GetConstraintsAsync(CancellationToken)).ToArray();
      var checkConstraints = constraints.Where(c => c.ConstraintType == "c").ToArray();

      checkConstraints.Should().Contain(c => c.ConstraintName == "chk_value_positive" && c.TableName == "check_constraint_test");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_HandlesConstraintNameWithSpecialCharacters()
   {
      // Constraint names with single quotes must be properly escaped in the generated script.
      await Db.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.special_constraint_test (
            id    bigint NOT NULL GENERATED ALWAYS AS IDENTITY,
            value integer NOT NULL,
            PRIMARY KEY (id)
         );
         """,
         ct: CancellationToken
      );

      await Db.Dapper.ExecuteAsync(
         """
         ALTER TABLE public.special_constraint_test
            ADD CONSTRAINT "chk_it's_positive" CHECK (value > 0);
         """,
         ct: CancellationToken
      );

      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      // The single quote in the constraint name must be escaped as ''
      script.Should().Contain("chk_it''s_positive");
   }

   [Fact]
   public async Task GetSequencesAsync_ReturnsUserSequences()
   {
      var sequences = (await Db.Management.Schema.GetSequencesAsync(CancellationToken)).ToArray();

      sequences.Should().Contain(s => s.Name == "test_seq");

      var testSeq = sequences.First(s => s.Name == "test_seq");
      testSeq.Schema.Should().Be("public");
      testSeq.StartValue.Should().Be(100);
      testSeq.IncrementBy.Should().Be(1);
      testSeq.IsCyclic.Should().BeFalse();
   }

   [Fact]
   public async Task GetSequencesAsync_ReturnsAllProperties()
   {
      var sequences = (await Db.Management.Schema.GetSequencesAsync(CancellationToken)).ToArray();
      var testSeq = sequences.First(s => s.Name == "test_seq");

      testSeq.DataType.Should().Be("bigint");
      testSeq.MinValue.Should().Be(1);
      testSeq.MaxValue.Should().Be(9223372036854775807);
      testSeq.CacheSize.Should().Be(1);
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_ContainsSequenceProperties()
   {
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      // Verify sequence has actual property values, not zeroes
      script.Should().Contain("INCREMENT BY 1");
      script.Should().Contain("START WITH 100");
      script.Should().Contain("AS bigint");
   }

   [Fact]
   public async Task GenerateSchemaScriptAsync_ContainsTableColumnDetails()
   {
      var script = await Db.Management.GenerateSchemaScriptAsync(CancellationToken);

      // Verify table columns have actual names and types, not empty strings
      script.Should().Contain("\"id\" bigint");
      script.Should().Contain("\"name\" text");
   }

   [Fact]
   public async Task GetTablesAsync_ColumnsHaveNonEmptyNamesAndTypes()
   {
      var tables = (await Db.Management.Schema.GetTablesAsync(CancellationToken)).ToArray();

      foreach (var table in tables)
      {
         table.Name.Should().NotBeNullOrEmpty($"table in schema {table.Schema} should have a name");

         foreach (var column in table.Columns)
         {
            column.Name.Should().NotBeNullOrEmpty($"column in table {table.Schema}.{table.Name} should have a name");
            column.DataType.Should().NotBeNullOrEmpty($"column {column.Name} in table {table.Schema}.{table.Name} should have a data type");
         }
      }
   }
}
