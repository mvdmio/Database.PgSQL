using AwesomeAssertions;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

public class RelationTypeModelTests
{
   [Fact]
   public void ConventionModelBuilder_MapsEveryRelationProperty_ToANavigationPropertyAClientCanExpand()
   {
      var author = EntityTypeOf(nameof(AuthorData));

      author.NavigationProperties().Select(x => x.Name).Should().BeEquivalentTo(
         nameof(AuthorData.Mentor),
         nameof(AuthorData.Mentees),
         nameof(AuthorData.Books)
      );

      NavigationOf(author, nameof(AuthorData.Mentor)).Type.IsCollection().Should().BeFalse();
      NavigationOf(author, nameof(AuthorData.Books)).Type.IsCollection().Should().BeTrue();

      EntityTypeOf(nameof(BookData)).NavigationProperties().Select(x => x.Name).Should().Equal(nameof(BookData.Author));
   }

   [Fact]
   public void ConventionModelBuilder_LeavesTheForeignKeyPropertyOnTheModelAlongsideTheNavigationProperty()
   {
      var author = EntityTypeOf(nameof(AuthorData));

      author.FindProperty(nameof(AuthorData.MentorId)).Should().NotBeNull();
      author.FindProperty(nameof(AuthorData.MentorId))!.Type.IsNullable.Should().BeTrue();
   }

   [Fact]
   public void ConventionModelBuilder_CannotDiscoverTheKeyByConvention_SoItHasToBeDeclared()
   {
      var builder = new ODataConventionModelBuilder { Namespace = ODataConfiguration.EDM_NAMESPACE, ContainerName = "Container" };

      builder.EntitySet<AuthorData>("Authors");

      var failure = Record.Exception(() => builder.GetEdmModel());

      failure.Should().NotBeNull();
      failure!.Message.Should().Contain(nameof(AuthorData));
   }

   [Fact]
   public void ConventionModelBuilder_KeepsTheCycleTwoRelationsInOppositeDirectionsCreate()
   {
      var author = EntityTypeOf(nameof(AuthorData));

      NavigationOf(author, nameof(AuthorData.Mentor)).ToEntityType().Should().BeSameAs(author);
      NavigationOf(author, nameof(AuthorData.Books)).ToEntityType().Should().BeSameAs(EntityTypeOf(nameof(BookData)));
      NavigationOf(EntityTypeOf(nameof(BookData)), nameof(BookData.Author)).ToEntityType().Should().BeSameAs(author);
   }

   [Fact]
   public void ConventionModelBuilder_ExposesAnExpandableTypeAsAnEntitySetOfItsOwn()
   {
      var container = ODataConfiguration.RelationModel.EntityContainer;

      container.FindEntitySet("Authors").Should().NotBeNull();
      container.FindEntitySet("Books").Should().NotBeNull();
   }

   private static IEdmNavigationProperty NavigationOf(IEdmEntityType entityType, string propertyName)
   {
      return entityType.NavigationProperties().Single(x => string.Equals(x.Name, propertyName, StringComparison.Ordinal));
   }

   private static IEdmEntityType EntityTypeOf(string typeName)
   {
      return (IEdmEntityType)ODataConfiguration.RelationModel.FindDeclaredType($"{ODataConfiguration.EDM_NAMESPACE}.{typeName}")!;
   }
}
