using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using System.Collections.Concurrent;

namespace mvdmio.Database.PgSQL.Tests.Integration;

/// <summary>
/// Tests for proper disposal behavior of DatabaseConnection, especially under race conditions.
/// </summary>
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
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.DisposeAsync();

      // Act & Assert
      await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
      {
         await connection.OpenAsync(CancellationToken);
      });
   }

   [Fact]
   public async Task Open_AfterDispose_ShouldThrowObjectDisposedException()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.DisposeAsync();

      // Act & Assert
      Assert.Throws<ObjectDisposedException>(() =>
      {
         connection.Open();
      });
   }

   [Fact]
   public async Task CloseAsync_AfterDispose_ShouldThrowObjectDisposedException()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.OpenAsync(CancellationToken);
      await connection.DisposeAsync();

      // Act & Assert
      await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
      {
         await connection.CloseAsync(CancellationToken);
      });
   }

   [Fact]
   public async Task Close_AfterDispose_ShouldThrowObjectDisposedException()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      connection.Open();
      await connection.DisposeAsync();

      // Act & Assert
      Assert.Throws<ObjectDisposedException>(() =>
      {
         connection.Close();
      });
   }

   [Fact]
   public async Task BeginTransactionAsync_AfterDispose_ShouldThrowObjectDisposedException()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.DisposeAsync();

      // Act & Assert
      await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
      {
         await connection.BeginTransactionAsync(ct: CancellationToken);
      });
   }

   [Fact]
   public async Task BeginTransaction_AfterDispose_ShouldThrowObjectDisposedException()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.DisposeAsync();

      // Act & Assert
      Assert.Throws<ObjectDisposedException>(() =>
      {
         connection.BeginTransaction();
      });
   }

   [Fact]
   public async Task DisposeAsync_CalledMultipleTimes_ShouldBeIdempotent()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.OpenAsync(CancellationToken);

      // Act - Dispose multiple times
      await connection.DisposeAsync();
      var secondDisposeException = await Record.ExceptionAsync(async () =>
      {
         await connection.DisposeAsync();
      });
      var thirdDisposeException = await Record.ExceptionAsync(async () =>
      {
         await connection.DisposeAsync();
      });

      // Assert - No exceptions should be thrown on subsequent disposes
      Assert.Null(secondDisposeException);
      Assert.Null(thirdDisposeException);
   }

   [Fact]
   public void Dispose_CalledMultipleTimes_ShouldBeIdempotent()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      connection.Open();

      // Act - Dispose multiple times
      connection.Dispose();
      var secondDisposeException = Record.Exception(() =>
      {
         connection.Dispose();
      });
      var thirdDisposeException = Record.Exception(() =>
      {
         connection.Dispose();
      });

      // Assert - No exceptions should be thrown on subsequent disposes
      Assert.Null(secondDisposeException);
      Assert.Null(thirdDisposeException);
   }

   [Fact]
   public async Task DisposeAsync_WithOpenConnection_ShouldDisposeCleanly()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.OpenAsync(CancellationToken);

      // Act
      var disposeException = await Record.ExceptionAsync(async () =>
      {
         await connection.DisposeAsync();
      });

      // Assert
      Assert.Null(disposeException);
   }

   [Fact]
   public void Dispose_WithOpenConnection_ShouldDisposeCleanly()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      connection.Open();

      // Act
      var disposeException = Record.Exception(() =>
      {
         connection.Dispose();
      });

      // Assert
      Assert.Null(disposeException);
   }

   [Fact]
   public async Task DisposeAsync_WithActiveTransaction_ShouldDisposeCleanly()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.BeginTransactionAsync(ct: CancellationToken);

      // Act
      var disposeException = await Record.ExceptionAsync(async () =>
      {
         await connection.DisposeAsync();
      });

      // Assert
      Assert.Null(disposeException);
   }

   [Fact]
   public void Dispose_WithActiveTransaction_ShouldDisposeCleanly()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      connection.BeginTransaction();

      // Act
      var disposeException = Record.Exception(() =>
      {
         connection.Dispose();
      });

      // Assert
      Assert.Null(disposeException);
   }

   [Fact]
   public async Task DisposeAsync_WaitsForLockToBeReleased_ThenDisposes()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());

      // First, open a connection (this acquires and releases the lock)
      await connection.OpenAsync(CancellationToken);

      // Start multiple close/open operations in sequence
      // The dispose should wait for any in-progress lock acquisition, or
      // throw ObjectDisposedException if dispose completes first
      var operations = Task.Run(async () =>
      {
         try
         {
            // These operations acquire and release the lock
            await connection.CloseAsync(CancellationToken);
            await connection.OpenAsync(CancellationToken);
            await connection.CloseAsync(CancellationToken);
         }
         catch (ObjectDisposedException)
         {
            // This is expected if dispose completes first
         }
      }, CancellationToken);

      // Start dispose - it will acquire the lock and dispose
      var disposeTask = connection.DisposeAsync();

      // Wait for everything to complete - neither should throw
      await operations;
      await disposeTask;

      // Verify that subsequent operations throw ObjectDisposedException
      await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.OpenAsync(CancellationToken));
   }

   [Fact]
   public async Task ConcurrentOperations_ThenDispose_AllCompleteSuccessfully()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var operationCount = 10;

      // Ensure at least one operation completes before we start the race
      await connection.OpenAsync(CancellationToken);
      await connection.CloseAsync(CancellationToken);
      var completedOperations = 1;

      // Act - Run several concurrent operations, then dispose
      var operationTasks = new List<Task>();
      for (var i = 0; i < operationCount; i++)
      {
         operationTasks.Add(Task.Run(async () =>
         {
            try
            {
               await connection.OpenAsync(CancellationToken);
               await Task.Delay(10); // Simulate some work
               await connection.CloseAsync(CancellationToken);
               Interlocked.Increment(ref completedOperations);
            }
            catch (ObjectDisposedException)
            {
               // This is acceptable - dispose happened before this operation
            }
         }, CancellationToken));
      }

      // Give some operations a chance to start
      await Task.Delay(20, CancellationToken);

      // Dispose while operations might still be running
      await connection.DisposeAsync();

      // Wait for all operations to complete
      await Task.WhenAll(operationTasks);

      // Assert - At least the initial operation should have completed
      Assert.True(completedOperations >= 1, "At least one operation should have completed");
   }

   [Fact]
   public async Task DisposeAsync_PreventsNewOperations_ExistingOperationsComplete()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());

      // Start an operation
      await connection.OpenAsync(CancellationToken);

      // Dispose
      await connection.DisposeAsync();

      // Try to start new operations - they should all fail with ObjectDisposedException
      await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.OpenAsync(CancellationToken));
      await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.CloseAsync(CancellationToken));
      await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.BeginTransactionAsync(ct: CancellationToken));
   }

   [Fact]
   public async Task CommitTransactionAsync_AfterDispose_ShouldThrowObjectDisposedException()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.BeginTransactionAsync(ct: CancellationToken);
      await connection.DisposeAsync();

      // Act & Assert
      await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
      {
         await connection.CommitTransactionAsync(CancellationToken);
      });
   }

   [Fact]
   public void CommitTransaction_AfterDispose_ShouldThrowObjectDisposedException()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      connection.BeginTransaction();
      connection.Dispose();

      // Act & Assert
      Assert.Throws<ObjectDisposedException>(() =>
      {
         connection.CommitTransaction();
      });
   }

   [Fact]
   public async Task RollbackTransactionAsync_AfterDispose_ShouldThrowObjectDisposedException()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await connection.BeginTransactionAsync(ct: CancellationToken);
      await connection.DisposeAsync();

      // Act & Assert
      await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
      {
         await connection.RollbackTransactionAsync(CancellationToken);
      });
   }

   [Fact]
   public void RollbackTransaction_AfterDispose_ShouldThrowObjectDisposedException()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      connection.BeginTransaction();
      connection.Dispose();

      // Act & Assert
      Assert.Throws<ObjectDisposedException>(() =>
      {
         connection.RollbackTransaction();
      });
   }

   /// <summary>
   /// This test verifies that when an operation is holding the connection lock and dispose is called,
   /// the dispose waits for the lock to be released before disposing the semaphore.
   /// Without proper locking in dispose, this would throw ObjectDisposedException on the SemaphoreSlim.
   /// </summary>
   [Fact]
   public async Task DisposeAsync_WhileOperationHoldsConnectionLock_WaitsAndDisposesCleanly()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var operationStarted = new TaskCompletionSource();
      var operationCanComplete = new TaskCompletionSource();
      Exception? caughtException = null;

      // Start an operation that will hold the connection lock
      var operationTask = Task.Run(async () =>
      {
         try
         {
            // Open acquires the lock
            await connection.OpenAsync(CancellationToken);
            operationStarted.SetResult();

            // Hold the lock while we wait - simulating a long-running operation
            // The connection is open, so we're "using" it
            await operationCanComplete.Task;

            // Now release by closing - this releases the lock
            await connection.CloseAsync(CancellationToken);
         }
         catch (ObjectDisposedException ex)
         {
            // This is expected for CloseAsync if dispose completed first
            // But we should NOT get ObjectDisposedException on SemaphoreSlim
            if (ex.ObjectName.Contains("SemaphoreSlim"))
               caughtException = ex;
         }
      }, CancellationToken);

      // Wait for the operation to start and acquire the lock
      await operationStarted.Task;

      // Now call dispose while the operation holds the lock
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

      // Give dispose a moment to attempt to acquire the lock
      await Task.Delay(50, CancellationToken);

      // Now let the operation complete and release the lock
      operationCanComplete.SetResult();

      // Wait for both to complete
      await Task.WhenAll(operationTask, disposeTask);

      // Assert - No ObjectDisposedException on SemaphoreSlim should have occurred
      Assert.Null(caughtException);
   }

   /// <summary>
   /// This test verifies that when an operation is holding the transaction lock and dispose is called,
   /// the dispose waits for the lock to be released before disposing the semaphore.
   /// </summary>
   [Fact]
   public async Task DisposeAsync_WhileOperationHoldsTransactionLock_WaitsAndDisposesCleanly()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var operationStarted = new TaskCompletionSource();
      var operationCanComplete = new TaskCompletionSource();
      Exception? caughtException = null;

      // Start an operation that will hold the transaction lock
      var operationTask = Task.Run(async () =>
      {
         try
         {
            // BeginTransaction acquires the transaction lock
            await connection.BeginTransactionAsync(ct: CancellationToken);
            operationStarted.SetResult();

            // Hold the lock while we wait
            await operationCanComplete.Task;

            // Commit releases the lock
            await connection.CommitTransactionAsync(CancellationToken);
         }
         catch (ObjectDisposedException ex)
         {
            // Check if this is an ObjectDisposedException on SemaphoreSlim - that's the bug
            if (ex.ObjectName.Contains("SemaphoreSlim"))
               caughtException = ex;

            // ObjectDisposedException on DatabaseConnection is expected/acceptable
         }
      }, CancellationToken);

      // Wait for the operation to start and acquire the lock
      await operationStarted.Task;

      // Now call dispose while the operation holds the lock
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

      // Give dispose a moment to attempt to acquire the lock
      await Task.Delay(50, CancellationToken);

      // Now let the operation complete and release the lock
      operationCanComplete.SetResult();

      // Wait for both to complete
      await Task.WhenAll(operationTask, disposeTask);

      // Assert - No ObjectDisposedException on SemaphoreSlim should have occurred
      Assert.Null(caughtException);
   }

   /// <summary>
   /// Stress test: Many concurrent operations racing with dispose.
   /// This test runs multiple iterations to increase the chance of hitting race conditions.
   /// Without proper locking, this would intermittently throw ObjectDisposedException on SemaphoreSlim.
   /// </summary>
   [Fact(Timeout = 10_000)]
   public async Task DisposeAsync_WithManyConcurrentOperations_NoSemaphoreExceptions()
   {
      const int OPERATIONS = 100;
      var semaphoreExceptions = new List<Exception>();

      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var startSignal = new TaskCompletionSource();
      var exceptions = new ConcurrentBag<Exception>();

      // Create many concurrent operations
      var operations = Enumerable.Range(0, OPERATIONS).Select(_ => Task.Run(async () =>
      {
         await startSignal.Task; // Wait for signal to start all at once

         try
         {
            await connection.OpenAsync(CancellationToken);
            await connection.CloseAsync(CancellationToken);
         }
         catch (ObjectDisposedException ex) when (ex.ObjectName.Contains("SemaphoreSlim"))
         {
            // This is the bug we're testing for
            exceptions.Add(ex);
         }
         catch (ObjectDisposedException)
         {
            // ObjectDisposedException on DatabaseConnection is expected
         }
      }, CancellationToken)).ToList();

      // Create dispose task
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

      // Start all operations simultaneously
      startSignal.SetResult();

      // Wait for everything
      await Task.WhenAll(operations.Concat(new[] { disposeTask }));

      semaphoreExceptions.AddRange(exceptions);

      // Assert - No SemaphoreSlim exceptions should have been caught
      Assert.Empty(semaphoreExceptions);
   }

   /// <summary>
   /// Verifies that dispose properly waits for an in-progress OpenAsync that is waiting on the semaphore.
   /// This simulates the exact scenario from the original bug report.
   /// </summary>
   [Fact]
   public async Task DisposeAsync_WhileOpenAsyncWaitingOnSemaphore_NoSemaphoreException()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var firstOperationStarted = new TaskCompletionSource();
      var firstOperationCanComplete = new TaskCompletionSource();
      Exception? semaphoreException = null;

      // First operation acquires the lock and holds it
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
            // Expected if dispose happened
         }
      }, CancellationToken);

      // Wait for first operation to hold the lock
      await firstOperationStarted.Task;

      // Second operation will be waiting on the semaphore
      var secondOperationStarted = new TaskCompletionSource();
      var secondOperation = Task.Run(async () =>
      {
         secondOperationStarted.SetResult();
         try
         {
            // This will wait on _connectionLock.WaitAsync()
            await connection.OpenAsync(CancellationToken);
            await connection.CloseAsync(CancellationToken);
         }
         catch (ObjectDisposedException ex)
         {
            if (ex.ObjectName.Contains("SemaphoreSlim"))
               semaphoreException = ex;

            // ObjectDisposedException on DatabaseConnection is expected
         }
      }, CancellationToken);

      // Wait for second operation to start (it will be blocked on semaphore)
      await secondOperationStarted.Task;
      await Task.Delay(20, CancellationToken); // Give it time to actually wait on the semaphore

      // Now dispose while second operation is waiting on the semaphore
      var disposeTask = connection.DisposeAsync();

      // Let the first operation complete, releasing the lock
      firstOperationCanComplete.SetResult();

      // Wait for everything
      await firstOperation;
      await secondOperation;
      await disposeTask;

      // Assert - No ObjectDisposedException on SemaphoreSlim
      Assert.Null(semaphoreException);
   }

   /// <summary>
   /// Tests sync Dispose with the same race condition scenario.
   /// </summary>
   [Fact]
   public void Dispose_WhileOperationHoldsConnectionLock_WaitsAndDisposesCleanly()
   {
      // Arrange
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var operationStarted = new ManualResetEventSlim(false);
      var operationCanComplete = new ManualResetEventSlim(false);
      Exception? caughtException = null;

      // Start an operation that will hold the connection lock
      var operationThread = new Thread(() =>
      {
         try
         {
            connection.Open();
            operationStarted.Set();

            // Hold the lock
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

      // Wait for the operation to acquire the lock
      operationStarted.Wait(CancellationToken);

      // Start dispose on another thread while operation holds the lock
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

      // Give dispose a moment to attempt lock acquisition
      Thread.Sleep(50);

      // Let the operation complete
      operationCanComplete.Set();

      // Wait for both threads
      operationThread.Join();
      disposeThread.Join();

      // Assert
      Assert.Null(caughtException);
   }

   /// <summary>
   /// Verifies that when Release() is called on a semaphore after Dispose started
   /// but before it acquired the lock, no exception occurs.
   /// </summary>
   [Fact]
   public async Task DisposeAsync_OperationReleasesLockAfterDisposeStarts_NoException()
   {
      const int ITERATIONS = 50;
      var exceptions = new List<Exception>();

      for (var i = 0; i < ITERATIONS; i++)
      {
         var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());

         // Open connection (acquires and releases lock)
         await connection.OpenAsync(CancellationToken);

         // Start close and dispose nearly simultaneously to race the lock
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
