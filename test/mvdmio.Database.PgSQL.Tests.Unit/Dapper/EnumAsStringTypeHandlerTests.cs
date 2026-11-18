using AwesomeAssertions;
using mvdmio.Database.PgSQL.Dapper.TypeHandlers.Base;

namespace mvdmio.Database.PgSQL.Tests.Unit.Dapper;

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
      _handler.Parse(stored).Should().Be(WorkState.InProgress);
   }

   [Fact]
   public void Parse_GivenTheUnderlyingNumberAsText_AnswersTheMember()
   {
      _handler.Parse("1").Should().Be(WorkState.InProgress);
   }

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
