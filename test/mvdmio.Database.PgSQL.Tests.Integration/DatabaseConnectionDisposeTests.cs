using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using System.Collections.Concurrent;

namespace mvdmio.Database.PgSQL.Tests.Integration;

public class DatabaseConnectionDisposeTests : TestBase
{
   private readonly TestFixture _fixture;

   public DatabaseConnectionDisposeTests(TestFixture fixture)
      : base(fixture)
   {
      _fixture = fixture;
   }

   [Fact]
   public async Task OpenAsync_AfterDispose_ShouldThrowObjectDisposedException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
      {
         await connection.OpenAsync(CancellationToken);
      });
   }

   [Fact]
   public async Task Open_AfterDispose_ShouldThrowObjectDisposedException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.DisposeAsync();

      Assert.Throws<ObjectDisposedException>(() =>
      {
         connection.Open();
      });
   }

   [Fact]
   public async Task CloseAsync_AfterDispose_ShouldThrowObjectDisposedException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.OpenAsync(CancellationToken);
      await connection.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
      {
         await connection.CloseAsync(CancellationToken);
      });
   }

   [Fact]
   public async Task Close_AfterDispose_ShouldThrowObjectDisposedException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      connection.Open();
      await connection.DisposeAsync();

      Assert.Throws<ObjectDisposedException>(() =>
      {
         connection.Close();
      });
   }

   [Fact]
   public async Task BeginTransactionAsync_AfterDispose_ShouldThrowObjectDisposedException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
      {
         await connection.BeginTransactionAsync(ct: CancellationToken);
      });
   }

   [Fact]
   public async Task BeginTransaction_AfterDispose_ShouldThrowObjectDisposedException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.DisposeAsync();

      Assert.Throws<ObjectDisposedException>(() =>
      {
         connection.BeginTransaction();
      });
   }

   [Fact]
   public async Task DisposeAsync_CalledMultipleTimes_ShouldBeIdempotent()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.OpenAsync(CancellationToken);

      await connection.DisposeAsync();
      var secondDisposeException = await Record.ExceptionAsync(async () =>
      {
         await connection.DisposeAsync();
      });
      var thirdDisposeException = await Record.ExceptionAsync(async () =>
      {
         await connection.DisposeAsync();
      });

      Assert.Null(secondDisposeException);
      Assert.Null(thirdDisposeException);
   }

   [Fact]
   public void Dispose_CalledMultipleTimes_ShouldBeIdempotent()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      connection.Open();

      connection.Dispose();
      var secondDisposeException = Record.Exception(() =>
      {
         connection.Dispose();
      });
      var thirdDisposeException = Record.Exception(() =>
      {
         connection.Dispose();
      });

      Assert.Null(secondDisposeException);
      Assert.Null(thirdDisposeException);
   }

   [Fact]
   public async Task DisposeAsync_WithOpenConnection_ShouldDisposeCleanly()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.OpenAsync(CancellationToken);

      var disposeException = await Record.ExceptionAsync(async () =>
      {
         await connection.DisposeAsync();
      });

      Assert.Null(disposeException);
   }

   [Fact]
   public void Dispose_WithOpenConnection_ShouldDisposeCleanly()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      connection.Open();

      var disposeException = Record.Exception(() =>
      {
         connection.Dispose();
      });

      Assert.Null(disposeException);
   }

   [Fact]
   public async Task DisposeAsync_WithActiveTransaction_ShouldDisposeCleanly()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.BeginTransactionAsync(ct: CancellationToken);

      var disposeException = await Record.ExceptionAsync(async () =>
      {
         await connection.DisposeAsync();
      });

      Assert.Null(disposeException);
   }

   [Fact]
   public void Dispose_WithActiveTransaction_ShouldDisposeCleanly()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      connection.BeginTransaction();

      var disposeException = Record.Exception(() =>
      {
         connection.Dispose();
      });

      Assert.Null(disposeException);
   }

   [Fact]
   public async Task DisposeAsync_WaitsForLockToBeReleased_ThenDisposes()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());

      await connection.OpenAsync(CancellationToken);

      var operations = Task.Run(async () =>
      {
         try
         {
            await connection.CloseAsync(CancellationToken);
            await connection.OpenAsync(CancellationToken);
            await connection.CloseAsync(CancellationToken);
         }
         catch (ObjectDisposedException)
         {
         }
      }, CancellationToken);

      var disposeTask = connection.DisposeAsync();

      await operations;
      await disposeTask;

      await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.OpenAsync(CancellationToken));
   }

   [Fact]
   public async Task ConcurrentOperations_ThenDispose_AllCompleteSuccessfully()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var operationCount = 10;

      await connection.OpenAsync(CancellationToken);
      await connection.CloseAsync(CancellationToken);
      var completedOperations = 1;

      var operationTasks = new List<Task>();
      for (var i = 0; i < operationCount; i++)
      {
         operationTasks.Add(Task.Run(async () =>
         {
            try
            {
               await connection.OpenAsync(CancellationToken);
               await Task.Delay(10);
               await connection.CloseAsync(CancellationToken);
               Interlocked.Increment(ref completedOperations);
            }
            catch (ObjectDisposedException)
            {
            }
         }, CancellationToken));
      }

      await Task.Delay(20, CancellationToken);

      await connection.DisposeAsync();

      await Task.WhenAll(operationTasks);

      Assert.True(completedOperations >= 1, "At least one operation should have completed");
   }

   [Fact]
   public async Task DisposeAsync_PreventsNewOperations_ExistingOperationsComplete()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());

      await connection.OpenAsync(CancellationToken);

      await connection.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.OpenAsync(CancellationToken));
      await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.CloseAsync(CancellationToken));
      await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.BeginTransactionAsync(ct: CancellationToken));
   }

   [Fact]
   public async Task CommitTransactionAsync_AfterDispose_ShouldThrowObjectDisposedException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.BeginTransactionAsync(ct: CancellationToken);
      await connection.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
      {
         await connection.CommitTransactionAsync(CancellationToken);
      });
   }

   [Fact]
   public void CommitTransaction_AfterDispose_ShouldThrowObjectDisposedException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      connection.BeginTransaction();
      connection.Dispose();

      Assert.Throws<ObjectDisposedException>(() =>
      {
         connection.CommitTransaction();
      });
   }

   [Fact]
   public async Task RollbackTransactionAsync_AfterDispose_ShouldThrowObjectDisposedException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.BeginTransactionAsync(ct: CancellationToken);
      await connection.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
      {
         await connection.RollbackTransactionAsync(CancellationToken);
      });
   }

   [Fact]
   public void RollbackTransaction_AfterDispose_ShouldThrowObjectDisposedException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      connection.BeginTransaction();
      connection.Dispose();

      Assert.Throws<ObjectDisposedException>(() =>
      {
         connection.RollbackTransaction();
      });
   }

   [Fact]
   public async Task DisposeAsync_WhileOperationHoldsConnectionLock_WaitsAndDisposesCleanly()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var operationStarted = new TaskCompletionSource();
      var operationCanComplete = new TaskCompletionSource();
      Exception? caughtException = null;

      var operationTask = Task.Run(async () =>
      {
         try
         {
            await connection.OpenAsync(CancellationToken);
            operationStarted.SetResult();

            await operationCanComplete.Task;

            await connection.CloseAsync(CancellationToken);
         }
         catch (ObjectDisposedException ex)
         {
            if (ex.ObjectName.Contains("SemaphoreSlim"))
               caughtException = ex;
         }
      }, CancellationToken);

      await operationStarted.Task;

      var disposeTask = Task.Run(async () =>
      {
         try
         {
            await connection.DisposeAsync();
         }
         catch (Exception ex)
         {
            caughtException = ex;
         }
      }, CancellationToken);

      await Task.Delay(50, CancellationToken);

      operationCanComplete.SetResult();

      await Task.WhenAll(operationTask, disposeTask);

      Assert.Null(caughtException);
   }

   [Fact]
   public async Task DisposeAsync_WhileOperationHoldsTransactionLock_WaitsAndDisposesCleanly()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var operationStarted = new TaskCompletionSource();
      var operationCanComplete = new TaskCompletionSource();
      Exception? caughtException = null;

      var operationTask = Task.Run(async () =>
      {
         try
         {
            await connection.BeginTransactionAsync(ct: CancellationToken);
            operationStarted.SetResult();

            await operationCanComplete.Task;

            await connection.CommitTransactionAsync(CancellationToken);
         }
         catch (ObjectDisposedException ex)
         {
            if (ex.ObjectName.Contains("SemaphoreSlim"))
               caughtException = ex;
         }
      }, CancellationToken);

      await operationStarted.Task;

      var disposeTask = Task.Run(async () =>
      {
         try
         {
            await connection.DisposeAsync();
         }
         catch (Exception ex)
         {
            caughtException = ex;
         }
      }, CancellationToken);

      await Task.Delay(50, CancellationToken);

      operationCanComplete.SetResult();

      await Task.WhenAll(operationTask, disposeTask);

      Assert.Null(caughtException);
   }

   [Fact(Timeout = 10_000)]
   public async Task DisposeAsync_WithManyConcurrentOperations_NoSemaphoreExceptions()
   {
      const int OPERATIONS = 100;
      var semaphoreExceptions = new List<Exception>();

      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var startSignal = new TaskCompletionSource();
      var exceptions = new ConcurrentBag<Exception>();

      var operations = Enumerable.Range(0, OPERATIONS).Select(_ => Task.Run(async () =>
      {
         await startSignal.Task;

         try
         {
            await connection.OpenAsync(CancellationToken);
            await connection.CloseAsync(CancellationToken);
         }
         catch (ObjectDisposedException ex) when (ex.ObjectName.Contains("SemaphoreSlim"))
         {
            exceptions.Add(ex);
         }
         catch (ObjectDisposedException)
         {
         }
      }, CancellationToken)).ToList();

      var disposeTask = Task.Run(
         async () =>
         {
            await startSignal.Task;

            try
            {
               await connection.DisposeAsync();
            }
            catch (Exception ex)
            {
               exceptions.Add(ex);
            }
         },
         CancellationToken
      );

      startSignal.SetResult();

      await Task.WhenAll(operations.Concat(new[] { disposeTask }));

      semaphoreExceptions.AddRange(exceptions);

      Assert.Empty(semaphoreExceptions);
   }

   [Fact]
   public async Task DisposeAsync_WhileOpenAsyncWaitingOnSemaphore_NoSemaphoreException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var firstOperationStarted = new TaskCompletionSource();
      var firstOperationCanComplete = new TaskCompletionSource();
      Exception? semaphoreException = null;

      var firstOperation = Task.Run(async () =>
      {
         await connection.OpenAsync(CancellationToken);
         firstOperationStarted.SetResult();
         await firstOperationCanComplete.Task;
         try
         {
            await connection.CloseAsync(CancellationToken);
         }
         catch (ObjectDisposedException)
         {
         }
      }, CancellationToken);

      await firstOperationStarted.Task;

      var secondOperationStarted = new TaskCompletionSource();
      var secondOperation = Task.Run(async () =>
      {
         secondOperationStarted.SetResult();
         try
         {
            await connection.OpenAsync(CancellationToken);
            await connection.CloseAsync(CancellationToken);
         }
         catch (ObjectDisposedException ex)
         {
            if (ex.ObjectName.Contains("SemaphoreSlim"))
               semaphoreException = ex;
         }
      }, CancellationToken);

      await secondOperationStarted.Task;
      await Task.Delay(20, CancellationToken);

      var disposeTask = connection.DisposeAsync();

      firstOperationCanComplete.SetResult();

      await firstOperation;
      await secondOperation;
      await disposeTask;

      Assert.Null(semaphoreException);
   }

   [Fact]
   public void Dispose_WhileOperationHoldsConnectionLock_WaitsAndDisposesCleanly()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var operationStarted = new ManualResetEventSlim(false);
      var operationCanComplete = new ManualResetEventSlim(false);
      Exception? caughtException = null;

      var operationThread = new Thread(() =>
      {
         try
         {
            connection.Open();
            operationStarted.Set();

            operationCanComplete.Wait();

            connection.Close();
         }
         catch (ObjectDisposedException ex)
         {
            if (ex.ObjectName.Contains("SemaphoreSlim"))
               caughtException = ex;
         }
      });
      operationThread.Start();

      operationStarted.Wait(CancellationToken);

      var disposeThread = new Thread(() =>
      {
         try
         {
            connection.Dispose();
         }
         catch (Exception ex)
         {
            caughtException = ex;
         }
      });
      disposeThread.Start();

      Thread.Sleep(50);

      operationCanComplete.Set();

      operationThread.Join();
      disposeThread.Join();

      Assert.Null(caughtException);
   }

   [Fact]
   public async Task DisposeAsync_OperationReleasesLockAfterDisposeStarts_NoException()
   {
      const int ITERATIONS = 50;
      var exceptions = new List<Exception>();

      for (var i = 0; i < ITERATIONS; i++)
      {
         var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());

         await connection.OpenAsync(CancellationToken);

         var closeTask = Task.Run(async () =>
         {
            try
            {
               await connection.CloseAsync(CancellationToken);
            }
            catch (ObjectDisposedException ex)
            {
               if (ex.ObjectName.Contains("SemaphoreSlim"))
                  exceptions.Add(ex);
            }
         }, CancellationToken);

         var disposeTask = Task.Run(async () =>
         {
            try
            {
               await connection.DisposeAsync();
            }
            catch (Exception ex)
            {
               exceptions.Add(ex);
            }
         }, CancellationToken);

         await Task.WhenAll(closeTask, disposeTask);
      }

      Assert.Empty(exceptions);
   }
}
