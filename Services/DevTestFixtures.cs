using BLUnion.Models;

namespace BLUnion.Services;

public static class DevTestFixtures
{
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
