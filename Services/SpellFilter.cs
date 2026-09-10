namespace BLUnion.Services;

public static class SpellFilter
{
    public static bool Matches(string spellName, int spellbookOrder, string filterText)
    {
        var trimmed = filterText.Trim();
        if (trimmed.Length == 0)
            return true;

        var isHashPrefixed = trimmed.StartsWith('#');
        var candidate = isHashPrefixed ? trimmed[1..] : trimmed;

        // Ein alleinstehendes "#" (keine Ziffern danach) ist eine UNVOLLSTÄNDIGE Nummernsuche,
        // kein Namens-Suchbegriff - ohne diesen Sonderfall würde unten spellName.Contains("")
        // aufgerufen, was in .NET für JEDEN String true ist und dadurch ALLE Spells statt KEINEN
        // träfe (siehe TEST_REPORT.md, Finding "SpellFilter/#-matches-everything").
        if (isHashPrefixed && candidate.Length == 0)
            return false;

        if (candidate.Length > 0 && candidate.All(char.IsAsciiDigit))
            return int.TryParse(candidate, out var order) && order == spellbookOrder;

        return spellName.Contains(candidate, StringComparison.OrdinalIgnoreCase);
    }
}
