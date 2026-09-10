using BLUnion.Models;
using BLUnion.Services;
using BLUnion.Tests.Fakes;
using Xunit;

namespace BLUnion.Tests;

public class SpellDataServiceTests
{
    private static SpellDataService LoadFixture()
    {
        var service = new SpellDataService(new TestPluginLog());
        var fixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SpellData");
        service.Load(fixtureDir);
        return service;
    }

    [Fact]
    public void Load_ReadsAllFixtureSpellsMonstersLocationsSources()
    {
        var service = LoadFixture();

        Assert.Equal(4, service.Spells.Count);
        Assert.Equal(3, service.Monsters.Count);
        Assert.Equal(2, service.Locations.Count);
        Assert.Equal(6, service.Sources.Count);
    }

    [Fact]
    public void GetSourcesForSpell_NormalSpell_ReturnsMonsterAndLocation()
    {
        var service = LoadFixture();

        var sources = service.GetSourcesForSpell(1).ToList();

        var source = Assert.Single(sources);
        Assert.Equal(10u, source.Monster.Id);
        Assert.NotNull(source.Location);
        Assert.Equal(100u, source.Location!.Id);
        Assert.Equal(SourceMethod.OpenWorld, source.Method);
    }

    [Fact]
    public void GetSourcesForSpell_UnknownSpellId_ReturnsEmpty()
    {
        var service = LoadFixture();

        var sources = service.GetSourcesForSpell(999999).ToList();

        Assert.Empty(sources);
    }

    [Fact]
    public void GetSourcesForSpell_SpellWithoutAnySource_ReturnsEmpty()
    {
        var service = LoadFixture();

        // Spell 3 existiert in spells.json, hat aber KEINEN Eintrag in sources.json.
        var sources = service.GetSourcesForSpell(3).ToList();

        Assert.Empty(sources);
    }

    [Fact]
    public void GetSourcesForSpell_DanglingMonsterId_SkipsSourceInsteadOfThrowing()
    {
        var service = LoadFixture();

        // Spell 5 hat einen Source-Eintrag, der auf MonsterId 999 zeigt - dieses Monster existiert
        // nicht in monsters.json. Erwartung laut Implementierung (Monsters.TryGetValue -> continue):
        // die Quelle wird stillschweigend übersprungen, kein Fehler.
        var sources = service.GetSourcesForSpell(5).ToList();

        Assert.Empty(sources);
    }

    [Fact]
    public void GetSourcesForSpell_DanglingLocationId_ReturnsSourceWithNullLocation()
    {
        var service = LoadFixture();

        // Spell 6 -> Monster 12 -> LocationId 300, das es in locations.json nicht gibt.
        var sources = service.GetSourcesForSpell(6).ToList();

        var source = Assert.Single(sources);
        Assert.Equal(12u, source.Monster.Id);
        Assert.Null(source.Location);
    }

    [Fact]
    public void GetSourcesForSpell_ExcludeTotems_FiltersOutTotemSources()
    {
        var service = LoadFixture();

        // Spell 2 hat zwei Quellen: eine OpenWorld- und eine Totem-Quelle.
        var allSources = service.GetSourcesForSpell(2).ToList();
        var nonTotemSources = service.GetSourcesForSpell(2, excludeTotems: true).ToList();

        Assert.Equal(2, allSources.Count);
        var remaining = Assert.Single(nonTotemSources);
        Assert.Equal(SourceMethod.OpenWorld, remaining.Method);
    }

    [Fact]
    public void IsOnlyLearnableViaTotem_SpellWithOnlyTotemSource_ReturnsTrue()
    {
        var service = LoadFixture();

        // Spell 4 hat ausschließlich eine Totem-Quelle.
        Assert.True(service.IsOnlyLearnableViaTotem(4));
    }

    [Fact]
    public void IsOnlyLearnableViaTotem_SpellWithMixedSources_ReturnsFalse()
    {
        var service = LoadFixture();

        // Spell 2 hat sowohl eine OpenWorld- als auch eine Totem-Quelle - "nur per Totem" trifft
        // also NICHT zu.
        Assert.False(service.IsOnlyLearnableViaTotem(2));
    }

    [Fact]
    public void IsOnlyLearnableViaTotem_SpellWithoutAnySource_ReturnsFalse()
    {
        var service = LoadFixture();

        // Grenzfall: "keine Quelle" ist NICHT dasselbe wie "nur per Totem lernbar" - beide
        // Bedingungen (GetSourcesForSpell().Any() UND !GetSourcesForSpell(excludeTotems:
        // true).Any()) referenzieren GetSourcesForSpell(), das für Spell 3 in beiden Fällen leer
        // ist, also liefert die erste Bedingung bereits false.
        Assert.False(service.IsOnlyLearnableViaTotem(3));
    }

    [Fact]
    public void IsOnlyLearnableViaTotem_UnknownSpellId_ReturnsFalse()
    {
        var service = LoadFixture();

        Assert.False(service.IsOnlyLearnableViaTotem(999999));
    }

    [Fact]
    public void Load_MissingDataFile_LogsWarningAndYieldsEmptyCollections()
    {
        // Eigenes, leeres Verzeichnis statt der Fixtures - Load() darf dabei laut Implementierung
        // NICHT werfen (siehe LoadList: File.Exists-Check -> Warning + leere Liste).
        var tempDir = Path.Combine(Path.GetTempPath(), "BLUnionTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            var service = new SpellDataService(new TestPluginLog());
            service.Load(tempDir);

            Assert.Empty(service.Spells);
            Assert.Empty(service.Monsters);
            Assert.Empty(service.Locations);
            Assert.Empty(service.Sources);
            Assert.Empty(service.Loadouts);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_MalformedJson_LogsErrorAndYieldsEmptyListInsteadOfThrowing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "BLUnionTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Gültiges JSON, aber falsche Struktur für List<Spell> (Objekt statt Array) - muss laut
            // LoadList-Implementierung als JsonException abgefangen werden (leere Liste, kein Crash).
            File.WriteAllText(Path.Combine(tempDir, "spells.json"), "{ \"not\": \"a list\" }");

            var service = new SpellDataService(new TestPluginLog());
            service.Load(tempDir);

            Assert.Empty(service.Spells);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_RealShippedData_ParsesWithoutErrors()
    {
        // Integrationstest gegen die ECHTEN Data/*.json (siehe BLUnion.Tests.csproj, LinkBase
        // "TestData") - stellt sicher, dass die tatsächlich ausgelieferten Produktivdaten zum
        // aktuellen Modell passen, unabhängig von den handgeschriebenen Grenzfall-Fixtures oben.
        var service = new SpellDataService(new TestPluginLog());
        var realDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");

        service.Load(realDataDir);

        Assert.NotEmpty(service.Spells);
        Assert.NotEmpty(service.Monsters);
        Assert.NotEmpty(service.Locations);
        Assert.NotEmpty(service.Sources);
        Assert.Equal(service.Spells.Count, service.OrderedSpellIds.Count);
    }
}
