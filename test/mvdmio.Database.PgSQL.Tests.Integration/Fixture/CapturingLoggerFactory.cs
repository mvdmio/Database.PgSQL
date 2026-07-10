using Microsoft.Extensions.Logging;

namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture;

/// <summary>
///    Logger factory that captures every log entry, so tests can assert on warnings emitted by the migrator.
/// </summary>
public sealed class CapturingLoggerFactory : ILoggerFactory
{
   private readonly CapturingLogger _logger = new();

   public List<(LogLevel Level, string Message)> Entries => _logger.Entries;

   public ILogger CreateLogger(string categoryName)
   {
      return _logger;
   }

   public void AddProvider(ILoggerProvider provider)
   {
      // No-op.
   }

   public void Dispose()
   {
   }

   private sealed class CapturingLogger : ILogger
   {
      public List<(LogLevel Level, string Message)> Entries { get; } = [];

      public IDisposable? BeginScope<TState>(TState state)
         where TState : notnull
      {
         return null;
      }

      public bool IsEnabled(LogLevel logLevel)
      {
         return true;
      }

      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
      {
         Entries.Add((logLevel, formatter(state, exception)));
      }
   }
}
