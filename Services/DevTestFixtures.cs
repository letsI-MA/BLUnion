using BLUnion.Models;

namespace BLUnion.Services;

/// <summary>
/// NUR FÜR LOKALE ENTWICKLUNG - kein Teil des Release-Verhaltens, erzeugt keine
/// echten Spielerdaten und greift nicht ins Spiel ein.
///
/// Bewusst dauerhaft im Code belassen (nicht aus Versehen als "Wegwerf-Debug-
/// Code" wieder rausnehmen!): solange am Comparison-/Lernplan-Feature
/// weitergearbeitet wird, braucht man mehrere, vom eigenen Status unabhängige
/// PlayerSpellStatus-Objekte, ohne jedes Mal echte weitere Personen in der
/// Party zu brauchen oder Export/Import-Codes hin- und herzukopieren.
/// </summary>
public static class DevTestFixtures
{
    /// <summary>
    /// Drei erfundene Test-Charaktere mit unterschiedlichem, aber überlappendem
    /// Fortschritt - gestaffelt über die echte "Stars"-Einstufung (1-5 Sterne
    /// Lernschwierigkeit, siehe <see cref="Spell.Stars"/>) aus den echten
    /// <see cref="SpellDataService"/>-Daten, statt geratener Spell-Ids.
    ///
    /// Wer wie weit kommt:
    ///   - Alice (Neuling):        nur Stars 1 (früh, leicht lernbar)
    ///   - Bob (Fortgeschritten):  Stars 1-3
    ///   - Charles (erfahren):     Stars 1-4
    ///
    /// Damit deckt der Comparison-Tab beim gleichzeitigen Laden aller drei alle
    /// vier interessanten Fälle ab, ganz ohne Sonderfall-Logik:
    ///   - Stars 1 (früh):        alle drei kennen sie      -> "alle drei haben" (taucht im Vergleich nicht auf)
    ///   - Stars 2-3 (mittel):    nur Alice fehlt sie       -> "genau EINER fehlt"
    ///   - Stars 4 (spät):        Alice UND Bob fehlt sie   -> "genau ZWEI fehlen"
    ///   - Stars 5 (sehr spät):   allen dreien fehlt sie    -> "allen fehlt sie" (höchste Priorität im Vergleich)
    /// </summary>
    public static PlayerSpellStatus CreateAlice(SpellDataService spellDataService) =>
        CreateFixture(spellDataService, "Alice", maxStars: 1);

    public static PlayerSpellStatus CreateBob(SpellDataService spellDataService) =>
        CreateFixture(spellDataService, "Bob", maxStars: 3);

    public static PlayerSpellStatus CreateCharles(SpellDataService spellDataService) =>
        CreateFixture(spellDataService, "Charles", maxStars: 4);

    private static PlayerSpellStatus CreateFixture(SpellDataService spellDataService, string characterName, int maxStars)
    {
        var learnedIds = spellDataService.Spells.Values
            .Where(spell => spell.Stars <= maxStars)
            .Select(spell => spell.Id)
            .ToHashSet();

        return new PlayerSpellStatus
        {
            CharacterName = characterName,
            LearnedSpellIds = learnedIds,
            IsLocalPlayer = false,
        };
    }
}
