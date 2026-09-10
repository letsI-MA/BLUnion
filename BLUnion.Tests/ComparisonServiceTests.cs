using BLUnion.Models;
using BLUnion.Services;
using BLUnion.Tests.Fakes;
using Xunit;

namespace BLUnion.Tests;

public class ComparisonServiceTests
{
    private static PlayerSpellStatus Status(string name, params uint[] learnedIds) => new()
    {
        CharacterName = name,
        LearnedSpellIds = learnedIds.ToHashSet(),
    };

    [Fact]
    public void GetCommonlyMissingSpells_NoPlayers_ReturnsEmpty()
    {
        var service = new ComparisonService();

        var result = service.GetCommonlyMissingSpells(new uint[] { 1, 2, 3 }, []);

        Assert.Empty(result);
    }

    [Fact]
    public void GetCommonlyMissingSpells_NoKnownSpells_ReturnsEmpty()
    {
        var service = new ComparisonService();
        var party = new List<PlayerSpellStatus> { Status("Alice") };

        var result = service.GetCommonlyMissingSpells(Array.Empty<uint>(), party);

        Assert.Empty(result);
    }

    [Fact]
    public void GetCommonlyMissingSpells_SpellKnownByEveryone_IsExcluded()
    {
        var service = new ComparisonService();
        var party = new List<PlayerSpellStatus>
        {
            Status("Alice", 1, 2),
            Status("Bob", 1, 2),
        };

        var result = service.GetCommonlyMissingSpells([1, 2], party);

        Assert.Empty(result);
    }

    [Fact]
    public void GetCommonlyMissingSpells_SingleSpellMissingForOnePlayer_ListsOnlyThatPlayer()
    {
        var service = new ComparisonService();
        var party = new List<PlayerSpellStatus>
        {
            Status("Alice", 1),
            Status("Bob"), // kennt Spell 1 nicht
        };

        var result = service.GetCommonlyMissingSpells([1], party);

        var entry = Assert.Single(result);
        Assert.Equal(1u, entry.SpellId);
        Assert.Equal(["Bob"], entry.PlayersMissingIt);
    }

    [Fact]
    public void GetCommonlyMissingSpells_SpellMissingForEveryone_ListsAllPlayers()
    {
        var service = new ComparisonService();
        var party = new List<PlayerSpellStatus>
        {
            Status("Alice"),
            Status("Bob"),
            Status("Charlie"),
        };

        var result = service.GetCommonlyMissingSpells([1], party);

        var entry = Assert.Single(result);
        Assert.Equal(3, entry.PlayersMissingIt.Count);
        Assert.Contains("Alice", entry.PlayersMissingIt);
        Assert.Contains("Bob", entry.PlayersMissingIt);
        Assert.Contains("Charlie", entry.PlayersMissingIt);
    }

    [Fact]
    public void GetCommonlyMissingSpells_OverlappingMissingSets_ResultSortedByMissingCountDescending()
    {
        var service = new ComparisonService();
        var party = new List<PlayerSpellStatus>
        {
            // Spell 1: fehlt allen drei (3 Spieler)
            // Spell 2: fehlt nur Bob (1 Spieler)
            // Spell 3: fehlt Alice und Charlie (2 Spieler)
            Status("Alice", 2),
            Status("Bob", 3),
            Status("Charlie", 2),
        };

        var result = service.GetCommonlyMissingSpells([1, 2, 3], party);

        Assert.Equal(3, result.Count);
        Assert.Equal(1u, result[0].SpellId);
        Assert.Equal(3, result[0].PlayersMissingIt.Count);
        Assert.Equal(3u, result[1].SpellId);
        Assert.Equal(2, result[1].PlayersMissingIt.Count);
        Assert.Equal(2u, result[2].SpellId);
        Assert.Single(result[2].PlayersMissingIt);
    }

    [Fact]
    public void GetCommonlyMissingSpells_TiedMissingCounts_PreservesInputOrderOfKnownSpellIds()
    {
        // OrderByDescending in .NET ist ein stabiler Sort - bei gleichem PlayersMissingIt.Count
        // bleibt also die Reihenfolge aus allKnownSpellIds erhalten.
        var service = new ComparisonService();
        var party = new List<PlayerSpellStatus> { Status("Alice") };

        var result = service.GetCommonlyMissingSpells([5, 3, 9], party);

        Assert.Equal([5u, 3u, 9u], result.Select(r => r.SpellId));
    }

    [Fact]
    public void GroupMissingSpellsByMonster_GroupsBySharedMonsterAndOrdersByCoverageDescending()
    {
        var dataService = LoadFixtureSpellData();
        var comparisonService = new ComparisonService();

        // Spell 1 und 2 beide über Monster 10 lernbar (siehe Fixtures/SpellData/sources.json),
        // Spell 4 nur über Monster 11.
        var missing = new List<MissingSpellInfo>
        {
            new(1, ["Alice"]),
            new(2, ["Alice"]),
            new(4, ["Alice"]),
        };

        var groups = comparisonService.GroupMissingSpellsByMonster(missing, dataService);

        Assert.Equal(10u, groups[0].MonsterId);
        Assert.Equal([1u, 2u], groups[0].CoveredMissingSpellIds.OrderBy(id => id));

        var monster11Group = groups.Single(g => g.MonsterId == 11);
        Assert.Contains(2u, monster11Group.CoveredMissingSpellIds);
        Assert.Contains(4u, monster11Group.CoveredMissingSpellIds);
    }

    [Fact]
    public void GroupMissingSpellsByMonster_ExcludeTotems_OmitsTotemOnlyMonsterGroup()
    {
        var dataService = LoadFixtureSpellData();
        var comparisonService = new ComparisonService();

        // Spell 4 ist NUR über Monster 11 per Totem lernbar (siehe Fixture).
        var missing = new List<MissingSpellInfo> { new(4, ["Alice"]) };

        var withTotems = comparisonService.GroupMissingSpellsByMonster(missing, dataService, excludeTotems: false);
        var withoutTotems = comparisonService.GroupMissingSpellsByMonster(missing, dataService, excludeTotems: true);

        Assert.Single(withTotems);
        Assert.Empty(withoutTotems);
    }

    [Fact]
    public void GroupMissingSpellsByMonster_SpellWithoutSource_ContributesNoGroup()
    {
        var dataService = LoadFixtureSpellData();
        var comparisonService = new ComparisonService();

        // Spell 3 hat laut Fixture keine Quelle.
        var missing = new List<MissingSpellInfo> { new(3, ["Alice"]) };

        var groups = comparisonService.GroupMissingSpellsByMonster(missing, dataService);

        Assert.Empty(groups);
    }

    [Fact]
    public void GroupMissingSpellsByMonster_EmptyMissingList_ReturnsEmpty()
    {
        var dataService = LoadFixtureSpellData();
        var comparisonService = new ComparisonService();

        var groups = comparisonService.GroupMissingSpellsByMonster([], dataService);

        Assert.Empty(groups);
    }

    private static SpellDataService LoadFixtureSpellData()
    {
        var service = new SpellDataService(new TestPluginLog());
        service.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SpellData"));
        return service;
    }
}
