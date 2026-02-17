using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Exceptions;

/// <summary>
///   Base class for all database-related exceptions.
/// </summary>
[PublicAPI]
public class DatabaseException : Exception
{
   /// <inheritdoc />
   public DatabaseException()
   {
   }

   /// <inheritdoc />
   public DatabaseException(string message)
      : base(message)
   {
   }

   /// <inheritdoc />
   public DatabaseException(string message, Exception inner)
      : base(message, inner)
   {
   }
}
