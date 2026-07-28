using AwesomeAssertions;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    What OData's convention model builder makes of the relation properties the generator mirrors onto a data type.
///    Nothing here is expansion behaviour — it is what has to be true of the model before a client can ask for an
///    expansion at all, and each of these was previously something a consumer would have found out by trial.
/// </summary>
/// <remarks>
///    No database: this is a question about the model, and the model is built from CLR types alone. Parallel to
///    <see cref="GeneratedTypeModelTests" />, which asks the same kind of question about column-backed property types.
/// </remarks>
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

      // Cardinality comes from the property's type, not from the annotation: a collection relation is a many-valued
      // navigation property and a single one is not.
      NavigationOf(author, nameof(AuthorData.Mentor)).Type.IsCollection().Should().BeFalse();
      NavigationOf(author, nameof(AuthorData.Books)).Type.IsCollection().Should().BeTrue();

      EntityTypeOf(nameof(BookData)).NavigationProperties().Select(x => x.Name).Should().Equal(nameof(BookData.Author));
   }

   [Fact]
   public void ConventionModelBuilder_LeavesTheForeignKeyPropertyOnTheModelAlongsideTheNavigationProperty()
   {
      // The scalar is still there, so a client can filter on the key without expanding — and a null one is how "points
      // nowhere" is expressed.
      var author = EntityTypeOf(nameof(AuthorData));

      author.FindProperty(nameof(AuthorData.MentorId)).Should().NotBeNull();
      author.FindProperty(nameof(AuthorData.MentorId))!.Type.IsNullable.Should().BeTrue();
   }

   [Fact]
   public void ConventionModelBuilder_CannotDiscoverTheKeyByConvention_SoItHasToBeDeclared()
   {
      // Convention-based key discovery looks for "Id" or "<TypeName>Id" — AuthorData would need "AuthorDataId". A table
      // definition's key is neither, so model building fails on an entity set whose key is left to convention. This is
      // why ODataConfiguration.RelationModel declares both keys explicitly.
      var builder = new ODataConventionModelBuilder { Namespace = ODataConfiguration.EDM_NAMESPACE, ContainerName = "Container" };

      builder.EntitySet<AuthorData>("Authors");

      var failure = Record.Exception(() => builder.GetEdmModel());

      failure.Should().NotBeNull();
      failure!.Message.Should().Contain(nameof(AuthorData));
   }

   [Fact]
   public void ConventionModelBuilder_KeepsTheCycleTwoRelationsInOppositeDirectionsCreate()
   {
      // Expected rather than a mistake. ADR 0005 records that relations are one-directional and never paired, so a
      // child-to-parent relation alongside a parent-to-children relation already is a cycle — and a self-reference is one
      // on its own. There is nothing to remove; expansion depth is what bounds a client walking it.
      var author = EntityTypeOf(nameof(AuthorData));

      NavigationOf(author, nameof(AuthorData.Mentor)).ToEntityType().Should().BeSameAs(author);
      NavigationOf(author, nameof(AuthorData.Books)).ToEntityType().Should().BeSameAs(EntityTypeOf(nameof(BookData)));
      NavigationOf(EntityTypeOf(nameof(BookData)), nameof(BookData.Author)).ToEntityType().Should().BeSameAs(author);
   }

   [Fact]
   public void ConventionModelBuilder_ExposesAnExpandableTypeAsAnEntitySetOfItsOwn()
   {
      // Not strictly required to expand — the expansion is a projection, and a navigation property alone is enough for
      // one — but a consumer exposing an expandable type routes to it as well, so the model matches the routes.
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
