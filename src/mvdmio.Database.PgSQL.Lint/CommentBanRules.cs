using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace mvdmio.Database.PgSQL.Lint;

internal static class CommentBanRules
{
   private const string PRE_REVIEW_WAIVER = "pre-review:";
   private const string EXTENSION_BLOCK_KEYWORD = "extension";

   public static bool IsComment(SyntaxTrivia trivia)
   {
      return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
             || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
             || IsDocumentationComment(trivia);
   }

   public static string KindName(SyntaxTrivia trivia)
   {
      if (IsDocumentationComment(trivia) || LooksLikeDocumentationComment(trivia.ToString()))
         return "///";

      return trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ? "/* */" : "//";
   }

   private static bool LooksLikeDocumentationComment(string text)
   {
      return text.StartsWith("///", StringComparison.Ordinal) && !text.StartsWith("////", StringComparison.Ordinal)
             || text.StartsWith("/**", StringComparison.Ordinal) && !text.StartsWith("/**/", StringComparison.Ordinal);
   }

   public static bool IsAllowed(SyntaxTrivia trivia, bool documentationFileGenerated)
   {
      if (IsDocumentationComment(trivia))
         return documentationFileGenerated && DocumentsVisibleMember(trivia);

      return IsTrailingPragmaDisableReason(trivia) || StartsWithPreReviewWaiver(trivia);
   }

   private static bool IsDocumentationComment(SyntaxTrivia trivia)
   {
      return trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
   }

   private static bool IsTrailingPragmaDisableReason(SyntaxTrivia trivia)
   {
      return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
             && trivia.Token.Parent?.FirstAncestorOrSelf<PragmaWarningDirectiveTriviaSyntax>() is { } pragma
             && pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword);
   }

   private static bool StartsWithPreReviewWaiver(SyntaxTrivia trivia)
   {
      return trivia.ToString().Substring(2).TrimStart().StartsWith(PRE_REVIEW_WAIVER, StringComparison.Ordinal);
   }

   private static bool DocumentsVisibleMember(SyntaxTrivia trivia)
   {
      var declaration = trivia.Token.Parent?.FirstAncestorOrSelf<SyntaxNode>(node => node is MemberDeclarationSyntax or EnumMemberDeclarationSyntax);

      return declaration is not null
             && declaration.GetFirstToken() == trivia.Token
             && IsVisibleOutsideAssembly(declaration);
   }

   private static bool IsVisibleOutsideAssembly(SyntaxNode declaration)
   {
      if (declaration is BaseNamespaceDeclarationSyntax or GlobalStatementSyntax)
         return false;

      for (var node = declaration; node is not null and not BaseNamespaceDeclarationSyntax and not CompilationUnitSyntax; node = node.Parent)
      {
         if (node is MemberDeclarationSyntax or EnumMemberDeclarationSyntax && !IsVisibleAlone(node))
            return false;
      }

      return true;
   }

   private static bool IsVisibleAlone(SyntaxNode declaration)
   {
      if (declaration is EnumMemberDeclarationSyntax)
         return true;

      if (declaration is TypeDeclarationSyntax { Keyword.ValueText: EXTENSION_BLOCK_KEYWORD })
         return true;

      var modifiers = ((MemberDeclarationSyntax)declaration).Modifiers;
      if (modifiers.Any(SyntaxKind.FileKeyword) || modifiers.Any(SyntaxKind.PrivateKeyword))
         return false;

      if (modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.ProtectedKeyword))
         return true;

      if (modifiers.Any(SyntaxKind.InternalKeyword))
         return false;

      return declaration.Parent is InterfaceDeclarationSyntax;
   }
}
