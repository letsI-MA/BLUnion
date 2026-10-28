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

    // Sammelnamen ("Goblin Fisher & Gambler", "Coeurl Sruti or Smriti") brauchen je Teil eine ID in
    // BNpcNameIds - eine einzelne BNpcNameId würde in der Anzeige einen der Namen verschlucken.
    [Fact]
    public void CombinedMonsterNames_HaveOneBNpcNameIdPerPart()
    {
        var data = LoadRealData();

        var combined = data.Monsters.Values
            .Select(monster => (Monster: monster, Parts: monster.NameEn.Split([" & ", " or "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .Where(entry => entry.Parts.Length > 1)
            .ToList();
        Assert.NotEmpty(combined);

        var violations = combined
            .Where(entry => entry.Monster.BNpcNameId is not null
                || entry.Monster.BNpcNameIds is not { } ids
                || ids.Count != entry.Parts.Length
                || ids.Distinct().Count() != ids.Count)
            .Select(entry => $"Monster {entry.Monster.Id} \"{entry.Monster.NameEn}\"")
            .ToList();

        Assert.True(violations.Count == 0, "Sammelnamen ohne passende BNpcNameIds:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Monsters_UseEitherBNpcNameIdOrBNpcNameIds()
    {
        var data = LoadRealData();

        var violations = data.Monsters.Values
            .Where(monster => monster.BNpcNameId is not null && monster.BNpcNameIds is not null
                || monster.BNpcNameIds is { Count: < 2 })
            .Select(monster => $"Monster {monster.Id} \"{monster.NameEn}\"")
            .ToList();

        Assert.True(violations.Count == 0, "BNpcNameId und BNpcNameIds gemischt oder BNpcNameIds mit < 2 IDs:\n" + string.Join("\n", violations));
    }

    // Fängt verwaiste Einträge ab, z.B. einen am Komma zerschnittenen Namen ("Aenc Thon" /
    // "Lord of the Lingering Gaze"), dessen zweite Hälfte beim Zusammenführen übrig bleibt.
    [Fact]
    public void EveryMonster_IsUsedByASource_AndEverySourceMonsterExists()
    {
        var data = LoadRealData();
        var usedMonsterIds = data.Sources.Select(source => source.MonsterId).ToHashSet();

        var unused = data.Monsters.Values.Where(monster => !usedMonsterIds.Contains(monster.Id))
            .Select(monster => $"Monster {monster.Id} \"{monster.NameEn}\" ohne Quelle");
        var missing = usedMonsterIds.Where(id => !data.Monsters.ContainsKey(id))
            .Select(id => $"Quelle verweist auf fehlendes Monster {id}");
        var violations = unused.Concat(missing).ToList();

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    // Masked-Carnivale-Stages: Stage-Nummer als Feld, Anzeigename aus dem UiStrings-Template. Die
    // ZoneName-Felder im JSON müssen dazu passen (Fallback/Lesbarkeit), DutyName = "Masked Carnivale: <n>".
    [Fact]
    public void MaskedCarnivaleStages_HaveStageNumber_AndConsistentNames()
    {
        var data = LoadRealData();

        var stages = data.Locations.Values
            .Where(location => location.MaskedCarnivaleStage is not null
                || location.DutyName?.StartsWith("Masked Carnivale", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        Assert.NotEmpty(stages);

        var violations = new List<string>();
        foreach (var location in stages)
        {
            var label = $"Location {location.Id} \"{location.DutyName ?? location.ZoneNameEn}\"";
            if (location.MaskedCarnivaleStage is not { } stage || stage is < 1 or > 40)
            {
                violations.Add($"{label}: MaskedCarnivaleStage fehlt oder ungültig");
                continue;
            }

            if (location.DutyName != $"Masked Carnivale: {stage}")
                violations.Add($"{label}: DutyName passt nicht zu Stage {stage}");
            if (location.GetContentFinderConditionIds().Count > 0 || location.TerritoryTypeId is not null || location.PlaceNameId is not null)
                violations.Add($"{label}: Game-IDs gesetzt - der Name käme dann nicht aus dem Template");

            var jsonNames = new Dictionary<DisplayLanguage, string>
            {
                [DisplayLanguage.German] = location.ZoneNameDe,
                [DisplayLanguage.English] = location.ZoneNameEn,
                [DisplayLanguage.French] = location.ZoneNameFr,
                [DisplayLanguage.Japanese] = location.ZoneNameJa,
            };
            foreach (var (language, jsonName) in jsonNames)
            {
                var expected = UiStrings.Format(UiStrings.Key.MaskedCarnivaleStageFormat, language, stage);
                if (jsonName != expected)
                    violations.Add($"{label}: ZoneName ({language}) \"{jsonName}\" statt \"{expected}\"");
            }
        }

        // Jede Carnivale-Quelle zeigt auf eine Stage mit Nummer.
        violations.AddRange(data.Sources
            .Where(source => source.Method == SourceMethod.MaskedCarnivale)
            .Select(source => data.Monsters.GetValueOrDefault(source.MonsterId))
            .Where(monster => monster is null || data.Locations.GetValueOrDefault(monster.LocationId)?.MaskedCarnivaleStage is null)
            .Select(monster => $"Carnivale-Quelle ohne Stage-Location: Monster {monster?.Id} \"{monster?.NameEn}\""));

        Assert.True(violations.Count == 0, "Masked-Carnivale-Stages inkonsistent:\n" + string.Join("\n", violations));
    }
}
