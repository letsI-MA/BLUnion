namespace BLUnion.Services;

public static class SpellFilter
{
    public static bool Matches(string spellName, int spellbookOrder, string filterText)
    {
        var trimmed = filterText.Trim();
        if (trimmed.Length == 0)
            return true;

        var candidate = trimmed.StartsWith('#') ? trimmed[1..] : trimmed;

        if (candidate.Length > 0 && candidate.All(char.IsAsciiDigit))
            return int.TryParse(candidate, out var order) && order == spellbookOrder;

        return spellName.Contains(candidate, StringComparison.OrdinalIgnoreCase);
    }
}
