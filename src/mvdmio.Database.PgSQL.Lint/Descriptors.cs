using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Lint;

internal static class Descriptors
{
   public static readonly DiagnosticDescriptor CommentBan = new(
      "MVDM0053",
      "Code carries no comments",
      "A '{0}' comment is not allowed. Explain the code through names and structure, or record the reason in an ADR (docs/adr/0013-code-carries-no-comments.md).",
      "mvdmio",
      DiagnosticSeverity.Error,
      isEnabledByDefault: true,
      description: "In every C# file, a //, /* */ or /// comment fails the build. Allowed: the reason at the end of a #pragma warning disable line, a comment that starts with pre-review:, and a /// comment on a member visible outside its assembly in a project that sets GenerateDocumentationFile to true."
   );
}
