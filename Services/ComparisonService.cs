using BLUnion.Models;

namespace BLUnion.Services;

public sealed record MissingSpellInfo(uint SpellId, IReadOnlyList<string> PlayersMissingIt);

public sealed class ComparisonService
{
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

    public IReadOnlyList<(uint MonsterId, IReadOnlyList<uint> CoveredMissingSpellIds)> GroupMissingSpellsByMonster(
        IReadOnlyList<MissingSpellInfo> missingSpells,
        SpellDataService dataService,
        bool excludeTotems = false)
    {
        var missingIds = missingSpells.Select(m => m.SpellId).ToHashSet();
        var byMonster = new Dictionary<uint, List<uint>>();

        foreach (var spellId in missingIds)
        {
            foreach (var (monster, _, _) in dataService.GetSourcesForSpell(spellId, excludeTotems))
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
