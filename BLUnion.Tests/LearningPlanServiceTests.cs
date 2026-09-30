using BLUnion.Models;
using BLUnion.Services;
using BLUnion.Tests.Fakes;
using Xunit;

namespace BLUnion.Tests;

// LearningPlanService ohne Dalamud-Runtime: Gruppierung nach Ort, Sortierung, Nutzen-Summe,
// Carnivale-Doppelzählung, Totem-Bedingungen - synthetisch und gegen die echten Data/*.json.
public class LearningPlanServiceTests
{
    // ----- Test-Daten -----

    private sealed class TestData
    {
        public Dictionary<uint, Spell> Spells { get; } = [];
        public Dictionary<uint, Monster> Monsters { get; } = [];
        public Dictionary<uint, Location> Locations { get; } = [];
        public List<SpellSource> Sources { get; } = [];

        public TestData Spell(uint id, int order)
        {
            this.Spells[id] = new Spell { Id = id, NameDe = $"S{id}", NameEn = $"S{id}", NameFr = $"S{id}", NameJa = $"S{id}", SpellbookOrder = order };
            return this;
        }

        public TestData Location(uint id, string name, uint? territoryTypeId = null, uint? cfcId = null, string? dutyName = null, string? coordinates = null)
        {
            this.Locations[id] = new Location
            {
                Id = id, ZoneNameDe = name, ZoneNameEn = name, ZoneNameFr = name, ZoneNameJa = name,
                TerritoryTypeId = territoryTypeId, ContentFinderConditionId = cfcId, DutyName = dutyName, Coordinates = coordinates,
            };
            return this;
        }

        public TestData Monster(uint id, uint locationId, int? requirementValue = null, string? notes = null)
        {
            this.Monsters[id] = new Monster
            {
                Id = id, NameDe = $"M{id}", NameEn = $"M{id}", NameFr = $"M{id}", NameJa = $"M{id}", LocationId = locationId,
                RequirementValue = requirementValue, Notes = notes,
            };
            return this;
        }

        public TestData Source(uint spellId, uint monsterId, SourceMethod method)
        {
            this.Sources.Add(new SpellSource { SpellId = spellId, MonsterId = monsterId, Method = method });
            return this;
        }

        public LearningPlan Build(IReadOnlyList<PlayerSpellStatus> players, int? blueMageLevel = null) =>
            LearningPlanService.Build(players, this.Spells, this.Monsters, this.Locations, this.Sources, location => location.ZoneNameEn, blueMageLevel);
    }

    private static PlayerSpellStatus Player(string name, bool isLocal = false, params uint[] learned) =>
        new() { CharacterName = name, LearnedSpellIds = [.. learned], IsLocalPlayer = isLocal };

    // ----- Gruppierung -----

    [Fact]
    public void LocationsWithSameTerritory_ButDifferentCoordinates_AreOneEntry()
    {
        var data = new TestData()
            .Spell(1, 1).Spell(2, 2).Spell(3, 3)
            .Location(7, "Central Shroud", territoryTypeId: 148)
            .Location(109, "Central Shroud", territoryTypeId: 148, coordinates: "15, 21")
            .Location(111, "Central Shroud", territoryTypeId: 148, coordinates: "13, 21")
            .Monster(10, 7).Monster(11, 109).Monster(12, 111)
            .Source(1, 10, SourceMethod.OpenWorld).Source(2, 11, SourceMethod.OpenWorld).Source(3, 12, SourceMethod.OpenWorld);

        var plan = data.Build([Player("Alice")]);

        var entry = Assert.Single(plan.Locations);
        Assert.Equal("tt:148", entry.Key);
        Assert.Equal(7u, entry.RepresentativeLocation.Id); // Location ohne Koordinaten als Repräsentant
        Assert.Equal([10u, 11u, 12u], entry.Monsters.Select(monster => monster.Monster.Id));
        Assert.Equal(["15, 21"], entry.Monsters.Where(monster => monster.Monster.Id == 11).Select(monster => monster.Location.Coordinates));
        Assert.False(entry.IsDuty);
    }

    [Fact]
    public void DutyBosses_AreGroupedByContentFinderCondition_NotByMonster()
    {
        var data = new TestData()
            .Spell(1, 1).Spell(2, 2).Spell(3, 3)
            .Location(1, "Pharos Sirius", territoryTypeId: 160, cfcId: 17, dutyName: "Pharos Sirius")
            .Location(2, "Pharos Sirius (Boss 2)", territoryTypeId: 999, cfcId: 17, dutyName: "Pharos Sirius")
            .Monster(10, 1).Monster(11, 1).Monster(12, 2)
            .Source(1, 10, SourceMethod.Duty).Source(2, 11, SourceMethod.Duty).Source(3, 12, SourceMethod.DungeonTrash);

        var entry = Assert.Single(data.Build([Player("Alice")]).Locations);

        Assert.Equal("cfc:17", entry.Key);
        Assert.True(entry.IsDuty);
        Assert.Equal(3, entry.Monsters.Count);
    }

    [Fact]
    public void LocationsWithoutGameIds_FallBackToEnglishName()
    {
        var data = new TestData()
            .Spell(1, 1).Spell(2, 2)
            .Location(1, "Some Zone").Location(2, " some zone ", coordinates: "1, 2")
            .Monster(10, 1).Monster(11, 2)
            .Source(1, 10, SourceMethod.OpenWorld).Source(2, 11, SourceMethod.OpenWorld);

        var entry = Assert.Single(data.Build([Player("Alice")]).Locations);

        Assert.Equal("name:some zone", entry.Key);
    }

    [Fact]
    public void SingleSpellLocations_AreIncluded()
    {
        var data = new TestData()
            .Spell(1, 1)
            .Location(1, "Lonely Zone", territoryTypeId: 1)
            .Monster(10, 1)
            .Source(1, 10, SourceMethod.OpenWorld);

        var entry = Assert.Single(data.Build([Player("Alice")]).Locations);

        Assert.Equal(1, entry.Benefit);
        Assert.Single(Assert.Single(entry.Monsters).Spells);
    }

    [Fact]
    public void KnownSpells_AreNotPlanned()
    {
        var data = new TestData()
            .Spell(1, 1)
            .Location(1, "Zone", territoryTypeId: 1)
            .Monster(10, 1)
            .Source(1, 10, SourceMethod.OpenWorld);

        var plan = data.Build([Player("Alice", learned: 1), Player("Bob", learned: 1)]);

        Assert.Empty(plan.Locations);
        Assert.Equal(0, plan.MissingSpellCount);
    }

    // ----- Nutzen und Sortierung -----

    [Fact]
    public void Benefit_SumsPlayersPerDistinctSpell_AndCountsASpellTaughtTwiceOnce()
    {
        var data = new TestData()
            .Spell(1, 1).Spell(2, 2)
            .Location(1, "Duty", cfcId: 5, dutyName: "Duty")
            .Monster(10, 1).Monster(11, 1)
            .Source(1, 10, SourceMethod.Duty)
            .Source(1, 11, SourceMethod.Duty) // derselbe Spell von einem zweiten Monster im selben Ort
            .Source(2, 11, SourceMethod.Duty);

        // Spell 1 fehlt Alice + Bob (2), Spell 2 nur Alice (1) -> 3
        var entry = Assert.Single(data.Build([Player("Alice"), Player("Bob", learned: 2)]).Locations);

        Assert.Equal(3, entry.Benefit);
    }

    [Fact]
    public void Beneficiaries_ListEachPlayerWithTheSpellsTheyNeedHere()
    {
        var data = new TestData()
            .Spell(1, 1).Spell(2, 2)
            .Location(1, "Duty", cfcId: 5, dutyName: "Duty")
            .Monster(10, 1)
            .Source(1, 10, SourceMethod.Duty).Source(2, 10, SourceMethod.Duty);

        var entry = Assert.Single(data.Build([Player("Alice", learned: 1), Player("Bob"), Player("Carol", learned: [1, 2])]).Locations);

        Assert.Equal(["Alice", "Bob"], entry.Beneficiaries.Select(beneficiary => beneficiary.PlayerName));
        Assert.Equal([2u], entry.Beneficiaries[0].SpellIds);
        Assert.Equal([1u, 2u], entry.Beneficiaries[1].SpellIds.Order());
    }

    [Fact]
    public void Sorting_ByBenefit_ThenDutiesBeforeOpenWorld_ThenAlphabetical()
    {
        var data = new TestData()
            .Spell(1, 1).Spell(2, 2).Spell(3, 3).Spell(4, 4).Spell(5, 5)
            .Location(1, "Zeta Zone", territoryTypeId: 1)
            .Location(2, "Alpha Zone", territoryTypeId: 2)
            .Location(3, "Omega Duty", cfcId: 3, dutyName: "Omega Duty")
            .Location(4, "Big Zone", territoryTypeId: 4)
            .Monster(10, 1).Monster(11, 2).Monster(12, 3).Monster(13, 4)
            .Source(1, 10, SourceMethod.OpenWorld)
            .Source(2, 11, SourceMethod.OpenWorld)
            .Source(3, 12, SourceMethod.Duty)
            .Source(4, 13, SourceMethod.OpenWorld).Source(5, 13, SourceMethod.OpenWorld);

        var plan = data.Build([Player("Alice")]);

        // Big Zone (2) vor den Einer-Orten; bei Gleichstand Duty zuerst, dann Alpha vor Zeta.
        Assert.Equal(["Big Zone", "Omega Duty", "Alpha Zone", "Zeta Zone"], plan.Locations.Select(entry => entry.RepresentativeLocation.ZoneNameEn));
    }

    // ----- Masked Carnivale -----

    [Fact]
    public void SpellInDutyAndCarnivale_IsListedInBoth_ButCountedOnceInTotal()
    {
        var data = new TestData()
            .Spell(1, 25)
            .Location(1, "The Dragon's Neck", cfcId: 1, dutyName: "The Dragon's Neck")
            .Location(2, "Masked Carnivale: 20", dutyName: "Masked Carnivale: 20")
            .Monster(10, 1).Monster(11, 2)
            .Source(1, 10, SourceMethod.Duty)
            .Source(1, 11, SourceMethod.MaskedCarnivale);

        var plan = data.Build([Player("Alice"), Player("Bob")]);

        var location = Assert.Single(plan.Locations);
        Assert.Equal(2, location.Benefit);
        var carnivale = Assert.Single(plan.Carnivale);
        Assert.Equal(2u, carnivale.Stage!.Id);
        Assert.Equal(1u, carnivale.Spell.SpellId);
        Assert.Equal(2, plan.TotalBenefit); // nicht 4
    }

    [Fact]
    public void CarnivaleOnlySpells_DoNotAppearInTheLocationList()
    {
        var data = new TestData()
            .Spell(1, 1).Spell(2, 2)
            .Location(1, "Masked Carnivale: 08", dutyName: "Masked Carnivale: 08")
            .Monster(10, 1).Monster(11, 0, requirementValue: 10)
            .Source(1, 10, SourceMethod.MaskedCarnivale)
            .Source(2, 11, SourceMethod.MaskedCarnivaleProgress);

        var plan = data.Build([Player("Alice")]);

        Assert.Empty(plan.Locations);
        Assert.Equal(2, plan.Carnivale.Count);
        Assert.Equal(10, plan.Carnivale.Single(entry => entry.Spell.SpellId == 2).RequiredStages);
        Assert.Null(plan.Carnivale.Single(entry => entry.Spell.SpellId == 1).RequiredStages);
    }

    // ----- Totems und Sonderquellen -----

    [Theory]
    [InlineData(SourceMethod.TotemSpellCount, 10, TotemConditionKind.LearnedSpells, 10)]
    [InlineData(SourceMethod.TotemSpellCount, 120, TotemConditionKind.LearnedSpells, 120)]
    [InlineData(SourceMethod.TotemLevel, 80, TotemConditionKind.Level, 80)]
    [InlineData(SourceMethod.Totem, 10, TotemConditionKind.None, 0)]
    [InlineData(SourceMethod.TotemSpellCount, null, TotemConditionKind.None, 0)]
    [InlineData(SourceMethod.TotemLevel, 0, TotemConditionKind.None, 0)]
    public void GetTotemCondition_UsesTheRequirementValueForTheMethod(SourceMethod method, int? requirementValue, TotemConditionKind kind, int required)
    {
        Assert.Equal(new TotemCondition(kind, required), LearningPlanService.GetTotemCondition(method, requirementValue));
    }

    [Fact]
    public void Conditions_ComeFromRequirementValue_NotFromNotes()
    {
        var data = new TestData()
            .Spell(1, 1).Spell(2, 2)
            .Monster(10, 0, requirementValue: null, notes: "Learn 10 spells")
            .Monster(11, 0, requirementValue: null, notes: "Complete 10 Masked Carnivale stages")
            .Source(1, 10, SourceMethod.TotemSpellCount)
            .Source(2, 11, SourceMethod.MaskedCarnivaleProgress);

        var plan = data.Build([Player("Me", isLocal: true)]);

        Assert.Equal(TotemConditionKind.None, Assert.Single(plan.Totems).Condition.Kind);
        Assert.Null(Assert.Single(plan.Carnivale).RequiredStages);
    }

    [Fact]
    public void TotemSpellCount_IsMetOnlyWhenTheLocalPlayerHasEnoughSpells()
    {
        var data = new TestData()
            .Spell(1, 1).Spell(2, 2).Spell(3, 3)
            .Monster(10, 0, requirementValue: 2)
            .Source(3, 10, SourceMethod.TotemSpellCount);

        var enough = Assert.Single(data.Build([Player("Me", isLocal: true, learned: [1, 2])]).Totems);
        var tooFew = Assert.Single(data.Build([Player("Me", isLocal: true, learned: 1)]).Totems);
        var noLocal = Assert.Single(data.Build([Player("Other", learned: [1, 2])]).Totems);

        Assert.True(enough.Met);
        Assert.False(tooFew.Met);
        Assert.Null(noLocal.Met);
    }

    [Fact]
    public void TotemLevel_UsesTheBlueMageLevel_AndIsUnknownWithoutIt()
    {
        var data = new TestData()
            .Spell(1, 88)
            .Monster(10, 0, requirementValue: 70)
            .Source(1, 10, SourceMethod.TotemLevel);

        var players = new[] { Player("Me", isLocal: true) };

        Assert.True(Assert.Single(data.Build(players, blueMageLevel: 70).Totems).Met);
        Assert.False(Assert.Single(data.Build(players, blueMageLevel: 69).Totems).Met);
        Assert.Null(Assert.Single(data.Build(players, blueMageLevel: null).Totems).Met);
    }

    [Fact]
    public void Totems_AndStartingSpells_AreNotMixedIntoTheLocationList()
    {
        var data = new TestData()
            .Spell(1, 1).Spell(2, 2)
            .Location(1, "Somewhere", territoryTypeId: 1)
            .Monster(10, 1, requirementValue: 5).Monster(11, 1)
            .Source(1, 10, SourceMethod.TotemSpellCount)
            .Source(2, 11, SourceMethod.StartingSpell);

        var plan = data.Build([Player("Alice")]);

        Assert.Empty(plan.Locations);
        Assert.Single(plan.Totems);
        Assert.Empty(plan.Carnivale);
    }

    [Fact]
    public void LevequestSources_AreOpenWorldLocations()
    {
        var data = new TestData()
            .Spell(1, 1)
            .Location(1, "Mor Dhona", territoryTypeId: 156)
            .Monster(10, 1)
            .Source(1, 10, SourceMethod.Levequest);

        Assert.False(Assert.Single(data.Build([Player("Alice")]).Locations).IsDuty);
    }

    // ----- Echte Daten -----

    private static SpellDataService LoadRealData()
    {
        var service = new SpellDataService(new TestPluginLog());
        service.Load(Path.Combine(AppContext.BaseDirectory, "TestData"));
        return service;
    }

    [Fact]
    public void RealData_CentralShroudLocations7_109_111_AreOneEntry()
    {
        var data = LoadRealData();
        var plan = LearningPlanService.Build([Player("Nobody")], data, location => location.ZoneNameEn, blueMageLevel: null);

        var centralShroud = plan.Locations.Where(entry => entry.Monsters.Any(monster => monster.Location.Id is 7 or 109 or 111)).ToList();

        var entry = Assert.Single(centralShroud);
        var locationIds = entry.Monsters.Select(monster => monster.Location.Id).ToHashSet();
        Assert.Contains(109u, locationIds);
        Assert.Contains(111u, locationIds);
        Assert.Equal(7u, entry.RepresentativeLocation.Id);
    }

    [Fact]
    public void RealData_EveryTotemAndCarnivaleRewardEntry_HasARequirementValue()
    {
        var data = LoadRealData();

        var conditionalMonsters = data.Sources
            .Where(source => source.Method is SourceMethod.TotemSpellCount or SourceMethod.TotemLevel or SourceMethod.MaskedCarnivaleProgress)
            .Select(source => data.Monsters[source.MonsterId])
            .DistinctBy(monster => monster.Id)
            .ToList();

        Assert.NotEmpty(conditionalMonsters);
        Assert.All(conditionalMonsters, monster => Assert.True(
            monster.RequirementValue is > 0, $"Monster {monster.Id} \"{monster.NameEn}\" hat keinen RequirementValue > 0."));

        // ... und der Plan liest ihn tatsächlich aus.
        var plan = LearningPlanService.Build([Player("Nobody", isLocal: true)], data, location => location.ZoneNameEn, blueMageLevel: 1);
        Assert.All(plan.Totems, totem => Assert.NotEqual(TotemConditionKind.None, totem.Condition.Kind));
        Assert.All(plan.Carnivale.Where(entry => entry.Stage is null), entry => Assert.True(entry.RequiredStages is > 0));
    }

    [Fact]
    public void RealData_LocationListContainsOnlyLocationMethods()
    {
        var data = LoadRealData();
        var plan = LearningPlanService.Build([Player("Nobody")], data, location => location.ZoneNameEn, blueMageLevel: null);

        var carnivaleStageIds = data.Locations.Values
            .Where(location => location.DutyName?.StartsWith("Masked Carnivale", StringComparison.Ordinal) == true)
            .Select(location => location.Id)
            .ToHashSet();

        Assert.NotEmpty(plan.Locations);
        Assert.DoesNotContain(plan.Locations, entry => entry.Monsters.Any(monster => carnivaleStageIds.Contains(monster.Location.Id)));
        Assert.Equal(plan.Locations.Count, plan.Locations.Select(entry => entry.Key).Distinct().Count());
    }
}
