namespace mvdmio.Database.PgSQL.Analyzers.Tests;

public class CommentBanTests
{
   private static readonly IReadOnlyDictionary<string, string> _documentationFile = new Dictionary<string, string> { ["GenerateDocumentationFile"] = "true" };

   private static Task VerifyMembersAsync(string members, IReadOnlyDictionary<string, string>? properties = null)
   {
      var source = $$"""
         public class Service
         {
         {{members}}
         }
         """;

      return CommentBanTestHost.VerifyAsync(source, properties ?? new Dictionary<string, string>());
   }

   [Theory]
   [InlineData("{|MVDM0053:// note|}\npublic void Run() { }")]
   [InlineData("public void Run() { } {|MVDM0053:// note|}")]
   [InlineData("{|MVDM0053:/* note */|}\npublic void Run() { }")]
   [InlineData("{|MVDM0053:/// <summary>Runs.</summary>|}\nprivate void Run() { }")]
   [InlineData("{|MVDM0053:/// <summary>Runs.</summary>|}\npublic void Run() { }")]
   public Task Fails_AComment(string members)
   {
      return VerifyMembersAsync(members);
   }

   [Fact]
   public Task Fails_AMultiLineBlockOnItsFirstLine()
   {
      const string SOURCE = """
         public class Service
         {
            public void Run() { }

            {|MVDM0053:/* first line|}
               second line */
            public void Stop() { }
         }
         """;

      return CommentBanTestHost.VerifyAsync(SOURCE, new Dictionary<string, string>());
   }

   [Fact]
   public Task Fails_AMultiLineDocumentationCommentOnItsFirstLine()
   {
      return VerifyMembersAsync("{|MVDM0053:/// <summary>|}\n/// Runs.\n/// </summary>\npublic void Run() { }");
   }

   [Fact]
   public Task Fails_ABlockDocumentationComment()
   {
      return VerifyMembersAsync("{|MVDM0053:/** <summary>Runs.</summary> */|}\npublic void Run() { }");
   }

   [Theory]
   [InlineData("{|#0:/// <summary>Runs.</summary>|}\npublic void Run() { }", "///")]
   [InlineData("{|#0:/** <summary>Runs.</summary> */|}\npublic void Run() { }", "///")]
   [InlineData("{|#0:// note|}\npublic void Run() { }", "//")]
   [InlineData("{|#0://// note|}\npublic void Run() { }", "//")]
   [InlineData("{|#0:/* note */|}\npublic void Run() { }", "/* */")]
   [InlineData("{|#0:/**/|}\npublic void Run() { }", "/* */")]
   public Task Message_NamesTheKindOfComment(string members, string kind)
   {
      var source = $$"""
         public class Service
         {
         {{members}}
         }
         """;

      return CommentBanTestHost.VerifyKindAsync(source, kind);
   }

   [Fact]
   public Task Passes_APragmaDisableWithATrailingReason()
   {
      return VerifyMembersAsync("#pragma warning disable CS0169 // the field no page shows\nprivate int _unused;\n#pragma warning restore CS0169");
   }

   [Fact]
   public Task Passes_APragmaDisableWithoutAnIdAndATrailingReason()
   {
      return VerifyMembersAsync("#pragma warning disable // every warning, for a reason\nprivate int _unused;\n#pragma warning restore");
   }

   [Fact]
   public Task Fails_AReasonOnTheLineAbovePragmaDisable()
   {
      return VerifyMembersAsync("{|MVDM0053:// the field no page shows|}\n#pragma warning disable CS0169\nprivate int _unused;\n#pragma warning restore CS0169");
   }

   [Fact]
   public Task Fails_ATrailingCommentOnPragmaRestore()
   {
      return VerifyMembersAsync("#pragma warning disable CS0169\nprivate int _unused;\n#pragma warning restore CS0169 {|MVDM0053:// done|}");
   }

   [Fact]
   public Task Passes_ASingleLinePreReviewWaiver()
   {
      return VerifyMembersAsync("// pre-review: the shared fixture needs the literal\npublic void Run() { }");
   }

   [Fact]
   public Task Passes_ABlockPreReviewWaiver()
   {
      return VerifyMembersAsync("public void Run() { /* pre-review: the shared fixture needs the literal */ }");
   }

   [Fact]
   public Task Fails_ACommentThatOnlyMentionsPreReview()
   {
      return VerifyMembersAsync("{|MVDM0053:// see pre-review: the waiver|}\npublic void Run() { }");
   }

   [Theory]
   [InlineData("public void Run() { }")]
   [InlineData("protected void Run() { }")]
   [InlineData("protected internal void Run() { }")]
   public Task Passes_ADocumentationCommentOnAMemberVisibleOutsideTheAssembly(string member)
   {
      return VerifyMembersAsync("/// <summary>Runs.</summary>\n" + member, _documentationFile);
   }

   [Theory]
   [InlineData("internal void Run() { }")]
   [InlineData("private protected void Run() { }")]
   [InlineData("private void Run() { }")]
   [InlineData("void Run() { }")]
   public Task Fails_ADocumentationCommentOnAMemberHiddenFromOtherAssemblies(string member)
   {
      return VerifyMembersAsync("{|MVDM0053:/// <summary>Runs.</summary>|}\n" + member, _documentationFile);
   }

   [Fact]
   public Task Fails_ADocumentationCommentOnAPublicMemberOfAnInternalType()
   {
      const string SOURCE = """
         internal class Hidden
         {
            {|MVDM0053:/// <summary>Runs.</summary>|}
            public void Run() { }
         }
         """;

      return CommentBanTestHost.VerifyAsync(SOURCE, _documentationFile);
   }

   [Fact]
   public Task Fails_ADocumentationCommentOnATypeWithoutAModifier()
   {
      const string SOURCE = """
         {|MVDM0053:/// <summary>Runs.</summary>|}
         class Hidden
         {
         }
         """;

      return CommentBanTestHost.VerifyAsync(SOURCE, _documentationFile);
   }

   [Fact]
   public Task Passes_ADocumentationCommentOnAnInterfaceMemberAndAnEnumMember()
   {
      const string SOURCE = """
         /// <summary>Runs.</summary>
         public interface IRunner
         {
            /// <summary>Runs.</summary>
            void Run();
         }

         /// <summary>Modes.</summary>
         public enum Mode
         {
            /// <summary>Fast.</summary>
            Fast
         }
         """;

      return CommentBanTestHost.VerifyAsync(SOURCE, _documentationFile);
   }

   [Fact]
   public Task Passes_ADocumentationCommentOnAPublicMemberOfAnExtensionBlock()
   {
      const string SOURCE = """
         public static class Extensions
         {
            extension(string text)
            {
               /// <summary>Counts.</summary>
               public int Count()
               {
                  return text.Length;
               }
            }
         }
         """;

      return CommentBanTestHost.VerifyAsync(SOURCE, _documentationFile);
   }

   [Fact]
   public Task Fails_ADocumentationCommentOnAnInternalMemberOfAnExtensionBlock()
   {
      const string SOURCE = """
         public static class Extensions
         {
            extension(string text)
            {
               {|MVDM0053:/// <summary>Counts.</summary>|}
               internal int Count()
               {
                  return text.Length;
               }
            }
         }
         """;

      return CommentBanTestHost.VerifyAsync(SOURCE, _documentationFile);
   }

   [Fact]
   public Task Fails_ADocumentationCommentOnAMemberOfAnExtensionBlockOfAnInternalClass()
   {
      const string SOURCE = """
         internal static class Extensions
         {
            extension(string text)
            {
               {|MVDM0053:/// <summary>Counts.</summary>|}
               public int Count()
               {
                  return text.Length;
               }
            }
         }
         """;

      return CommentBanTestHost.VerifyAsync(SOURCE, _documentationFile);
   }

   [Fact]
   public Task Fails_ADocumentationCommentOnAMemberOfAnInternalInterface()
   {
      const string SOURCE = """
         internal interface IRunner
         {
            {|MVDM0053:/// <summary>Runs.</summary>|}
            void Run();
         }
         """;

      return CommentBanTestHost.VerifyAsync(SOURCE, _documentationFile);
   }

   [Fact]
   public Task Fails_AnOrdinaryCommentWhereDocumentationIsAllowed()
   {
      return VerifyMembersAsync("{|MVDM0053:// note|}\npublic void Run() { }", _documentationFile);
   }

   [Fact]
   public Task Passes_ACommentInAGeneratedFile()
   {
      const string SOURCE = """
         // <auto-generated/>
         public class Service
         {
            // note
            public void Run() { }
         }
         """;

      return CommentBanTestHost.VerifySourceFileAsync("/0/Service.g.cs", SOURCE);
   }

   [Fact]
   public Task Passes_ACommentMarkerInsideAStringLiteral()
   {
      return VerifyMembersAsync("public string Url = \"https://example.com\";");
   }
}
