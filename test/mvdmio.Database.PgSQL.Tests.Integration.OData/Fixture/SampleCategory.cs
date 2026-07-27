namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    The enum column on <see cref="SampleTable" />. Declared with explicit values because the query surface stores an
///    enum as its underlying number, so the numbers are part of the fixture's data.
/// </summary>
public enum SampleCategory
{
   Standard = 0,
   Premium = 1,
   Legacy = 2
}
