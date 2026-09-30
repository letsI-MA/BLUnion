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
}
