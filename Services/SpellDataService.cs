using System.Text.Json;
using System.Text.Json.Serialization;
using BLUnion.Models;
using Dalamud.Plugin.Services;

namespace BLUnion.Services;

public sealed class SpellDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IPluginLog log;

    public SpellDataService(IPluginLog log)
    {
        this.log = log;
    }

    public IReadOnlyDictionary<uint, Spell> Spells { get; private set; } = new Dictionary<uint, Spell>();
    public IReadOnlyDictionary<uint, Monster> Monsters { get; private set; } = new Dictionary<uint, Monster>();
    public IReadOnlyDictionary<uint, Location> Locations { get; private set; } = new Dictionary<uint, Location>();
    public IReadOnlyList<SpellSource> Sources { get; private set; } = new List<SpellSource>();

    public IReadOnlyList<Loadout> Loadouts { get; private set; } = new List<Loadout>();

    public IReadOnlyList<uint> OrderedSpellIds { get; private set; } = new List<uint>();

    public void Load(string dataDirectory)
    {
        this.Spells = this.LoadDictionary<Spell>(Path.Combine(dataDirectory, "spells.json"), s => s.Id);
        this.Monsters = this.LoadDictionary<Monster>(Path.Combine(dataDirectory, "monsters.json"), m => m.Id);
        this.Locations = this.LoadDictionary<Location>(Path.Combine(dataDirectory, "locations.json"), l => l.Id);
        this.Sources = this.LoadList<SpellSource>(Path.Combine(dataDirectory, "sources.json"));
        this.Loadouts = this.LoadList<Loadout>(Path.Combine(dataDirectory, "loadouts.json"));
        this.OrderedSpellIds = this.Spells.Keys.OrderBy(id => id).ToList();

        this.log.Information(
            $"SpellDataService.Load(\"{dataDirectory}\"): {this.Spells.Count} Spells, " +
            $"{this.Monsters.Count} Monster, {this.Locations.Count} Orte, " +
            $"{this.Sources.Count} Quellen, {this.Loadouts.Count} Loadouts geladen.");
    }

    public IEnumerable<(Monster Monster, Location? Location, SourceMethod Method)> GetSourcesForSpell(uint spellId, bool excludeTotems = false)
    {
        foreach (var source in this.Sources.Where(s => s.SpellId == spellId))
        {
            if (excludeTotems && source.Method.IsTotemRelated())
                continue;

            if (!this.Monsters.TryGetValue(source.MonsterId, out var monster))
                continue;

            this.Locations.TryGetValue(monster.LocationId, out var location);
            yield return (monster, location, source.Method);
        }
    }

    public bool IsOnlyLearnableViaTotem(uint spellId) =>
        this.GetSourcesForSpell(spellId).Any() && !this.GetSourcesForSpell(spellId, excludeTotems: true).Any();

    private IReadOnlyDictionary<uint, T> LoadDictionary<T>(string path, Func<T, uint> keySelector)
    {
        var list = this.LoadList<T>(path);
        var dict = new Dictionary<uint, T>();
        foreach (var item in list)
            dict[keySelector(item)] = item;
        return dict;
    }

    private List<T> LoadList<T>(string path)
    {
        if (!File.Exists(path))
        {
            this.log.Warning(
                $"SpellDataService: Datei nicht gefunden - \"{path}\". " +
                "Liefere für diese Datei eine leere Liste statt sie stillschweigend zu ignorieren.");
            return [];
        }

        var json = File.ReadAllText(path);

        List<T>? result;
        try
        {
            result = JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            this.log.Error(
                ex,
                $"SpellDataService: \"{path}\" passt nicht zum aktuellen {typeof(T).Name}-Modell " +
                "(JSON-Struktur veraltet?). Liefere leere Liste, statt das Plugin abstürzen zu lassen.");
            return [];
        }

        if (result is null)
        {
            this.log.Warning(
                $"SpellDataService: \"{path}\" konnte nicht als JSON-Liste geparst werden " +
                "(Deserialize lieferte null). Liefere leere Liste.");
            return [];
        }

        return result;
    }
}
