using BLUnion.Models;
using Xunit;

namespace BLUnion.Tests;

public class SourceMethodTests
{
    public static IEnumerable<object[]> AllMethodsAndLanguages() =>
        from method in Enum.GetValues<SourceMethod>()
        from language in Enum.GetValues<DisplayLanguage>()
        select new object[] { method, language };

    // Erwartete Anzeige je Enum-Wert (DE, EN) - fängt vertauschte UiStrings-Keys oder Texte ab, die
    // nicht zum Key passen (z.B. Duty -> "Open World"). Neue Enum-Werte müssen hier ergänzt werden,
    // siehe ExpectedNames_CoverEveryEnumValue.
    private static readonly Dictionary<SourceMethod, (string German, string English)> ExpectedNames = new()
    {
        [SourceMethod.Duty] = ("Duty", "Duty"),
        [SourceMethod.OpenWorld] = ("Open World", "Open World"),
        [SourceMethod.Levequest] = ("Levequest", "Levequest"),
        [SourceMethod.DungeonTrash] = ("Dungeon-Trash", "Dungeon trash"),
        [SourceMethod.MaskedCarnivale] = ("Die Große Maskerade", "Masked Carnivale"),
        [SourceMethod.Totem] = ("Totem", "Totem"),
        [SourceMethod.TotemSpellCount] = ("Totem (nach Spell-Anzahl)", "Totem (by spell count)"),
        [SourceMethod.TotemLevel] = ("Totem (nach Level)", "Totem (by level)"),
        [SourceMethod.MaskedCarnivaleProgress] = ("Die Große Maskerade (Fortschritt)", "Masked Carnivale (progress)"),
        [SourceMethod.StartingSpell] = ("Von Anfang an bekannt", "Known from the start"),
    };

    public static IEnumerable<object[]> ExpectedNameRows() =>
        ExpectedNames.Select(pair => new object[] { pair.Key, pair.Value.German, pair.Value.English });

    [Theory]
    [MemberData(nameof(AllMethodsAndLanguages))]
    public void GetDisplayName_ReturnsNonEmptyText(SourceMethod method, DisplayLanguage language)
    {
        Assert.False(string.IsNullOrWhiteSpace(method.GetDisplayName(language)));
    }

    [Theory]
    [MemberData(nameof(ExpectedNameRows))]
    public void GetDisplayName_MatchesExpectedTerm_InGermanAndEnglish(SourceMethod method, string german, string english)
    {
        Assert.Equal(german, method.GetDisplayName(DisplayLanguage.German));
        Assert.Equal(english, method.GetDisplayName(DisplayLanguage.English));
    }

    [Fact]
    public void ExpectedNames_CoverEveryEnumValue()
    {
        Assert.Equal(Enum.GetValues<SourceMethod>().Order(), ExpectedNames.Keys.Order());
    }

    // Kein Wert darf in einer Sprache denselben Text wie ein ANDERER Wert liefern - sonst wären zwei
    // Lernmethoden im Tooltip nicht unterscheidbar.
    [Theory]
    [MemberData(nameof(Languages))]
    public void GetDisplayName_IsDistinctPerLanguage(DisplayLanguage language)
    {
        var names = Enum.GetValues<SourceMethod>().Select(method => method.GetDisplayName(language)).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    public static IEnumerable<object[]> Languages() =>
        Enum.GetValues<DisplayLanguage>().Select(language => new object[] { language });
}
