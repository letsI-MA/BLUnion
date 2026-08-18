namespace BLUnion.Services;

/// <summary>
/// Reiner Filter-Algorithmus für die Comparison-Tabelle (ein gemeinsames Textfeld für
/// Nummer- und Namenssuche) - dieselbe Logik wie im Filterfeld der Web-Companion
/// (docs/index.html). Bewusst als eigene, von ImGui unabhängige Klasse statt im
/// Rendering-Code verstreut, damit sie ohne Spielverbindung testbar ist.
/// </summary>
public static class SpellFilter
{
    /// <summary>
    /// Prüft, ob ein Spell zu <paramref name="filterText"/> passt.
    /// </summary>
    /// <remarks>
    /// - Leeres/nur-Whitespace-Feld: immer <c>true</c> (kein Filter).
    /// - Eingabe (getrimmt, optionales führendes '#' entfernt) besteht ausschließlich aus
    ///   Ziffern: wird als int geparst (führende Nullen damit automatisch egal) und gegen
    ///   <paramref name="spellbookOrder"/> verglichen. "58", "058", "#58", "#058" matchen
    ///   also alle denselben Spell.
    /// - Sonst: case-insensitive Teilstring-Suche irgendwo in <paramref name="spellName"/>
    ///   (nicht nur Präfix).
    /// </remarks>
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
