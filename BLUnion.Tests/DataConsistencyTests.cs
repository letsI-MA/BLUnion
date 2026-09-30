using BLUnion.Models;
using BLUnion.Services;
using BLUnion.Tests.Fakes;
using Xunit;

namespace BLUnion.Tests;

// Plausibilitätsregeln für die ECHTEN Data/*.json (siehe TestData-Link in BLUnion.Tests.csproj) -
// fangen Datenfehler wie "Duty-Boss als OpenWorld/Levequest markiert" ab.
public class DataConsistencyTests
{
    private static SpellDataService LoadRealData()
    {
        var service = new SpellDataService(new TestPluginLog());
        service.Load(Path.Combine(AppContext.BaseDirectory, "TestData"));
        return service;
    }

    [Fact]
    public void OpenWorldAndLevequestSources_NeverPointToADuty()
    {
        var data = LoadRealData();
        Assert.NotEmpty(data.Sources);

        var violations = data.Sources
            .Where(source => source.Method is SourceMethod.OpenWorld or SourceMethod.Levequest)
            .Select(source =>
            {
                var monster = data.Monsters.GetValueOrDefault(source.MonsterId);
                var location = monster is null ? null : data.Locations.GetValueOrDefault(monster.LocationId);
                return (Source: source, Monster: monster, Location: location);
            })
            .Where(entry => entry.Location is { } location
                && (location.DutyName is not null || location.ContentFinderConditionId is not null))
            .Select(entry =>
                $"Spell {entry.Source.SpellId} / Monster {entry.Source.MonsterId} \"{entry.Monster!.NameEn}\" ({entry.Source.Method}) " +
                $"-> Location {entry.Location!.Id} \"{entry.Location.DutyName ?? entry.Location.ZoneNameEn}\"")
            .ToList();

        Assert.True(violations.Count == 0, "Quellen mit Duty-Location:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void EverySpellExceptTheStartingSpell_HasAtLeastOneSource()
    {
        var data = LoadRealData();

        var startingSpellIds = data.Sources
            .Where(source => source.Method == SourceMethod.StartingSpell)
            .Select(source => source.SpellId)
            .ToHashSet();

        var withoutSource = data.Spells.Values
            .Where(spell => !startingSpellIds.Contains(spell.Id))
            .Where(spell => !data.Sources.Any(source => source.SpellId == spell.Id))
            .Select(spell => $"#{spell.SpellbookOrder} {spell.NameEn} ({spell.Id})")
            .ToList();

        Assert.NotEmpty(data.Spells);
        Assert.True(withoutSource.Count == 0, "Spells ohne Quelle:\n" + string.Join("\n", withoutSource));
    }

    [Fact]
    public void ContentFinderConditionIds_OnlyOnDutyLocations_WithSeveralDistinctVariants()
    {
        var data = LoadRealData();

        var multiDuty = data.Locations.Values.Where(location => location.ContentFinderConditionIds is not null).ToList();
        Assert.NotEmpty(multiDuty);

        var violations = multiDuty
            .Where(location => location.DutyName is null
                || location.DutyName.StartsWith("Masked Carnivale", StringComparison.OrdinalIgnoreCase)
                || location.ContentFinderConditionId is not null
                || location.ContentFinderConditionIds!.Count < 2
                || location.ContentFinderConditionIds.Distinct().Count() != location.ContentFinderConditionIds.Count)
            .Select(location => $"Location {location.Id} \"{location.DutyName ?? location.ZoneNameEn}\"")
            .ToList();

        Assert.True(violations.Count == 0, "Ungültige ContentFinderConditionIds:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void AllCoordinates_AreParsable_AndOpenWorldLocationsWithCoordinatesGetAMapLink()
    {
        var data = LoadRealData();

        var violations = data.Locations.Values
            .Where(location => location.Coordinates is not null)
            .Where(location => location.DutyName is not null || LocationActions.Get(location).MapLink is null)
            .Select(location => $"Location {location.Id} \"{location.ZoneNameEn}\" ({location.Coordinates})")
            .ToList();

        Assert.True(violations.Count == 0, "Koordinaten ohne Karten-Link:\n" + string.Join("\n", violations));
    }
}
