using BLUnion.Models;

namespace BLUnion.Services;

// Ein fehlender Spell samt der betrachteten Spieler, die ihn noch brauchen.
public sealed record PlanSpell(uint SpellId, IReadOnlyList<string> PlayersMissingIt);

// Ein Monster innerhalb eines Orts-Eintrags - Location ist die konkrete Location des Monsters
// (für die Koordinaten), nicht zwingend die repräsentative Location des Eintrags.
public sealed record PlanMonster(Monster Monster, Location Location, IReadOnlyList<PlanSpell> Spells);

public sealed record PlanBeneficiary(string PlayerName, IReadOnlyList<uint> SpellIds);

// Ein Ort (Duty oder Open-World-Zone). Benefit = Summe über die verschiedenen fehlenden Spells
// dieses Orts x Anzahl der Spieler, die den Spell noch brauchen.
public sealed record PlanLocation(
    string Key,
    Location RepresentativeLocation,
    bool IsDuty,
    IReadOnlyList<PlanMonster> Monsters,
    IReadOnlyList<PlanBeneficiary> Beneficiaries,
    int Benefit);

// Masked Carnivale: entweder eine Stage (Stage = Location des Monsters) oder eine Fortschritts-
// Belohnung (RequiredStages = Monster.RequirementValue).
public sealed record PlanCarnivaleEntry(Monster Monster, Location? Stage, PlanSpell Spell, int? RequiredStages);

public enum TotemConditionKind
{
    None,
    LearnedSpells,
    Level,
}

public sealed record TotemCondition(TotemConditionKind Kind, int Required);

// Met: null = für den lokalen Spieler nicht beurteilbar (kein lokaler Status / Level unbekannt /
// Totem ohne Bedingung).
public sealed record PlanTotemEntry(PlanSpell Spell, TotemCondition Condition, bool? Met);

public sealed record LearningPlan(
    IReadOnlyList<PlanLocation> Locations,
    IReadOnlyList<PlanCarnivaleEntry> Carnivale,
    IReadOnlyList<PlanTotemEntry> Totems,
    int MissingSpellCount,
    int TotalBenefit);

// Berechnet den Learning Plan (siehe MainWindow.LearningPlan.cs) - bewusst ohne Dalamud-Abhängigkeit,
// damit die Logik in BLUnion.Tests testbar ist.
//
// - Gruppiert nach ORT, nicht nach Monster/LocationId: Schlüssel ist die ContentFinderConditionId
//   (Duty), sonst die TerritoryTypeId (Zone), sonst der englische Duty-/Zonenname. Locations, die nur
//   wegen unterschiedlicher Koordinaten getrennt sind (Central Shroud = 7/109/111), ergeben EINEN Eintrag.
// - Nur Duty/DungeonTrash/OpenWorld/Levequest-Quellen landen in der Orts-Liste. Masked Carnivale und
//   Totems bekommen eigene Abschnitte; Startzauber tauchen nicht auf.
// - Sortierung: Benefit absteigend, bei Gleichstand Duties vor Open World (synced in Gruppen-Duties ist
//   das Lernen garantiert), dann alphabetisch nach dem angezeigten Namen (locationName).
public static class LearningPlanService
{
    public static LearningPlan Build(
        IReadOnlyList<PlayerSpellStatus> players,
        IReadOnlyDictionary<uint, Spell> spells,
        IReadOnlyDictionary<uint, Monster> monsters,
        IReadOnlyDictionary<uint, Location> locations,
        IReadOnlyList<SpellSource> sources,
        Func<Location, string> locationName,
        int? blueMageLevel)
    {
        // Spell -> Spieler, denen er fehlt (Reihenfolge der Spieler wie übergeben).
        var missingBySpell = spells.Keys
            .Select(spellId => new PlanSpell(
                spellId,
                players.Where(player => !player.LearnedSpellIds.Contains(spellId)).Select(player => player.CharacterName).ToList()))
            .Where(spell => spell.PlayersMissingIt.Count > 0)
            .ToDictionary(spell => spell.SpellId);

        var locationSources = new Dictionary<string, List<(Monster Monster, Location Location, PlanSpell Spell)>>();
        var carnivale = new List<PlanCarnivaleEntry>();
        var totems = new List<PlanTotemEntry>();
        var localPlayer = players.FirstOrDefault(player => player.IsLocalPlayer);

        foreach (var source in sources)
        {
            if (!missingBySpell.TryGetValue(source.SpellId, out var spell) || !monsters.TryGetValue(source.MonsterId, out var monster))
                continue;

            locations.TryGetValue(monster.LocationId, out var location);

            switch (source.Method)
            {
                case SourceMethod.Duty or SourceMethod.DungeonTrash or SourceMethod.OpenWorld or SourceMethod.Levequest:
                    if (location is null)
                        break;

                    var key = GetLocationKey(location);
                    if (!locationSources.TryGetValue(key, out var list))
                        locationSources[key] = list = [];
                    list.Add((monster, location, spell));
                    break;

                case SourceMethod.MaskedCarnivale:
                    carnivale.Add(new PlanCarnivaleEntry(monster, location, spell, null));
                    break;

                case SourceMethod.MaskedCarnivaleProgress:
                    carnivale.Add(new PlanCarnivaleEntry(monster, null, spell, monster.RequirementValue is > 0 ? monster.RequirementValue : null));
                    break;

                case SourceMethod.Totem or SourceMethod.TotemSpellCount or SourceMethod.TotemLevel:
                    var condition = GetTotemCondition(source.Method, monster.RequirementValue);
                    totems.Add(new PlanTotemEntry(spell, condition, IsTotemConditionMet(condition, localPlayer, blueMageLevel)));
                    break;
            }
        }

        var planLocations = locationSources
            .Select(pair => BuildLocation(pair.Key, pair.Value, players))
            .OrderByDescending(entry => entry.Benefit)
            .ThenByDescending(entry => entry.IsDuty)
            .ThenBy(entry => locationName(entry.RepresentativeLocation), StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Gesamtsumme über die VERSCHIEDENEN fehlenden Spells mit bekannter Quelle - ein Spell aus Duty
        // und Carnivale (oder aus zwei Orten) zählt nur einmal.
        var learnableSpells = locationSources.Values.SelectMany(list => list.Select(entry => entry.Spell))
            .Concat(carnivale.Select(entry => entry.Spell))
            .Concat(totems.Select(entry => entry.Spell))
            .DistinctBy(spell => spell.SpellId)
            .ToList();

        return new LearningPlan(
            planLocations,
            carnivale
                .OrderBy(entry => entry.RequiredStages is not null)
                .ThenBy(entry => entry.RequiredStages ?? 0)
                // Stage-Nummer numerisch (sonst stünde "Nr. 8" als Text hinter "Nr. 24").
                .ThenBy(entry => entry.Stage?.MaskedCarnivaleStage ?? int.MaxValue)
                .ThenBy(entry => entry.Stage is null ? "" : locationName(entry.Stage), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => spells[entry.Spell.SpellId].SpellbookOrder)
                .ToList(),
            totems.OrderBy(entry => spells[entry.Spell.SpellId].SpellbookOrder).ToList(),
            missingBySpell.Count,
            learnableSpells.Sum(spell => spell.PlayersMissingIt.Count));
    }

    public static LearningPlan Build(
        IReadOnlyList<PlayerSpellStatus> players, SpellDataService data, Func<Location, string> locationName, int? blueMageLevel) =>
        Build(players, data.Spells, data.Monsters, data.Locations, data.Sources, locationName, blueMageLevel);

    public static string GetLocationKey(Location location)
    {
        if (location.ContentFinderConditionId is { } cfcId)
            return $"cfc:{cfcId}";
        if (location.TerritoryTypeId is { } territoryTypeId)
            return $"tt:{territoryTypeId}";
        return $"name:{(location.DutyName ?? location.ZoneNameEn).Trim().ToLowerInvariant()}";
    }

    public static bool IsDuty(Location location) => location.ContentFinderConditionId is not null || location.DutyName is not null;

    // Die Art der Bedingung kommt aus der SourceMethod, der Wert aus Monster.RequirementValue -
    // ohne Wert (> 0): keine Bedingung.
    public static TotemCondition GetTotemCondition(SourceMethod method, int? requirementValue) =>
        (method, requirementValue) switch
        {
            (SourceMethod.TotemSpellCount, > 0) => new TotemCondition(TotemConditionKind.LearnedSpells, requirementValue.Value),
            (SourceMethod.TotemLevel, > 0) => new TotemCondition(TotemConditionKind.Level, requirementValue.Value),
            _ => new TotemCondition(TotemConditionKind.None, 0),
        };

    private static bool? IsTotemConditionMet(TotemCondition condition, PlayerSpellStatus? localPlayer, int? blueMageLevel) =>
        condition.Kind switch
        {
            TotemConditionKind.LearnedSpells when localPlayer is not null => localPlayer.LearnedSpellIds.Count >= condition.Required,
            TotemConditionKind.Level when blueMageLevel is { } level => level >= condition.Required,
            _ => null,
        };

    private static PlanLocation BuildLocation(
        string key, List<(Monster Monster, Location Location, PlanSpell Spell)> entries, IReadOnlyList<PlayerSpellStatus> players)
    {
        // Repräsentant: bevorzugt eine Location ohne Koordinaten (die "reine" Zone), sonst die kleinste Id.
        var representative = entries
            .Select(entry => entry.Location)
            .DistinctBy(location => location.Id)
            .OrderBy(location => location.Coordinates is not null)
            .ThenBy(location => location.Id)
            .First();

        var monsters = entries
            .GroupBy(entry => entry.Monster.Id)
            .Select(group => new PlanMonster(
                group.First().Monster,
                group.First().Location,
                group.Select(entry => entry.Spell).DistinctBy(spell => spell.SpellId).ToList()))
            .OrderBy(monster => monster.Monster.Id)
            .ToList();

        // Jeder Spell zählt pro Ort nur einmal, auch wenn ihn mehrere Monster dort lehren.
        var distinctSpells = entries.Select(entry => entry.Spell).DistinctBy(spell => spell.SpellId).ToList();

        var beneficiaries = players
            .Select(player => new PlanBeneficiary(
                player.CharacterName,
                distinctSpells.Where(spell => spell.PlayersMissingIt.Contains(player.CharacterName)).Select(spell => spell.SpellId).ToList()))
            .Where(beneficiary => beneficiary.SpellIds.Count > 0)
            .ToList();

        return new PlanLocation(
            key,
            representative,
            entries.Any(entry => IsDuty(entry.Location)),
            monsters,
            beneficiaries,
            distinctSpells.Sum(spell => spell.PlayersMissingIt.Count));
    }
}
