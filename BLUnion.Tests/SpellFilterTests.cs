using BLUnion.Services;
using Xunit;

namespace BLUnion.Tests;

public class SpellFilterTests
{
    [Fact]
    public void EmptyFilter_MatchesEverything()
    {
        Assert.True(SpellFilter.Matches("Fireball", 42, ""));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void WhitespaceOnlyFilter_MatchesEverything(string whitespace)
    {
        Assert.True(SpellFilter.Matches("Fireball", 42, whitespace));
    }

    [Fact]
    public void NameFilter_SubstringMatch_ReturnsTrue()
    {
        Assert.True(SpellFilter.Matches("Fireball", 1, "ball"));
    }

    [Fact]
    public void NameFilter_NonMatchingSubstring_ReturnsFalse()
    {
        Assert.False(SpellFilter.Matches("Fireball", 1, "xyz"));
    }

    [Theory]
    [InlineData("fireball")]
    [InlineData("FIREBALL")]
    [InlineData("FireBall")]
    [InlineData("ball")]
    [InlineData("BALL")]
    public void NameFilter_IsCaseInsensitive(string filterText)
    {
        Assert.True(SpellFilter.Matches("Fireball", 1, filterText));
    }

    [Fact]
    public void HashNumberFilter_MatchesExactSpellbookOrder()
    {
        Assert.True(SpellFilter.Matches("Fireball", 58, "#058"));
    }

    [Fact]
    public void HashNumberFilter_WithoutLeadingZeros_StillMatches()
    {
        Assert.True(SpellFilter.Matches("Fireball", 58, "#58"));
    }

    [Fact]
    public void HashNumberFilter_NonMatchingOrder_ReturnsFalse()
    {
        Assert.False(SpellFilter.Matches("Fireball", 58, "#59"));
    }

    [Fact]
    public void PlainNumberFilter_WithoutHash_MatchesBySpellbookOrder()
    {
        // Laut Implementierung wird auch eine reine Zahl (ohne führendes '#') als
        // Nummernsuche behandelt, nicht als Namenssuche.
        Assert.True(SpellFilter.Matches("Fireball", 58, "58"));
    }

    [Fact]
    public void PlainNumberFilter_NonMatchingOrder_ReturnsFalse()
    {
        Assert.False(SpellFilter.Matches("Fireball", 58, "12"));
    }

    [Fact]
    public void PlainNumberFilter_DoesNotAccidentallyMatchAsNameSubstring()
    {
        // "1" ist eine reine Zahl -> Nummernvergleich (order == 1), NICHT ein Namens-Teilstring-
        // Vergleich, obwohl der Spellname z.B. "Level 100" enthalten könnte.
        Assert.False(SpellFilter.Matches("Level 100", 100, "1"));
    }

    [Theory]
    [InlineData("#")]
    [InlineData("  #  ")]
    public void HashOnly_NoDigitsAfter_MatchesNothing(string filterText)
    {
        // candidate nach dem Entfernen von '#' ist leer - ein alleinstehendes '#' ist eine
        // UNVOLLSTÄNDIGE Nummernsuche, kein Namens-Suchbegriff, und darf deshalb NICHT auf den
        // Namensvergleich zurückfallen (dort würde spellName.Contains("") greifen, was in .NET für
        // JEDEN String true ist - siehe TEST_REPORT.md, Finding "SpellFilter/#-matches-everything",
        // dort ursprünglich als Bug dokumentiert und hier behoben). Case gegen Regression: egal ob
        // der Spellname zufällig "#" enthält oder nicht, darf hier nichts matchen.
        Assert.False(SpellFilter.Matches("Fireball", 1, filterText));
        Assert.False(SpellFilter.Matches("", 1, filterText));
    }

    [Fact]
    public void MixedAlphanumericFilter_FallsBackToNameSearch()
    {
        // "a1" besteht nicht NUR aus Ziffern -> candidate.All(char.IsAsciiDigit) ist false,
        // fällt auf den Namensvergleich zurück.
        Assert.False(SpellFilter.Matches("Fireball", 1, "a1"));
        Assert.True(SpellFilter.Matches("a1 Special", 1, "a1"));
    }

    [Fact]
    public void FilterText_IsTrimmedBeforeMatching()
    {
        Assert.True(SpellFilter.Matches("Fireball", 58, "  #58  "));
        Assert.True(SpellFilter.Matches("Fireball", 1, "  ball  "));
    }

    [Fact]
    public void HashNumberFilter_NonNumericAfterHash_TryParseFailsGracefully()
    {
        // "#abc" -> candidate = "abc", All(IsAsciiDigit) ist false -> Namenspfad statt Zahlenpfad,
        // "abc" matcht den Namen nicht -> false, kein Absturz durch int.TryParse.
        Assert.False(SpellFilter.Matches("Fireball", 1, "#abc"));
    }
}
