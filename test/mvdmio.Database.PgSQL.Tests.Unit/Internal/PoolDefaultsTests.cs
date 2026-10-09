using AwesomeAssertions;
using mvdmio.Database.PgSQL.Internal;

namespace mvdmio.Database.PgSQL.Tests.Unit.Internal;

public class PoolDefaultsTests
{
   private const string BASE = "Host=localhost;Database=test;Username=test;Password=test";
   private const string ENTRY = "Some.Entry.App";

   [Fact]
   public void Resolve_WithNoKeywords_GivesCapOfTenAndTheEntryAssemblyName()
   {
      var result = PoolDefaults.Resolve(BASE, ENTRY);

      result.Should().Be(new PoolSettings(10, ENTRY));
   }

   [Fact]
   public void Resolve_WithoutEntryAssembly_LeavesTheNameUnset()
   {
      var result = PoolDefaults.Resolve(BASE, null);

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
      var result = PoolDefaults.Resolve($"{BASE};{keyword}=42", ENTRY);

      result.Should().Be(new PoolSettings(42, ENTRY));
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
      var result = PoolDefaults.Resolve($"{BASE};{keyword}=Keyword.App", ENTRY);

      result.Should().Be(new PoolSettings(10, "Keyword.App"));
   }

   [Fact]
   public void Resolve_WithACapKeywordEqualToNpgsqlsDefault_KeepsTheKeywordValue()
   {
      var result = PoolDefaults.Resolve($"{BASE};Maximum Pool Size=100", ENTRY);

      result.MaxPoolSize.Should().Be(100);
   }

   // ADO.NET's connection string parser drops a keyword with an empty value, so Npgsql never sees it either: the keyword
   // counts as absent and the default name applies.
   [Fact]
   public void Resolve_WithAnEmptyNameKeyword_TreatsItAsAbsent()
   {
      var result = PoolDefaults.Resolve($"{BASE};Application Name=", ENTRY);

      result.Should().Be(new PoolSettings(10, ENTRY));
   }

   [Fact]
   public void Resolve_WithAnEmptyNameKeywordAndNoEntryAssembly_LeavesTheNameUnset()
   {
      var result = PoolDefaults.Resolve($"{BASE};Application Name=", null);

      result.Should().Be(new PoolSettings(10, null));
   }
}
