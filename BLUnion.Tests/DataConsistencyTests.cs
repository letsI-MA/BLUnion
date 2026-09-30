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
}
