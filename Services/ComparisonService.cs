using BLUnion.Models;

namespace BLUnion.Services;

public sealed record MissingSpellInfo(uint SpellId, IReadOnlyList<string> PlayersMissingIt);

/// <summary>
/// Reiner Vergleichs-/Planungsalgorithmus. Bewusst unabhängig davon, WIE die
/// PlayerSpellStatus-Objekte zustande kamen (lokal ermittelt, importiert oder
/// gesynct) - siehe ISyncProvider.
/// </summary>
public sealed class ComparisonService
{
    /// <summary>
    /// Für jeden Spell (über alle bekannten Spells), der mindestens einem
    /// Spieler fehlt: welche Spieler ihn nicht haben. Absteigend sortiert
    /// nach Anzahl betroffener Spieler (Prioritätsregel 1 aus dem Konzept).
    /// </summary>
    public IReadOnlyList<MissingSpellInfo> GetCommonlyMissingSpells(
        IEnumerable<uint> allKnownSpellIds,
        IReadOnlyList<PlayerSpellStatus> partyStatus)
    {
        var result = new List<MissingSpellInfo>();

        foreach (var spellId in allKnownSpellIds)
        {
            var missingFor = partyStatus
                .Where(p => !p.LearnedSpellIds.Contains(spellId))
                .Select(p => p.CharacterName)
                .ToList();

            if (missingFor.Count > 0)
                result.Add(new MissingSpellInfo(spellId, missingFor));
        }

        return result.OrderByDescending(r => r.PlayersMissingIt.Count).ToList();
    }

    /// <summary>
    /// Gruppiert fehlende Spells nach Monster, um "ein Monster besuchen,
    /// mehrere Spells gleichzeitig lernen"-Kombinationen sichtbar zu machen
    /// (Konzept Punkt 5). Erfordert die Source-Daten aus SpellDataService.
    /// </summary>
    public IReadOnlyList<(uint MonsterId, IReadOnlyList<uint> CoveredMissingSpellIds)> GroupMissingSpellsByMonster(
        IReadOnlyList<MissingSpellInfo> missingSpells,
        SpellDataService dataService)
    {
        var missingIds = missingSpells.Select(m => m.SpellId).ToHashSet();
        var byMonster = new Dictionary<uint, List<uint>>();

        foreach (var spellId in missingIds)
        {
            foreach (var (monster, _, _) in dataService.GetSourcesForSpell(spellId))
            {
                if (!byMonster.TryGetValue(monster.Id, out var list))
                    byMonster[monster.Id] = list = [];
                list.Add(spellId);
            }
        }

        return byMonster
            .Select(kv => (MonsterId: kv.Key, CoveredMissingSpellIds: (IReadOnlyList<uint>)kv.Value))
            .OrderByDescending(x => x.CoveredMissingSpellIds.Count)
            .ToList();
    }
}
