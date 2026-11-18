using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using System.Collections.Immutable;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Tests.Unit;

public class QueryableExtensionsTests
{
   [Fact]
   public void Include_OnAQueryThisLibraryDidNotProduce_ThrowsWithAnExplanation()
   {
      var foreign = new[] { new Row() }.AsQueryable();

      var failure = Record.Exception(() => foreign.Include(x => x.Related));

      failure.Should().BeOfType<NotSupportedException>();
      failure!.Message.Should().Contain("generated repository's Query()");
   }

   [Fact]
   public void Include_RecordsOneStepThatTheRewriterStrips()
   {
      var query = CreateQuery().Include(x => x.Related);

      StepsRecordedBy(query).Should().HaveCount(1);
   }

   [Fact]
   public void Include_WithAFilter_RecordsOneStepThatTheRewriterStrips()
   {
      var query = CreateQuery().Include(x => x.Children, children => children.Where(child => child.Related == null));

      StepsRecordedBy(query).Should().HaveCount(1);
   }

   [Fact]
   public void ThenInclude_RecordsASecondStepInCompositionOrder()
   {
      var query = CreateQuery()
         .Include(x => x.Children, children => children.Where(child => child.Related == null))
         .ThenInclude(x => x.Related);

      StepsRecordedBy(query).Should().HaveCount(2);
   }

   private static ImmutableArray<IncludeStep> StepsRecordedBy(IQueryable<Row> query)
   {
      var steps = ImmutableArray<IncludeStep>.Empty;

      IncludeRewriter.Rewrite(
         query.Expression,
         (innermost, recorded) =>
         {
            steps = recorded;
            return innermost;
         }
      );

      return steps;
   }

   private static IQueryable<Row> CreateQuery()
   {
      var root = Array.Empty<Row>().AsQueryable();

      return new TranslatedQueryable<Row>(new LinqQuerySource(new object(), () => root, () => null));
   }

   private sealed class Row
   {
      public Row? Related { get; set; }
      public List<Row> Children { get; set; } = [];
   }
}
