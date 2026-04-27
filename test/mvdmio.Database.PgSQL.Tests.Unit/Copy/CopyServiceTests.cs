using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Schema.Models;
using mvdmio.Database.PgSQL.Tool.Copy;

namespace mvdmio.Database.PgSQL.Tests.Unit.Copy;

public class CopyServiceTests
{
   [Fact]
   public async Task CopyAsync_WithMissingTableInDestination_Throws()
   {
      var ct = TestContext.Current.CancellationToken;
      var sourceClient = new FakeClient
      {
         Tables = [Table("public", "users", "id", "name"), Table("public", "orders", "id")]
      };
      var destClient = new FakeClient
      {
         Tables = [Table("public", "users", "id", "name")]
      };

      var service = new CopyService(new FakeFactory(sourceClient, destClient));

      var act = async () => await service.CopyAsync("src", "dst", null, null, new NullReporter(), ct);

      var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
      ex.Message.Should().Contain("public.orders");
   }

   [Fact]
   public async Task CopyAsync_WithMissingColumnInDestination_Throws()
   {
      var ct = TestContext.Current.CancellationToken;
      var source = new FakeClient { Tables = [Table("public", "users", "id", "name", "email")] };
      var dest = new FakeClient { Tables = [Table("public", "users", "id", "name")] };

      var service = new CopyService(new FakeFactory(source, dest));

      var act = async () => await service.CopyAsync("src", "dst", null, null, new NullReporter(), ct);

      var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
      ex.Message.Should().Contain("email");
   }

   [Fact]
   public async Task CopyAsync_TruncatesAndCopiesEachTable()
   {
      var ct = TestContext.Current.CancellationToken;
      var source = new FakeClient
      {
         Tables = [Table("public", "users", "id", "name"), Table("public", "orders", "id", "amount")]
      };
      var dest = new FakeClient
      {
         Tables = [Table("public", "users", "id", "name"), Table("public", "orders", "id", "amount")]
      };

      var service = new CopyService(new FakeFactory(source, dest));
      var result = await service.CopyAsync("src", "dst", null, null, new NullReporter(), ct);

      dest.Truncated.Should().BeEquivalentTo(["public.orders", "public.users"]);
      dest.Copied.Select(c => $"{c.Schema}.{c.Table}").Should().BeEquivalentTo(["public.orders", "public.users"]);
      dest.ReplicationRoles.Should().ContainInOrder("replica", "origin");
      result.Tables.Should().HaveCount(2);
   }

   [Fact]
   public async Task CopyAsync_RestoresReplicationRoleEvenOnFailure()
   {
      var ct = TestContext.Current.CancellationToken;
      var source = new FakeClient { Tables = [Table("public", "users", "id")] };
      var dest = new FakeClient
      {
         Tables = [Table("public", "users", "id")],
         ThrowOnCopy = true
      };

      var service = new CopyService(new FakeFactory(source, dest));
      var act = async () => await service.CopyAsync("src", "dst", null, null, new NullReporter(), ct);

      await act.Should().ThrowAsync<InvalidOperationException>();
      dest.ReplicationRoles.Should().ContainInOrder("replica", "origin");
   }

   [Fact]
   public async Task CopyAsync_ExcludesTablesFromExcludeList()
   {
      var ct = TestContext.Current.CancellationToken;
      var source = new FakeClient
      {
         Tables = [Table("public", "users", "id"), Table("public", "audit_log", "id")]
      };
      var dest = new FakeClient
      {
         Tables = [Table("public", "users", "id"), Table("public", "audit_log", "id")]
      };

      var service = new CopyService(new FakeFactory(source, dest));
      await service.CopyAsync("src", "dst", null, ["public.audit_log"], new NullReporter(), ct);

      dest.Copied.Select(c => $"{c.Schema}.{c.Table}").Should().BeEquivalentTo(["public.users"]);
      dest.Truncated.Should().BeEquivalentTo(["public.users"]);
   }

   [Fact]
   public async Task CopyAsync_SkipsGeneratedAndAlwaysIdentityColumns()
   {
      var ct = TestContext.Current.CancellationToken;
      var sourceTable = new TableInfo
      {
         Schema = "public",
         Name = "things",
         Columns =
         [
            new ColumnInfo { Name = "id", DataType = "bigint", IsNullable = false, IsIdentity = true, IdentityGeneration = "ALWAYS", IsGeneratedStored = false },
            new ColumnInfo { Name = "name", DataType = "text", IsNullable = false, IsIdentity = false, IsGeneratedStored = false },
            new ColumnInfo { Name = "search", DataType = "tsvector", IsNullable = true, IsIdentity = false, IsGeneratedStored = true, GeneratedExpression = "to_tsvector(name)" }
         ]
      };
      var source = new FakeClient { Tables = [sourceTable] };
      var dest = new FakeClient { Tables = [sourceTable] };

      var service = new CopyService(new FakeFactory(source, dest));
      await service.CopyAsync("src", "dst", null, null, new NullReporter(), ct);

      dest.Copied.Should().ContainSingle();
      dest.Copied[0].Columns.Should().BeEquivalentTo(["name"]);
   }

   [Fact]
   public async Task CopyAsync_ResetsOwnedSequencesOnDestination()
   {
      var ct = TestContext.Current.CancellationToken;
      var source = new FakeClient { Tables = [Table("public", "users", "id")] };
      var dest = new FakeClient
      {
         Tables = [Table("public", "users", "id")],
         Sequences =
         [
            new SequenceInfo { Schema = "public", Name = "users_id_seq", DataType = "bigint", StartValue = 1, IncrementBy = 1, MinValue = 1, MaxValue = 9999999, CacheSize = 1, IsCyclic = false, OwnedByTable = "users", OwnedByColumn = "id" },
            new SequenceInfo { Schema = "public", Name = "freestanding_seq", DataType = "bigint", StartValue = 1, IncrementBy = 1, MinValue = 1, MaxValue = 9999999, CacheSize = 1, IsCyclic = false }
         ]
      };

      var service = new CopyService(new FakeFactory(source, dest));
      await service.CopyAsync("src", "dst", null, null, new NullReporter(), ct);

      dest.SequencesReset.Should().ContainSingle();
      dest.SequencesReset[0].Should().Be(("public", "users_id_seq", "users", "id"));
   }

   private static TableInfo Table(string schema, string name, params string[] columnNames)
   {
      return new TableInfo
      {
         Schema = schema,
         Name = name,
         Columns = columnNames.Select(c => new ColumnInfo
         {
            Name = c,
            DataType = "text",
            IsNullable = false,
            IsIdentity = false,
            IsGeneratedStored = false
         }).ToArray()
      };
   }

   private sealed class FakeFactory : ICopyDatabaseFactory
   {
      private readonly FakeClient _source;
      private readonly FakeClient _dest;
      private int _calls;

      public FakeFactory(FakeClient source, FakeClient dest)
      {
         _source = source;
         _dest = dest;
      }

      public ICopyDatabaseClient Create(string connectionString, IReadOnlyCollection<string>? schemas)
      {
         return _calls++ == 0 ? _source : _dest;
      }
   }

   private sealed class FakeClient : ICopyDatabaseClient
   {
      public IReadOnlyList<TableInfo> Tables { get; set; } = [];
      public IReadOnlyList<SequenceInfo> Sequences { get; set; } = [];
      public List<string> Truncated { get; } = [];
      public List<(string Schema, string Table, IReadOnlyList<string> Columns)> Copied { get; } = [];
      public List<string> ReplicationRoles { get; } = [];
      public List<(string SeqSchema, string SeqName, string TableName, string ColumnName)> SequencesReset { get; } = [];
      public bool ThrowOnCopy { get; set; }

      public DatabaseConnection RawConnection => throw new NotImplementedException();

      public ValueTask DisposeAsync() => ValueTask.CompletedTask;

      public Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken cancellationToken) => Task.FromResult(Tables);
      public Task<IReadOnlyList<SequenceInfo>> GetSequencesAsync(CancellationToken cancellationToken) => Task.FromResult(Sequences);

      public Task TruncateAsync(string schema, string table, CancellationToken cancellationToken)
      {
         Truncated.Add($"{schema}.{table}");
         return Task.CompletedTask;
      }

      public Task TruncateManyAsync(IReadOnlyList<(string Schema, string Name)> tables, CancellationToken cancellationToken)
      {
         foreach (var t in tables)
            Truncated.Add($"{t.Schema}.{t.Name}");
         return Task.CompletedTask;
      }

      public Task<long> CopyFromAsync(ICopyDatabaseClient source, string schema, string table, IReadOnlyList<string> columns, CancellationToken cancellationToken)
      {
         if (ThrowOnCopy)
            throw new InvalidOperationException("simulated copy failure");
         Copied.Add((schema, table, columns));
         return Task.FromResult(0L);
      }

      public Task<long> CountRowsAsync(string schema, string table, CancellationToken cancellationToken) => Task.FromResult(0L);

      public Task SetSessionReplicationRoleAsync(string role, CancellationToken cancellationToken)
      {
         ReplicationRoles.Add(role);
         return Task.CompletedTask;
      }

      public Task ResetSequenceToColumnMaxAsync(string sequenceSchema, string sequenceName, string tableName, string columnName, CancellationToken cancellationToken)
      {
         SequencesReset.Add((sequenceSchema, sequenceName, tableName, columnName));
         return Task.CompletedTask;
      }

      public List<(string Schema, string Table, string Column)> SerialSequencesReset { get; } = [];

      public Task ResetSerialSequenceAsync(string tableSchema, string tableName, string columnName, CancellationToken cancellationToken)
      {
         SerialSequencesReset.Add((tableSchema, tableName, columnName));
         return Task.CompletedTask;
      }

      public Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;
      public Task CloseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
   }

   private sealed class NullReporter : ICopyReporter
   {
      public void WriteInfo(string message) { }
      public void WriteWarning(string message) { }
      public void WriteError(string message) { }
   }
}
