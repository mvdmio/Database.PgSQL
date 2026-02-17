namespace mvdmio.Database.PgSQL.Internal;

/// <summary>
///    Provides helper methods for dealing with async methods in sync contexts.
/// </summary>
internal class AsyncHelper
{
   private static readonly TaskFactory _taskFactory = new TaskFactory(CancellationToken.None, TaskCreationOptions.None, TaskContinuationOptions.None, TaskScheduler.Default);

   /// <summary>
   ///    Runs the given async func synchronously and returns the result.
   /// </summary>
   /// <typeparam name="TResult">The type of the result.</typeparam>
   /// <param name="func">The async func to run synchronously.</param>
   /// <returns>The result of the async func</returns>
   public static TResult RunSync<TResult>(Func<Task<TResult>> func)
   {
      try
      {
         return _taskFactory.StartNew(func).Unwrap().GetAwaiter().GetResult();
      }
      catch (Exception ex) when (ex is ThreadAbortException or TaskCanceledException)
      {
         // Ignore. Happens when application is shutdown while process is running.
         return default!;
      }
   }

   /// <summary>
   ///    Runs the given async func.
   /// </summary>
   /// <param name="func">The async func to run synchronously.</param>
   public static void RunSync(Func<Task> func)
   {
      try
      {
         _taskFactory.StartNew(func).Unwrap().GetAwaiter().GetResult();
      }
      catch (Exception ex) when (ex is ThreadAbortException or TaskCanceledException)
      {
         // Ignore. Happens when application is shutdown while process is running.
      }
   }
}
