using BLUnion.Models;
using BLUnion.Services;
using BLUnion.Tests.Fakes;
using Lumina.Text;
using Lumina.Text.Payloads;
using Xunit;

namespace BLUnion.Tests;

// Fallback-Kette/Cache von GameTextService mit gefaktem Text-Provider und die SeString-Bereinigung
// (GameTextCleaner) - beides ohne Dalamud-Runtime.
public class GameTextServiceTests
{
    private sealed class FakeGameTextSource : IGameTextSource
    {
        public Dictionary<(string Kind, uint Id, DisplayLanguage Language), string?> Texts { get; } = [];

        public Dictionary<uint, uint> TerritoryPlaceNames { get; } = [];

        public int Calls { get; private set; }

        public string? GetActionName(uint actionId, DisplayLanguage language) => this.Get("action", actionId, language);

        public string? GetAozActionDescription(uint aozActionId, DisplayLanguage language) => this.Get("aoz", aozActionId, language);

        public string? GetBNpcName(uint bnpcNameId, DisplayLanguage language) => this.Get("bnpc", bnpcNameId, language);

        public string? GetPlaceName(uint placeNameId, DisplayLanguage language) => this.Get("place", placeNameId, language);

        public uint? GetTerritoryPlaceNameId(uint territoryTypeId) =>
            this.TerritoryPlaceNames.TryGetValue(territoryTypeId, out var id) ? id : null;

        public string? GetContentFinderConditionName(uint contentFinderConditionId, DisplayLanguage language) =>
            this.Get("cfc", contentFinderConditionId, language);

        private string? Get(string kind, uint id, DisplayLanguage language)
        {
            this.Calls++;
            return this.Texts.GetValueOrDefault((kind, id, language));
        }
    }

    private static Spell CreateSpell(uint? actionId = 11383, uint? aozActionId = 1, string? description = "Deutsche Beschreibung") => new()
    {
        Id = 11383,
        NameDe = "Nieser",
        NameEn = "Snort",
        NameFr = "",
        NameJa = "鼻息",
        Description = description,
        ActionId = actionId,
        AozActionId = aozActionId,
    };

    private static Monster CreateMonster(uint? bnpcNameId = 7) => new()
    {
        Id = 1,
        NameDe = "Mudpuppy",
        NameEn = "Mudpuppy",
        NameFr = "Mudpuppy",
        NameJa = "Mudpuppy",
        BNpcNameId = bnpcNameId,
    };

    private static Location CreateLocation(uint? placeNameId = null, uint? territoryTypeId = null, uint? cfcId = null) => new()
    {
        Id = 4,
        ZoneNameDe = "Pharos Sirius",
        ZoneNameEn = "Pharos Sirius",
        ZoneNameFr = "Le Phare de Sirius",
        ZoneNameJa = "",
        DutyName = "Pharos Sirius",
        PlaceNameId = placeNameId,
        TerritoryTypeId = territoryTypeId,
        ContentFinderConditionId = cfcId,
    };

    // ----- Fallback-Kette -----

    [Fact]
    public void GameText_WinsOverJson()
    {
        var source = new FakeGameTextSource();
        source.Texts[("action", 11383, DisplayLanguage.French)] = "Souffle nasal";

        Assert.Equal("Souffle nasal", new GameTextService(source).GetSpellName(CreateSpell(), DisplayLanguage.French));
    }

    [Fact]
    public void MissingGameText_FallsBackToJsonOfLanguage()
    {
        var service = new GameTextService(new FakeGameTextSource());

        Assert.Equal("Nieser", service.GetSpellName(CreateSpell(), DisplayLanguage.German));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyGameTextAndEmptyJson_FallBackToEnglishJson(string? gameText)
    {
        var source = new FakeGameTextSource();
        source.Texts[("action", 11383, DisplayLanguage.French)] = gameText;

        // NameFr ist im Fixture leer -> englischer JSON-Text statt leerer Anzeige.
        Assert.Equal("Snort", new GameTextService(source).GetSpellName(CreateSpell(), DisplayLanguage.French));
    }

    [Fact]
    public void WithoutGameId_TheSourceIsNotAsked()
    {
        var source = new FakeGameTextSource();
        var service = new GameTextService(source);

        Assert.Equal("Nieser", service.GetSpellName(CreateSpell(actionId: null), DisplayLanguage.German));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void Results_AreCachedPerEntryAndLanguage()
    {
        var source = new FakeGameTextSource();
        source.Texts[("bnpc", 7, DisplayLanguage.German)] = "Schlammwelpe";
        var service = new GameTextService(source);
        var monster = CreateMonster();

        for (var i = 0; i < 100; i++)
            service.GetMonsterName(monster, DisplayLanguage.German);
        service.GetMonsterName(monster, DisplayLanguage.English);

        Assert.Equal(2, source.Calls); // je Sprache genau ein Lookup
    }

    [Fact]
    public void MonsterName_IsCapitalized_ExceptJapanese()
    {
        var source = new FakeGameTextSource();
        source.Texts[("bnpc", 7, DisplayLanguage.English)] = "mudpuppy";
        source.Texts[("bnpc", 7, DisplayLanguage.Japanese)] = "マッドパピー";
        var service = new GameTextService(source);

        Assert.Equal("Mudpuppy", service.GetMonsterName(CreateMonster(), DisplayLanguage.English));
        Assert.Equal("マッドパピー", service.GetMonsterName(CreateMonster(), DisplayLanguage.Japanese));
    }

    [Fact]
    public void ZoneName_PrefersDutyNameOverPlaceName()
    {
        var source = new FakeGameTextSource();
        source.Texts[("cfc", 30, DisplayLanguage.French)] = "le Phare de Sirius";
        source.Texts[("place", 99, DisplayLanguage.French)] = "Autre lieu";

        var name = new GameTextService(source).GetZoneName(CreateLocation(placeNameId: 99, cfcId: 30), DisplayLanguage.French);

        Assert.Equal("Le Phare de Sirius", name);
    }

    [Fact]
    public void ZoneName_UsesPlaceName_ThenTerritoryTypePlaceName()
    {
        var source = new FakeGameTextSource();
        source.Texts[("place", 99, DisplayLanguage.German)] = "Oberes La Noscea";
        source.TerritoryPlaceNames[180] = 99;
        var service = new GameTextService(source);

        Assert.Equal("Oberes La Noscea", service.GetZoneName(CreateLocation(placeNameId: 99), DisplayLanguage.German));
        Assert.Equal("Oberes La Noscea", new GameTextService(source).GetZoneName(CreateLocation(territoryTypeId: 180), DisplayLanguage.German));
    }

    [Fact]
    public void ZoneName_FallsBackToJson_WhenDutyTextIsEmpty()
    {
        var source = new FakeGameTextSource();
        source.Texts[("cfc", 30, DisplayLanguage.Japanese)] = "";

        // ZoneNameJa ist leer -> englischer JSON-Text
        Assert.Equal("Pharos Sirius", new GameTextService(source).GetZoneName(CreateLocation(cfcId: 30), DisplayLanguage.Japanese));
    }

    [Fact]
    public void Description_FromGame_KeepsLineBreaksAndIsNotCapitalized()
    {
        var source = new FakeGameTextSource();
        source.Texts[("aoz", 1, DisplayLanguage.English)] = "typhon's own.\nA fierce snort.";

        Assert.Equal("typhon's own.\nA fierce snort.", new GameTextService(source).GetSpellDescription(CreateSpell(), DisplayLanguage.English));
    }

    [Fact]
    public void Description_FallsBackToGermanJson_ForEveryLanguage()
    {
        var service = new GameTextService(new FakeGameTextSource());

        Assert.Equal("Deutsche Beschreibung", service.GetSpellDescription(CreateSpell(), DisplayLanguage.Japanese));
    }

    [Fact]
    public void Description_IsNull_WithoutAnyText()
    {
        var service = new GameTextService(new FakeGameTextSource());

        Assert.Null(service.GetSpellDescription(CreateSpell(aozActionId: null, description: null), DisplayLanguage.English));
    }

    // ----- Sammelnamen (BNpcNameIds) -----

    private static Monster CreateCombinedMonster(IReadOnlyList<uint>? bnpcNameIds, uint? bnpcNameId = null) => new()
    {
        Id = 26,
        NameDe = "Goblin-Fischer & -Glücksspieler",
        NameEn = "Goblin Fisher & Gambler",
        NameFr = "",
        NameJa = "",
        BNpcNameId = bnpcNameId,
        BNpcNameIds = bnpcNameIds,
    };

    [Fact]
    public void CombinedName_JoinsAllResolvedPartsWithSlash()
    {
        var source = new FakeGameTextSource();
        source.Texts[("bnpc", 367, DisplayLanguage.German)] = "Goblin-Fischer";
        source.Texts[("bnpc", 3099, DisplayLanguage.German)] = "Goblin-Glücksspieler";

        var name = new GameTextService(source).GetMonsterName(CreateCombinedMonster([367, 3099]), DisplayLanguage.German);

        Assert.Equal("Goblin-Fischer / Goblin-Glücksspieler", name);
    }

    [Fact]
    public void CombinedName_CapitalizesEachPart_ExceptJapanese()
    {
        var source = new FakeGameTextSource();
        source.Texts[("bnpc", 6119, DisplayLanguage.English)] = "coeurl sruti";
        source.Texts[("bnpc", 6120, DisplayLanguage.English)] = "coeurl smriti";
        source.Texts[("bnpc", 6119, DisplayLanguage.Japanese)] = "クァール・シュルティ";
        source.Texts[("bnpc", 6120, DisplayLanguage.Japanese)] = "クァール・スムリティ";
        var service = new GameTextService(source);
        var monster = CreateCombinedMonster([6119, 6120]);

        Assert.Equal("Coeurl sruti / Coeurl smriti", service.GetMonsterName(monster, DisplayLanguage.English));
        Assert.Equal("クァール・シュルティ / クァール・スムリティ", service.GetMonsterName(monster, DisplayLanguage.Japanese));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void CombinedName_WithOneMissingPart_FallsBackToJsonOfLanguage(string? secondPart)
    {
        var source = new FakeGameTextSource();
        source.Texts[("bnpc", 367, DisplayLanguage.German)] = "Goblin-Fischer";
        source.Texts[("bnpc", 3099, DisplayLanguage.German)] = secondPart;

        var name = new GameTextService(source).GetMonsterName(CreateCombinedMonster([367, 3099]), DisplayLanguage.German);

        // Kein halber Name ("Goblin-Fischer"), sondern der vollständige JSON-Name.
        Assert.Equal("Goblin-Fischer & -Glücksspieler", name);
    }

    [Fact]
    public void CombinedName_WithMissingPartAndEmptyJson_FallsBackToEnglishJson()
    {
        var source = new FakeGameTextSource();
        source.Texts[("bnpc", 367, DisplayLanguage.French)] = "gobelin pêcheur";

        var name = new GameTextService(source).GetMonsterName(CreateCombinedMonster([367, 3099]), DisplayLanguage.French);

        Assert.Equal("Goblin Fisher & Gambler", name);
    }

    [Fact]
    public void CombinedName_TakesPrecedenceOverSingleId()
    {
        var source = new FakeGameTextSource();
        source.Texts[("bnpc", 1, DisplayLanguage.English)] = "Single";
        source.Texts[("bnpc", 367, DisplayLanguage.English)] = "Goblin Fisher";
        source.Texts[("bnpc", 3099, DisplayLanguage.English)] = "Goblin Gambler";

        var name = new GameTextService(source).GetMonsterName(CreateCombinedMonster([367, 3099], bnpcNameId: 1), DisplayLanguage.English);

        Assert.Equal("Goblin Fisher / Goblin Gambler", name);
    }

    [Fact]
    public void EmptyCombinedList_UsesSingleId()
    {
        var source = new FakeGameTextSource();
        source.Texts[("bnpc", 1, DisplayLanguage.English)] = "Single";

        Assert.Equal("Single", new GameTextService(source).GetMonsterName(CreateCombinedMonster([], bnpcNameId: 1), DisplayLanguage.English));
    }

    [Fact]
    public void CombinedName_IsCachedLikeSingleNames()
    {
        var source = new FakeGameTextSource();
        source.Texts[("bnpc", 367, DisplayLanguage.German)] = "Goblin-Fischer";
        source.Texts[("bnpc", 3099, DisplayLanguage.German)] = "Goblin-Glücksspieler";
        var service = new GameTextService(source);
        var monster = CreateCombinedMonster([367, 3099]);

        for (var i = 0; i < 50; i++)
            service.GetMonsterName(monster, DisplayLanguage.German);

        Assert.Equal(2, source.Calls); // ein Lookup je Teil, danach Cache
    }

    // ----- SeString-Bereinigung -----

    [Fact]
    public void Cleaner_KeepsTextAndNewLines_DropsColorAndItalic()
    {
        var seString = new SeStringBuilder()
            .Append("Typhons Spezialwaffe.")
            .AppendNewLine()
            .PushColorType(500)
            .Append("Ein kräftiger ")
            .PopColorType()
            .AppendSetItalic(true)
            .Append("Nieser")
            .AppendSetItalic(false)
            .Append(".")
            .ToReadOnlySeString();

        Assert.Equal("Typhons Spezialwaffe.\nEin kräftiger Nieser.", GameTextCleaner.ToPlainText(seString));
    }

    [Fact]
    public void Cleaner_DropsSoftHyphen_KeepsNonBreakingSpaceAndHyphen()
    {
        var seString = new SeStringBuilder()
            .Append("Wind")
            .BeginMacro(MacroCode.SoftHyphen).EndMacro()
            .Append("stoß")
            .BeginMacro(MacroCode.NonBreakingSpace).EndMacro()
            .Append("A")
            .BeginMacro(MacroCode.Hyphen).EndMacro()
            .Append("B")
            .ToReadOnlySeString();

        Assert.Equal("Windstoß\u00A0A-B", GameTextCleaner.ToPlainText(seString));
    }

    [Fact]
    public void Cleaner_NormalizesCrLfAndTrims()
    {
        var seString = new SeStringBuilder().Append("  Zeile 1\r\nZeile 2  ").ToReadOnlySeString();

        Assert.Equal("Zeile 1\nZeile 2", GameTextCleaner.ToPlainText(seString));
    }

    [Fact]
    public void Cleaner_EmptySeString_IsEmpty()
    {
        Assert.Equal(string.Empty, GameTextCleaner.ToPlainText(new SeStringBuilder().ToReadOnlySeString()));
    }

    // ----- Model-Deserialisierung mit/ohne neue ID-Felder -----

    [Fact]
    public void Load_WithoutIdFields_LeavesIdsNull()
    {
        var service = new SpellDataService(new TestPluginLog());
        service.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SpellData"));

        Assert.NotEmpty(service.Spells);
        Assert.All(service.Spells.Values, spell => Assert.Null(spell.AozActionId));
        Assert.All(service.Monsters.Values, monster => Assert.Null(monster.BNpcNameId));
        Assert.All(service.Locations.Values, location => Assert.Null(location.PlaceNameId));
    }

    [Fact]
    public void Load_WithIdFields_ReadsThem()
    {
        var dir = Directory.CreateTempSubdirectory("blunion-gametext-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "spells.json"), """
                [{ "Id": 11383, "NameDe": "Nieser", "NameEn": "Snort", "NameFr": "Souffle nasal", "NameJa": "鼻息",
                   "Stars": 4, "IconId": 3251, "SpellbookOrder": 25, "ActionId": 11383, "AozActionId": 1 }]
                """);
            File.WriteAllText(Path.Combine(dir, "monsters.json"), """
                [{ "Id": 1, "NameDe": "Typhon", "NameEn": "Typhon", "NameFr": "Typhon", "NameJa": "テュポーン",
                   "LocationId": 4, "BNpcNameId": 1234 },
                 { "Id": 26, "NameDe": "Goblin Fisher & Gambler", "NameEn": "Goblin Fisher & Gambler",
                   "NameFr": "Goblin Fisher & Gambler", "NameJa": "Goblin Fisher & Gambler", "LocationId": 4,
                   "BNpcNameIds": [367, 3099] }]
                """);
            File.WriteAllText(Path.Combine(dir, "locations.json"), """
                [{ "Id": 4, "ZoneNameDe": "Pharos Sirius", "ZoneNameEn": "Pharos Sirius", "ZoneNameFr": "Le Phare de Sirius",
                   "ZoneNameJa": "シリウス大灯台", "DutyName": "Pharos Sirius",
                   "TerritoryTypeId": 160, "MapId": 38, "PlaceNameId": 1040, "ContentFinderConditionId": 17 }]
                """);

            var service = new SpellDataService(new TestPluginLog());
            service.Load(dir);

            var spell = Assert.Single(service.Spells.Values);
            Assert.Equal(11383u, spell.ActionId);
            Assert.Equal(1u, spell.AozActionId);
            Assert.Equal(1234u, service.Monsters[1].BNpcNameId);
            Assert.Null(service.Monsters[1].BNpcNameIds);
            Assert.Equal([367u, 3099u], service.Monsters[26].BNpcNameIds);

            var location = Assert.Single(service.Locations.Values);
            Assert.Equal(160u, location.TerritoryTypeId);
            Assert.Equal(38u, location.MapId);
            Assert.Equal(1040u, location.PlaceNameId);
            Assert.Equal(17u, location.ContentFinderConditionId);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RealData_LoadsWithIdFields()
    {
        var service = new SpellDataService(new TestPluginLog());
        service.Load(Path.Combine(AppContext.BaseDirectory, "TestData"));

        Assert.All(service.Spells.Values, spell => Assert.NotNull(spell.AozActionId));
        Assert.Contains(service.Monsters.Values, monster => monster.BNpcNameId is not null);
        Assert.Contains(service.Locations.Values, location => location.ContentFinderConditionId is not null);
    }
}
