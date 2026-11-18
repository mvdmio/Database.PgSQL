using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using mvdmio.Database.PgSQL.Lint;

namespace mvdmio.Database.PgSQL.Analyzers.Tests;

internal static class CommentBanTestHost
{
   private const string DIAGNOSTIC_ID = "MVDM0053";

   public static Task VerifyAsync(string source, IReadOnlyDictionary<string, string> buildProperties)
   {
      var test = CreateTest(buildProperties);
      test.TestCode = source;

      return test.RunAsync(TestContext.Current.CancellationToken);
   }

   public static Task VerifySourceFileAsync(string path, string source)
   {
      var test = CreateTest(new Dictionary<string, string>());
      test.TestState.Sources.Add((path, source));

      return test.RunAsync(TestContext.Current.CancellationToken);
   }

   public static Task VerifyKindAsync(string source, string kind)
   {
      var test = CreateTest(new Dictionary<string, string>());
      test.TestCode = source;
      test.ExpectedDiagnostics.Add(new DiagnosticResult(DIAGNOSTIC_ID, DiagnosticSeverity.Error).WithLocation(0).WithArguments(kind));

      return test.RunAsync(TestContext.Current.CancellationToken);
   }

   private static CSharpAnalyzerTest<CommentBanAnalyzer, DefaultVerifier> CreateTest(IReadOnlyDictionary<string, string> buildProperties)
   {
      var test = new CSharpAnalyzerTest<CommentBanAnalyzer, DefaultVerifier>
      {
         ReferenceAssemblies = ReferenceAssemblies.Net.Net90
      };

      if (buildProperties.Count > 0)
      {
         var globalConfig = "is_global = true" + string.Concat(buildProperties.Select(property => $"\nbuild_property.{property.Key} = {property.Value}"));
         test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", globalConfig));
      }

      return test;
   }
}
