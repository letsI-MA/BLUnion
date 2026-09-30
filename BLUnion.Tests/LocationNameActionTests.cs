using BLUnion.Models;
using BLUnion.Services;
using Xunit;

namespace BLUnion.Tests;

// Klickbare Orts-/Duty-Namen (LocationActions.GetNameAction/GetVariantMenu), Aetheryten-Auswahl
// (AetheryteSelector) und das Format der Quellzeilen - ohne Dalamud-Runtime.
public class LocationNameActionTests
{
    private const uint CentralShroud = 148;
    private const uint NorthernThanalan = 147;

    // Positionen wie im MapMarker-Sheet (Stand 2026.09.15).
    private static readonly AetheryteCandidate BentbranchMeadows = new(3, CentralShroud, AetheryteKind.Aetheryte, 21.8f, 22.2f);
    private static readonly AetheryteCandidate BlueBadgerGate = new(31, CentralShroud, AetheryteKind.AethernetShard, null, null);
    private static readonly AetheryteCandidate CampBluefog = new(21, NorthernThanalan, AetheryteKind.Aetheryte, 21.9f, 30.6f);
    private static readonly AetheryteCandidate CeruleumPlant = new(22, NorthernThanalan, AetheryteKind.Aetheryte, 21.0f, 20.9f);

    private static readonly AetheryteCandidate[] Aetherytes = [BentbranchMeadows, BlueBadgerGate, CampBluefog, CeruleumPlant];

    private static Location CreateOpenWorld(string? coordinates = null, uint territoryTypeId = CentralShroud) => new()
    {
        Id = 109,
        ZoneNameDe = "Tiefer Wald",
        ZoneNameEn = "Central Shroud",
        ZoneNameFr = "Forêt centrale",
        ZoneNameJa = "黒衣森：中央森林",
        Coordinates = coordinates,
        TerritoryTypeId = territoryTypeId,
        MapId = 4,
    };

    private static Location CreateDuty(uint? cfcId = null, IReadOnlyList<uint>? cfcIds = null) => new()
    {
        Id = 58,
        ZoneNameDe = "Thornmarch (Hard oder Extreme)",
        ZoneNameEn = "Thornmarch (Hard oder Extreme)",
        ZoneNameFr = "Thornmarch (Hard oder Extreme)",
        ZoneNameJa = "Thornmarch (Hard oder Extreme)",
        DutyName = "Thornmarch (Hard oder Extreme)",
        TerritoryTypeId = 202,
        ContentFinderConditionId = cfcId,
        ContentFinderConditionIds = cfcIds,
    };

    // ----- Aetheryten-Auswahl -----

    [Fact]
    public void WithoutCoordinates_TheMainAetheryteIsChosen()
    {
        Assert.Equal(CampBluefog, AetheryteSelector.Select(Aetherytes, NorthernThanalan, null));
        Assert.Equal(BentbranchMeadows, AetheryteSelector.Select(Aetherytes, CentralShroud, null));
    }

    [Fact]
    public void WithCoordinates_TheNearestAetheryteOfTheZoneIsChosen()
    {
        Assert.Equal(CeruleumPlant, AetheryteSelector.Select(Aetherytes, NorthernThanalan, new MapLinkTarget(NorthernThanalan, 24, 20f, 22f)));
        Assert.Equal(CampBluefog, AetheryteSelector.Select(Aetherytes, NorthernThanalan, new MapLinkTarget(NorthernThanalan, 24, 22f, 29f)));
    }

    [Fact]
    public void ShardsAndHousingAetherytes_AreNeverChosen_EvenIfCloser()
    {
        AetheryteCandidate[] candidates =
        [
            BentbranchMeadows,
            BlueBadgerGate with { MapX = 15f, MapY = 21f },
            new(56, CentralShroud, AetheryteKind.Housing, 15f, 21f),
        ];

        Assert.Equal(BentbranchMeadows, AetheryteSelector.SelectNearest(candidates, CentralShroud, 15f, 21f));
        Assert.Equal(BentbranchMeadows, AetheryteSelector.SelectMain(candidates, CentralShroud));
    }

    [Fact]
    public void ZoneWithoutRealAetheryte_GivesNone()
    {
        AetheryteCandidate[] candidates = [BlueBadgerGate, new(56, 339, AetheryteKind.Housing, 10f, 10f)];

        Assert.Null(AetheryteSelector.SelectMain(candidates, CentralShroud));
        Assert.Null(AetheryteSelector.SelectNearest(candidates, CentralShroud, 15f, 21f));
        Assert.Null(AetheryteSelector.Select(Aetherytes, 9999, null));
    }

    // ----- Ausweich-Aetheryt für Zonen ohne eigenen -----

    private const uint DravanianHinterlands = 399;
    private const uint Idyllshire = 478;

    private static readonly AetheryteCandidate IdyllshireAetheryte = new(75, Idyllshire, AetheryteKind.Aetheryte, 7.2f, 5.1f);

    [Fact]
    public void DravanianHinterlands_FallsBackToIdyllshire()
    {
        Assert.Equal(Idyllshire, AetheryteSelector.FallbackTerritories[DravanianHinterlands]);

        AetheryteCandidate[] candidates =
        [
            .. Aetherytes,
            IdyllshireAetheryte,
            new(90, Idyllshire, AetheryteKind.AethernetShard, null, null),
            new(91, DravanianHinterlands, AetheryteKind.AethernetShard, 11.9f, 12.8f),
        ];

        Assert.Equal(IdyllshireAetheryte, AetheryteSelector.Select(candidates, DravanianHinterlands, null));

        // Mit Koordinaten des Monsters: trotzdem Idyllshire (Koordinaten gehören zur Karte der Hinterlands).
        Assert.Equal(IdyllshireAetheryte, AetheryteSelector.Select(candidates, DravanianHinterlands, new MapLinkTarget(DravanianHinterlands, 213, 12f, 13f)));
    }

    [Fact]
    public void Fallback_WithoutAetheryteInTheFallbackZone_GivesNone()
    {
        Assert.Null(AetheryteSelector.Select(Aetherytes, DravanianHinterlands, null));
    }

    [Fact]
    public void ZoneWithOwnAetheryte_NeverUsesAFallback()
    {
        Assert.DoesNotContain(CentralShroud, AetheryteSelector.FallbackTerritories.Keys);
        Assert.DoesNotContain(NorthernThanalan, AetheryteSelector.FallbackTerritories.Keys);
    }

    [Fact]
    public void WithoutKnownPositions_NearestFallsBackToTheMainAetheryte()
    {
        AetheryteCandidate[] candidates = [CampBluefog with { MapX = null, MapY = null }, CeruleumPlant with { MapX = null, MapY = null }];

        Assert.Equal(21u, AetheryteSelector.SelectNearest(candidates, NorthernThanalan, 21f, 21f)!.Id);
    }

    // ----- Name klickbar / nicht klickbar -----

    [Fact]
    public void OpenWorld_WithUnlockedAetheryte_Teleports()
    {
        var action = LocationActions.GetNameAction(CreateOpenWorld(), teleportEnabled: true, BentbranchMeadows, aetheryteUnlocked: true);

        Assert.Equal(LocationNameKind.Teleport, action.Kind);
        Assert.Equal(BentbranchMeadows, action.Aetheryte);
        Assert.True(action.IsClickable);
    }

    [Fact]
    public void OpenWorld_WithUnknownUnlockState_StaysClickable()
    {
        var action = LocationActions.GetNameAction(CreateOpenWorld(), teleportEnabled: true, BentbranchMeadows, aetheryteUnlocked: null);

        Assert.Equal(LocationNameKind.Teleport, action.Kind);
    }

    [Fact]
    public void OpenWorld_WithLockedAetheryte_IsNotClickable()
    {
        var action = LocationActions.GetNameAction(CreateOpenWorld(), teleportEnabled: true, BentbranchMeadows, aetheryteUnlocked: false);

        Assert.Equal(LocationNameKind.TeleportLocked, action.Kind);
        Assert.False(action.IsClickable);
    }

    [Fact]
    public void OpenWorld_WithSettingOff_IsPlainText()
    {
        var action = LocationActions.GetNameAction(CreateOpenWorld("15, 21"), teleportEnabled: false, BentbranchMeadows, aetheryteUnlocked: true);

        Assert.Equal(LocationNameKind.PlainText, action.Kind);
        Assert.False(action.IsClickable);

        // Map-Icons bleiben unabhängig von der Einstellung.
        Assert.NotNull(LocationActions.Get(CreateOpenWorld("15, 21")).MapLink);
    }

    [Fact]
    public void OpenWorld_WithoutAetheryte_IsPlainText()
    {
        Assert.Equal(LocationNameKind.PlainText, LocationActions.GetNameAction(CreateOpenWorld(), true, null, null).Kind);
        Assert.Equal(LocationNameKind.PlainText, LocationActions.GetNameAction(null, true, BentbranchMeadows, true).Kind);
    }

    [Fact]
    public void SingleDuty_OpensTheDutyFinder_IndependentOfTheTeleportSetting()
    {
        foreach (var teleportEnabled in new[] { true, false })
        {
            var action = LocationActions.GetNameAction(CreateDuty(cfcId: 66), teleportEnabled, BentbranchMeadows, true, singleDutyUnlocked: true);

            Assert.Equal(LocationNameKind.DutyFinder, action.Kind);
            Assert.Equal([66u], action.DutyIds);
            Assert.Null(action.Aetheryte);
        }
    }

    [Fact]
    public void LockedSingleDuty_IsNotClickable()
    {
        var action = LocationActions.GetNameAction(CreateDuty(cfcId: 66), true, null, null, singleDutyUnlocked: false);

        Assert.Equal(LocationNameKind.DutyFinderLocked, action.Kind);
        Assert.False(action.IsClickable);
    }

    [Fact]
    public void DutyWithoutIds_IsNeverATeleport()
    {
        var action = LocationActions.GetNameAction(CreateDuty(), true, BentbranchMeadows, true);

        Assert.Equal(LocationNameKind.PlainText, action.Kind);
    }

    // ----- Varianten-Menü -----

    [Fact]
    public void MultiDuty_OpensTheVariantMenu()
    {
        var action = LocationActions.GetNameAction(CreateDuty(cfcIds: [66, 67]), true, null, null);

        Assert.Equal(LocationNameKind.DutyFinderMenu, action.Kind);
        Assert.Equal([66u, 67u], action.DutyIds);
        Assert.True(action.IsClickable);
    }

    [Fact]
    public void VariantMenu_HasShortLabels_AndGreysOutLockedVariants()
    {
        var items = LocationActions.GetVariantMenu(
            [66, 67], ["Thornmarch (Hard)", "Thornmarch (Extreme)"], cfcId => cfcId == 66 ? true : false, "Normal", n => $"Variante {n}");

        Assert.Equal(
            [
                new DutyVariantMenuItem(66, "Hard", "Thornmarch (Hard)", true),
                new DutyVariantMenuItem(67, "Extreme", "Thornmarch (Extreme)", false),
            ],
            items);
    }

    [Fact]
    public void VariantMenu_UnknownUnlockState_StaysEnabled_BaseVariantIsNormal()
    {
        var items = LocationActions.GetVariantMenu(
            [56, 59, 63], ["The Bowl of Embers", "The Bowl of Embers (Hard)", "The Bowl of Embers (Extreme)"], _ => null, "Normal", n => $"Variante {n}");

        Assert.Equal(["Normal", "Hard", "Extreme"], items.Select(item => item.Label));
        Assert.All(items, item => Assert.True(item.Enabled));
    }

    [Fact]
    public void VariantMenu_MissingGameName_UsesTheFallback()
    {
        var items = LocationActions.GetVariantMenu([93, 94], [null, ""], _ => true, "Normal", n => $"Variante {n}");

        Assert.Equal(["Variante 1", "Variante 2"], items.Select(item => item.FullName));
    }

    // ----- Quellzeile: Ort - Monster (Methode), kein "Quelle" -----

    [Fact]
    public void SourceLine_German_IsLocationMonsterMethod()
    {
        var line = UiStrings.Format(UiStrings.Key.TooltipSourceLine, DisplayLanguage.German, "Nördliches Thanalan", "Magitek Vanguard H-2", "Open World");

        Assert.Equal("Nördliches Thanalan - Magitek Vanguard H-2 (Open World)", line);
        Assert.DoesNotContain("Quelle", line);
    }

    [Fact]
    public void SourceLine_English_IsLocationMonsterMethod()
    {
        var line = UiStrings.Format(UiStrings.Key.TooltipSourceLine, DisplayLanguage.English, "Northern Thanalan", "Magitek Vanguard H-2", "Open World");

        Assert.Equal("Northern Thanalan - Magitek Vanguard H-2 (Open World)", line);
        Assert.DoesNotContain("Source", line);
    }

    // Spellbook-Detail zeichnet Ort (klickbar) + Rest getrennt - muss in jeder Sprache dieselbe Zeile ergeben.
    [Theory]
    [InlineData(DisplayLanguage.German)]
    [InlineData(DisplayLanguage.English)]
    [InlineData(DisplayLanguage.French)]
    [InlineData(DisplayLanguage.Japanese)]
    public void SourceLine_SplitIntoLocationAndRest_MatchesTheFullLine(DisplayLanguage language)
    {
        var full = UiStrings.Format(UiStrings.Key.TooltipSourceLine, language, "ORT", "MONSTER", "METHODE");
        var split = "ORT " + UiStrings.Format(UiStrings.Key.SourceLineAfterLocationFormat, language, "MONSTER", "METHODE");

        Assert.Equal(full, split);
        Assert.True(full.IndexOf("ORT", StringComparison.Ordinal) < full.IndexOf("MONSTER", StringComparison.Ordinal));
        Assert.True(full.IndexOf("MONSTER", StringComparison.Ordinal) < full.IndexOf("METHODE", StringComparison.Ordinal));
    }
}
