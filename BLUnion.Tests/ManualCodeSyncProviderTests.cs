using BLUnion.Models;
using BLUnion.Services;
using BLUnion.Tests.Fakes;
using Xunit;

namespace BLUnion.Tests;

// Bonus-Abdeckung über die in der Aufgabenstellung explizit genannten drei Services (SpellFilter/
// ComparisonService/SpellDataService) hinaus: ManualCodeSyncProvider hat - anders als
// LocalSpellUnlockService/PartyService/LiveSyncService - KEINE Dalamud-Laufzeitabhängigkeit außer
// SpellDataService selbst (für das bereits ein Fixture-Setup existiert), ist also ohne Mehraufwand
// isoliert testbar. Deckt denselben Encode/Decode-Symmetrie-Aspekt ab, der in TEST_REPORT.md auch
// für die Web-Companion-JS-Implementierung (encodeCompact/decodeCompact) geprüft wird - beide
// Implementierungen MÜSSEN kompatibel sein (Aufgabenstellung "keine Parallelstruktur").
public class ManualCodeSyncProviderTests
{
    private static SpellDataService LoadFixtureSpellData()
    {
        var service = new SpellDataService(new TestPluginLog());
        service.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SpellData"));
        return service;
    }

    [Fact]
    public void ExportThenImport_RoundTripsCharacterNameAndLearnedSpells()
    {
        var dataService = LoadFixtureSpellData();
        var provider = new ManualCodeSyncProvider(dataService);
        var status = new PlayerSpellStatus { CharacterName = "Beispielname", LearnedSpellIds = [1, 3] };

        var code = provider.ExportToCode(status);
        provider.ImportCode(code);

        var imported = Assert.Single(provider.GetKnownPartyStatus());
        Assert.Equal("Beispielname", imported.CharacterName);
        Assert.Equal(new HashSet<uint> { 1, 3 }, imported.LearnedSpellIds);
    }

    [Theory]
    [InlineData("Y'shtola Rhul")]
    [InlineData("Söldnerin Ärger")]
    [InlineData("プレイヤー名")]
    [InlineData("Name With Spaces")]
    public void ExportThenImport_PreservesSpecialCharactersInCharacterName(string characterName)
    {
        var dataService = LoadFixtureSpellData();
        var provider = new ManualCodeSyncProvider(dataService);
        var status = new PlayerSpellStatus { CharacterName = characterName, LearnedSpellIds = [2] };

        var code = provider.ExportToCode(status);
        provider.ImportCode(code);

        var imported = Assert.Single(provider.GetKnownPartyStatus());
        Assert.Equal(characterName, imported.CharacterName);
    }

    [Fact]
    public void ExportToCode_NameTooLongForByteLengthPrefix_Throws()
    {
        var dataService = LoadFixtureSpellData();
        var provider = new ManualCodeSyncProvider(dataService);
        // Der Namens-Präfix ist EIN Byte (max. 255) - 300 ASCII-Zeichen sprengen das absichtlich.
        var status = new PlayerSpellStatus { CharacterName = new string('a', 300), LearnedSpellIds = [] };

        Assert.Throws<InvalidOperationException>(() => provider.ExportToCode(status));
    }

    [Fact]
    public void ImportCode_UnknownPrefix_ThrowsFormatException()
    {
        var dataService = LoadFixtureSpellData();
        var provider = new ManualCodeSyncProvider(dataService);

        Assert.Throws<FormatException>(() => provider.ImportCode("NOPE:abc"));
    }

    [Fact]
    public void ImportCode_InvalidBase64_ThrowsFormatException()
    {
        var dataService = LoadFixtureSpellData();
        var provider = new ManualCodeSyncProvider(dataService);

        Assert.Throws<FormatException>(() => provider.ImportCode("BLU:not-valid-base64!!!"));
    }

    [Fact]
    public void ImportCode_AlwaysMarksImportedPlayerAsNotLocal()
    {
        var dataService = LoadFixtureSpellData();
        var provider = new ManualCodeSyncProvider(dataService);
        var localStatus = new PlayerSpellStatus { CharacterName = "Wer", LearnedSpellIds = [], IsLocalPlayer = true };
        var code = provider.ExportToCode(localStatus);

        provider.ImportCode(code);

        var imported = Assert.Single(provider.GetKnownPartyStatus());
        Assert.False(imported.IsLocalPlayer);
    }

    [Fact]
    public void ImportCode_SameCharacterNameTwice_OverwritesInsteadOfDuplicating()
    {
        var dataService = LoadFixtureSpellData();
        var provider = new ManualCodeSyncProvider(dataService);
        var first = new PlayerSpellStatus { CharacterName = "Alice", LearnedSpellIds = [1] };
        var second = new PlayerSpellStatus { CharacterName = "Alice", LearnedSpellIds = [1, 2, 4] };

        provider.ImportCode(provider.ExportToCode(first));
        provider.ImportCode(provider.ExportToCode(second));

        var imported = Assert.Single(provider.GetKnownPartyStatus());
        Assert.Equal(new HashSet<uint> { 1, 2, 4 }, imported.LearnedSpellIds);
    }

    [Fact]
    public void RemovePlayer_RemovesOnlyTheSpecifiedCharacter()
    {
        var dataService = LoadFixtureSpellData();
        var provider = new ManualCodeSyncProvider(dataService);
        provider.PublishLocalStatus(new PlayerSpellStatus { CharacterName = "Alice", LearnedSpellIds = [] });
        provider.PublishLocalStatus(new PlayerSpellStatus { CharacterName = "Bob", LearnedSpellIds = [] });

        provider.RemovePlayer("Alice");

        var remaining = Assert.Single(provider.GetKnownPartyStatus());
        Assert.Equal("Bob", remaining.CharacterName);
    }

    [Fact]
    public void ExportToCode_EmptyLearnedSpells_RoundTripsToEmptySet()
    {
        var dataService = LoadFixtureSpellData();
        var provider = new ManualCodeSyncProvider(dataService);
        var status = new PlayerSpellStatus { CharacterName = "Niemand", LearnedSpellIds = [] };

        provider.ImportCode(provider.ExportToCode(status));

        var imported = Assert.Single(provider.GetKnownPartyStatus());
        Assert.Empty(imported.LearnedSpellIds);
    }

    [Fact]
    public void ExportToCode_AllKnownSpellsLearned_RoundTripsToFullSet()
    {
        var dataService = LoadFixtureSpellData();
        var provider = new ManualCodeSyncProvider(dataService);
        var allIds = dataService.OrderedSpellIds.ToHashSet();
        var status = new PlayerSpellStatus { CharacterName = "Alle", LearnedSpellIds = allIds };

        provider.ImportCode(provider.ExportToCode(status));

        var imported = Assert.Single(provider.GetKnownPartyStatus());
        Assert.Equal(allIds, imported.LearnedSpellIds);
    }
}
