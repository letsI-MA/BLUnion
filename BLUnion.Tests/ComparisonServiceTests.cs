using BLUnion.Models;
using BLUnion.Services;
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
}
