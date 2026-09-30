using BLUnion.Models;
using BLUnion.Services;
using Xunit;

namespace BLUnion.Tests;

// Logik hinter den Duty-Finder-/Karten-Knöpfen (LocationActions) - ohne Dalamud-Runtime.
public class LocationActionsTests
{
    private static Location CreateLocation(
        string? coordinates = null, string? dutyName = null, uint? territoryTypeId = 148, uint? mapId = 4,
        uint? cfcId = null, IReadOnlyList<uint>? cfcIds = null) => new()
    {
        Id = 7,
        ZoneNameDe = "Tiefer Wald",
        ZoneNameEn = "Central Shroud",
        ZoneNameFr = "Forêt centrale",
        ZoneNameJa = "黒衣森：中央森林",
        Coordinates = coordinates,
        DutyName = dutyName,
        TerritoryTypeId = territoryTypeId,
        MapId = mapId,
        ContentFinderConditionId = cfcId,
        ContentFinderConditionIds = cfcIds,
    };

    // ----- Koordinaten-Parser -----

    [Theory]
    [InlineData("9, 21", 9f, 21f)]
    [InlineData("9,21", 9f, 21f)]
    [InlineData(" 9 ,  21 ", 9f, 21f)]
    [InlineData("9.5, 21.3", 9.5f, 21.3f)]
    [InlineData("9.5,21", 9.5f, 21f)]
    [InlineData("9 21", 9f, 21f)]
    [InlineData("9/21", 9f, 21f)]
    [InlineData("X: 9.5 Y: 21.3", 9.5f, 21.3f)]
    [InlineData("x:9, y:21", 9f, 21f)]
    public void Coordinates_AreParsed(string text, float expectedX, float expectedY)
    {
        Assert.True(LocationActions.TryParseCoordinates(text, out var x, out var y));
        Assert.Equal(expectedX, x, 3);
        Assert.Equal(expectedY, y, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("9")]
    [InlineData("9, 21, 5")]
    [InlineData("9,5, 21,3")]
    [InlineData("north of Bentbranch")]
    [InlineData("0, 21")]
    [InlineData("9, 99")]
    [InlineData("-9, 21")]
    public void UnparsableCoordinates_AreRejected(string? text)
    {
        Assert.False(LocationActions.TryParseCoordinates(text, out _, out _));
    }

    // ----- Auswahl der Knöpfe -----

    [Fact]
    public void OpenWorldWithCoordinates_GetsMapLink_AndNoDutyButton()
    {
        var actions = LocationActions.Get(CreateLocation(coordinates: "22, 22"));

        Assert.Empty(actions.DutyIds);
        Assert.Equal(new MapLinkTarget(148, 4, 22f, 22f), actions.MapLink);
        Assert.False(actions.UnparsableCoordinates);
    }

    [Fact]
    public void OpenWorldWithoutCoordinates_GetsNoButtons()
    {
        var actions = LocationActions.Get(CreateLocation());

        Assert.Empty(actions.DutyIds);
        Assert.Null(actions.MapLink);
        Assert.False(actions.UnparsableCoordinates);
    }

    [Fact]
    public void UnparsableCoordinates_GiveNoMapLink_ButAreFlagged()
    {
        var actions = LocationActions.Get(CreateLocation(coordinates: "irgendwo im Wald"));

        Assert.Null(actions.MapLink);
        Assert.True(actions.UnparsableCoordinates);
    }

    [Theory]
    [InlineData(null, 4u)]
    [InlineData(148u, null)]
    public void CoordinatesWithoutTerritoryOrMap_GiveNoMapLink(uint? territoryTypeId, uint? mapId)
    {
        var actions = LocationActions.Get(CreateLocation(coordinates: "22, 22", territoryTypeId: territoryTypeId, mapId: mapId));

        Assert.Null(actions.MapLink);
        Assert.False(actions.UnparsableCoordinates);
    }

    [Fact]
    public void SingleDuty_GetsOneDutyButton_AndNoMapLink()
    {
        var actions = LocationActions.Get(CreateLocation(dutyName: "Thornmarch (Hard)", cfcId: 66));

        Assert.Equal([66u], actions.DutyIds);
        Assert.False(actions.IsMultiDuty);
        Assert.Null(actions.MapLink);
    }

    [Fact]
    public void MultiDuty_GetsOneButtonPerVariant_InDataOrder()
    {
        var actions = LocationActions.Get(CreateLocation(dutyName: "Thornmarch (Hard oder Extreme)", cfcIds: [66, 67]));

        Assert.Equal([66u, 67u], actions.DutyIds);
        Assert.True(actions.IsMultiDuty);
        Assert.Null(actions.MapLink);
    }

    [Fact]
    public void Variants_TakePrecedenceOverSingleDuty()
    {
        var actions = LocationActions.Get(CreateLocation(dutyName: "x", cfcId: 1, cfcIds: [93, 94]));

        Assert.Equal([93u, 94u], actions.DutyIds);
    }

    [Fact]
    public void DutyWithoutIds_GetsNoButtons()
    {
        var actions = LocationActions.Get(CreateLocation(dutyName: "Masked Carnivale: 20", territoryTypeId: null, mapId: null));

        Assert.Empty(actions.DutyIds);
        Assert.Null(actions.MapLink);
    }

    [Fact]
    public void NoLocation_GetsNoButtons()
    {
        var actions = LocationActions.Get(null);

        Assert.Empty(actions.DutyIds);
        Assert.Null(actions.MapLink);
    }

    // ----- Beschriftung der Varianten-Knöpfe -----

    [Fact]
    public void VariantNames_AreShortenedToTheDifference()
    {
        Assert.Equal(["Hard", "Extreme"], LocationActions.ShortenVariantNames(["Thornmarch (Hard)", "Thornmarch (Extreme)"]));
        Assert.Equal(["schwer", "extrem"], LocationActions.ShortenVariantNames(["Dornmarsch (schwer)", "Dornmarsch (extrem)"]));
    }

    [Fact]
    public void VariantNames_KeepTheWordAfterTheLastSeparator()
    {
        Assert.Equal(
            ["Turn 1", "Turn 2"],
            LocationActions.ShortenVariantNames(["The Binding Coil of Bahamut - Turn 1", "The Binding Coil of Bahamut - Turn 2"]));
    }

    [Fact]
    public void BaseVariant_BecomesNull()
    {
        Assert.Equal(
            [null, "Hard", "Extreme"],
            LocationActions.ShortenVariantNames(["The Bowl of Embers", "The Bowl of Embers (Hard)", "The Bowl of Embers (Extreme)"]));
    }

    [Fact]
    public void VariantNames_WithoutSeparator_StayFull()
    {
        string[] names = ["善王モグル・モグXII世討滅戦", "極王モグル・モグXII世討滅戦"];

        Assert.Equal(names, LocationActions.ShortenVariantNames(names));
    }

    [Fact]
    public void SingleName_StaysFull()
    {
        Assert.Equal(["Thornmarch (Hard)"], LocationActions.ShortenVariantNames(["Thornmarch (Hard)"]));
    }
}
