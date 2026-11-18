using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Lint;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CommentBanAnalyzer : DiagnosticAnalyzer
{
   private const string DOCUMENTATION_FILE_PROPERTY = "GenerateDocumentationFile";

   public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.CommentBan);

   public override void Initialize(AnalysisContext context)
   {
      context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
      context.EnableConcurrentExecution();
      context.RegisterSyntaxTreeAction(AnalyzeComments);
   }

   private static void AnalyzeComments(SyntaxTreeAnalysisContext context)
   {
      var documentationFileGenerated = BuildProperties.IsTrue(context.Options, DOCUMENTATION_FILE_PROPERTY);
      var text = context.Tree.GetText(context.CancellationToken);
      var root = context.Tree.GetRoot(context.CancellationToken);

      foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
      {
         if (!CommentBanRules.IsComment(trivia) || CommentBanRules.IsAllowed(trivia, documentationFileGenerated))
            continue;

         var firstLine = text.Lines.GetLineFromPosition(trivia.FullSpan.Start);
         var span = TextSpan.FromBounds(trivia.FullSpan.Start, Math.Min(trivia.FullSpan.End, firstLine.End));
         context.ReportDiagnostic(Diagnostic.Create(Descriptors.CommentBan, Location.Create(context.Tree, span), CommentBanRules.KindName(trivia)));
      }
   }
}
