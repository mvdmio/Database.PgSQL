using AwesomeAssertions;
using mvdmio.Database.PgSQL.Dapper.TypeHandlers.Base;

namespace mvdmio.Database.PgSQL.Tests.Unit.Dapper;

/// <summary>
///    The handler a consumer registers for hand-written Dapper SQL. Only the read direction is covered, because that is
///    the only direction Dapper ever reaches it from: an enum parameter is resolved to its underlying type before the
///    handler table is consulted, so <c>SetValue</c> is unreachable from a statement's parameters.
/// </summary>
public class EnumAsStringTypeHandlerTests
{
   private enum WorkState
   {
      Open,
      InProgress
   }

   private readonly EnumAsStringTypeHandler<WorkState> _handler = new();

   [Theory]
   [InlineData("InProgress")]
   [InlineData("inprogress")]
   [InlineData("INPROGRESS")]
   public void Parse_GivenAMemberName_IgnoresItsCase(string stored)
   {
      // Matched to what Dapper does for a text column with no handler registered, and to what the query surface does for
      // a column whose storage claim is text, so a value differing in case reads the same way through all three.
      _handler.Parse(stored).Should().Be(WorkState.InProgress);
   }

   [Fact]
   public void Parse_GivenTheUnderlyingNumberAsText_AnswersTheMember()
   {
      _handler.Parse("1").Should().Be(WorkState.InProgress);
   }

   /// <summary>
   ///    Refused rather than answered with the enum's zero member. Dapper skips the assignment for a null column instead
   ///    of calling this, so a nullable enum column reads back as null and never arrives here — and a consumer calling the
   ///    handler directly is told what happened rather than handed a value the column does not hold.
   /// </summary>
   [Theory]
   [InlineData(false)]
   [InlineData(true)]
   public void Parse_GivenNull_Throws(bool asDbNull)
   {
      object? value = asDbNull ? DBNull.Value : null;

      var failure = Record.Exception(() => _handler.Parse(value!));

      failure.Should().BeOfType<ArgumentException>();
      failure!.Message.Should().Contain(nameof(WorkState));
   }
}
