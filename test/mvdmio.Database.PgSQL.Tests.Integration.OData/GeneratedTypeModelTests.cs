using AwesomeAssertions;
using Microsoft.OData.Edm;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    What OData's convention model builder makes of the types the generator emits. The conformance entity is held to a
///    standard — every one of its properties must be an EDM primitive — while the rest are characterization tests: the
///    generator's mappable-type allowlist admits property types with no straightforward EDM equivalent, and these
///    record what actually happens to each, so a consumer can plan around it and a version bump that changes it fails
///    the build.
/// </summary>
/// <remarks>
///    No database here: what is being asked is a question about the model, and the model is built from CLR types alone.
/// </remarks>
public class GeneratedTypeModelTests
{
   private static readonly IEdmModel _model = ODataConfiguration.BuildAwkwardModel();

   [Fact]
   public void ConventionModelBuilder_MapsEveryConformanceProperty_ToAnEdmPrimitiveOrEnum()
   {
      var entityType = (IEdmEntityType)ODataConfiguration.Model.FindDeclaredType($"{ODataConfiguration.EDM_NAMESPACE}.{nameof(SampleData)}")!;

      entityType.DeclaredProperties.Should().AllSatisfy(
         property => (property.Type.IsPrimitive() || property.Type.IsEnum()).Should().BeTrue($"'{property.Name}' maps to '{property.Type.FullName()}'")
      );
   }

   [Fact]
   public void ConventionModelBuilder_BuildsAModelForEveryMappableType()
   {
      // The headline finding: nothing on the allowlist makes model building fail. A repository generated from a real
      // table will start up, whatever its column types.
      AwkwardEntityType().DeclaredProperties.Select(x => x.Name).Should().BeEquivalentTo(
         nameof(AwkwardData.AwkwardId),
         nameof(AwkwardData.HomePage),
         nameof(AwkwardData.Metadata),
         nameof(AwkwardData.BirthDate),
         nameof(AwkwardData.WakeTime),
         nameof(AwkwardData.Duration),
         nameof(AwkwardData.Payload),
         nameof(AwkwardData.Initial),
         nameof(AwkwardData.SignedOffset),
         nameof(AwkwardData.SmallCount),
         nameof(AwkwardData.MediumCount),
         nameof(AwkwardData.LargeCount),
         nameof(AwkwardData.OccurredAt)
      );
   }

   [Theory]
   [InlineData(nameof(AwkwardData.BirthDate), "Edm.Date")]
   [InlineData(nameof(AwkwardData.WakeTime), "Edm.TimeOfDay")]
   [InlineData(nameof(AwkwardData.Duration), "Edm.Duration")]
   [InlineData(nameof(AwkwardData.Payload), "Edm.Binary")]
   [InlineData(nameof(AwkwardData.SignedOffset), "Edm.SByte")]
   public void ConventionModelBuilder_MapsTheTypeToItsEdmEquivalent(string propertyName, string edmTypeName)
   {
      TypeNameOf(propertyName).Should().Be(edmTypeName);
   }

   [Theory]
   [InlineData(nameof(AwkwardData.Initial), "Edm.String", "EDM has no character type, so a char widens to a one-character string")]
   [InlineData(nameof(AwkwardData.SmallCount), "Edm.Int32", "EDM has no unsigned integer, so a ushort widens")]
   [InlineData(nameof(AwkwardData.MediumCount), "Edm.Int64", "EDM has no unsigned integer, so a uint widens")]
   [InlineData(nameof(AwkwardData.OccurredAt), "Edm.DateTimeOffset", "EDM offers only an offset-bearing instant, so a DateTime acquires one by convention")]
   public void ConventionModelBuilder_WidensTheTypeToTheNearestEdmPrimitive(string propertyName, string edmTypeName, string because)
   {
      TypeNameOf(propertyName).Should().Be(edmTypeName, because);
   }

   [Fact]
   public void ConventionModelBuilder_MapsAnUnsignedLong_ToASignedLongAndSoLosesTheTopHalfOfItsRange()
   {
      // The only lossy mapping on the list. A column holding a value above long.MaxValue cannot be represented in the
      // model at all, so it is not usable in an OData endpoint even though the generator accepts it.
      TypeNameOf(nameof(AwkwardData.LargeCount)).Should().Be("Edm.Int64");
   }

   [Fact]
   public void ConventionModelBuilder_MapsAUri_ToAComplexTypeRatherThanAString()
   {
      // Not filterable or comparable as a value: OData reflects over Uri and keeps whichever of its members it can map,
      // which is one collection of path segments. The query surface stores the same property as text.
      var propertyType = PropertyOf(nameof(AwkwardData.HomePage)).Type;

      propertyType.IsComplex().Should().BeTrue();
      propertyType.FullName().Should().Be($"{ODataConfiguration.EDM_NAMESPACE}.{nameof(Uri)}");

      var complexType = (IEdmComplexType)propertyType.Definition;
      complexType.DeclaredProperties.Select(x => x.Name).Should().Equal(nameof(Uri.Segments));
   }

   [Fact]
   public void ConventionModelBuilder_MapsADictionary_ToACollectionOfAComplexTypeWithNoProperties()
   {
      // Unusable: a dictionary is an IEnumerable of key-value pairs to the convention builder, and the pair's members
      // are not properties it maps, so the collection element carries nothing. The query surface stores the same
      // property as JSONB.
      var propertyType = PropertyOf(nameof(AwkwardData.Metadata)).Type;

      propertyType.IsCollection().Should().BeTrue();

      var elementType = propertyType.AsCollection().ElementType();

      elementType.IsComplex().Should().BeTrue();
      ((IEdmComplexType)elementType.Definition).DeclaredProperties.Should().BeEmpty();
   }

   private static string TypeNameOf(string propertyName)
   {
      return PropertyOf(propertyName).Type.FullName();
   }

   private static IEdmProperty PropertyOf(string propertyName)
   {
      return AwkwardEntityType().FindProperty(propertyName)
         ?? throw new InvalidOperationException($"'{propertyName}' is not on the model.");
   }

   private static IEdmEntityType AwkwardEntityType()
   {
      return (IEdmEntityType)_model.FindDeclaredType($"{ODataConfiguration.EDM_NAMESPACE}.{nameof(AwkwardData)}")!;
   }
}
