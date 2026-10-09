using AwesomeAssertions;
using mvdmio.Database.PgSQL.Internal;

namespace mvdmio.Database.PgSQL.Tests.Unit.Internal;

public class PoolDefaultsTests
{
   private const string BASE_CONNECTION_STRING = "Host=localhost;Database=test;Username=test;Password=test";
   private const string ENTRY_ASSEMBLY_NAME = "Some.Entry.App";

   [Fact]
   public void Resolve_WithNoKeywords_GivesCapOfTenAndTheEntryAssemblyName()
   {
      var result = PoolDefaults.Resolve(BASE_CONNECTION_STRING, ENTRY_ASSEMBLY_NAME);

      result.Should().Be(new PoolSettings(10, ENTRY_ASSEMBLY_NAME));
   }

   [Fact]
   public void Resolve_WithoutEntryAssembly_LeavesTheNameUnset()
   {
      var result = PoolDefaults.Resolve(BASE_CONNECTION_STRING, null);

      result.Should().Be(new PoolSettings(10, null));
   }

   [Theory]
   [InlineData("Maximum Pool Size")]
   [InlineData("maximum pool size")]
   [InlineData("MAXIMUM Pool SIZE")]
   [InlineData("MaxPoolSize")]
   [InlineData("maxpoolsize")]
   [InlineData("MAXPOOLSIZE")]
   public void Resolve_WithACapKeyword_KeywordBeatsTheDefaultCapAndKeepsTheDefaultName(string keyword)
   {
      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};{keyword}=42", ENTRY_ASSEMBLY_NAME);

      result.Should().Be(new PoolSettings(42, ENTRY_ASSEMBLY_NAME));
   }

   [Theory]
   [InlineData("Application Name")]
   [InlineData("application name")]
   [InlineData("APPLICATION Name")]
   [InlineData("ApplicationName")]
   [InlineData("applicationname")]
   [InlineData("APPLICATIONNAME")]
   public void Resolve_WithANameKeyword_KeywordBeatsTheDefaultNameAndKeepsTheDefaultCap(string keyword)
   {
      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};{keyword}=Keyword.App", ENTRY_ASSEMBLY_NAME);

      result.Should().Be(new PoolSettings(10, "Keyword.App"));
   }

   [Fact]
   public void Resolve_WithACapKeywordEqualToNpgsqlsDefault_KeepsTheKeywordValue()
   {
      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};Maximum Pool Size=100", ENTRY_ASSEMBLY_NAME);

      result.MaxPoolSize.Should().Be(100);
   }

   // ADO.NET's connection string parser drops a keyword with an empty value, so Npgsql never sees it either: the keyword
   // counts as absent and the default applies.
   [Fact]
   public void Resolve_WithAnEmptyNameKeyword_TreatsItAsAbsent()
   {
      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};Application Name=", ENTRY_ASSEMBLY_NAME);

      result.Should().Be(new PoolSettings(10, ENTRY_ASSEMBLY_NAME));
   }

   [Fact]
   public void Resolve_WithAnEmptyNameKeywordAndNoEntryAssembly_LeavesTheNameUnset()
   {
      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};Application Name=", null);

      result.Should().Be(new PoolSettings(10, null));
   }

   [Fact]
   public void Resolve_WithAnEmptyCapKeyword_TreatsItAsAbsent()
   {
      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};Maximum Pool Size=", ENTRY_ASSEMBLY_NAME);

      result.Should().Be(new PoolSettings(10, ENTRY_ASSEMBLY_NAME));
   }
}
