// Dev-Only: liefert feste Test-Spielerprofile für die "Dev: Alice/Bob/Charles laden"-Buttons
// bzw. das Testprofile-Veröffentlichen im Gruppenfinder (siehe UI/MainWindow.cs DrawSyncTab/
// DrawDevFixtureButton und Services/LiveSyncService.cs PublishDevTestProfiles). Die gesamte
// Klasse existiert nur in Debug-Builds, damit weder die Testdaten noch die zugehörige Dev-UI
// in einem Release-Build enthalten sein können - siehe die zugehörigen #if DEBUG-Blöcke dort.
#if DEBUG
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
#endif
