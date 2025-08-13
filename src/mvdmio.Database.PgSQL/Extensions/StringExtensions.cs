namespace mvdmio.Database.PgSQL.Extensions;

/// <summary>
/// Provides extension methods for the <see cref="string"/> type.
/// </summary>
public static class StringExtensions
{
   /// <summary>
   /// Indent the provided string.
   /// </summary>
   /// <param name="value">The string to indent.</param>
   /// <param name="spaces">The amount of indenting to apply.</param>
   /// <returns>An indented string.</returns>
   public static string Indented(this string value, int spaces = 3)
   {
      var indent = "";
      for (var i = 0; i < spaces; i++)
      {
         indent += ' ';
      }

      return $"{indent}{value.Replace("\n", $"\n{indent}", StringComparison.OrdinalIgnoreCase)}";
   }

   /// <summary>
   /// Trims newline characters and spaces from the given string.
   /// </summary>
   /// <param name="value">The string to trim.</param>
   /// <returns>A trimmed string.</returns>
   public static string TrimIncludingNewLines(this string value)
   {
      return value.Trim('\r', '\n', ' ');
   }
}
