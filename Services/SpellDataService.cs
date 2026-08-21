using System.Text.Json;
using System.Text.Json.Serialization;
using BLUnion.Models;
using Dalamud.Plugin.Services;

namespace BLUnion.Services;

/// <summary>
/// Lädt die kuratierten Spell/Monster/Source/Location-Daten aus den mit dem
/// Plugin ausgelieferten JSON-Dateien (Data/*.json). Diese sind bewusst
/// getrennt von den Nutzer-Settings (siehe Machbarkeitsanalyse Punkt 6).
/// </summary>
public sealed class SpellDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // SpellSource.Method ist als Enum (SourceMethod) modelliert, sources.json enthält den
        // Namen als String (z.B. "OpenWorld") - ohne diesen Converter würde Deserialize hier
        // scheitern.
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

    /// <summary>Kuratierte Spell-Empfehlungen pro Content-Typ (siehe UI/MainWindow.cs
    /// DrawLoadoutsTab). Anders als Spells/Monster/Orte/Quellen bewusst NICHT automatisiert
    /// befüllt - Data/loadouts.json wird manuell vom Projektinhaber gepflegt, siehe dortigen
    /// Kommentar.</summary>
    public IReadOnlyList<Loadout> Loadouts { get; private set; } = new List<Loadout>();

    /// <summary>Alle bekannten Spell-Ids, AUFSTEIGEND nach Id sortiert (nicht SpellbookOrder) -
    /// die kanonische Bit-Reihenfolge für das kompakte "BLU:"-Sync-Codeformat (Bit-Index 0 =
    /// kleinste Id, siehe <see cref="ManualCodeSyncProvider"/>). Export UND Import müssen beide
    /// über diese Property gehen, sonst laufen die Bitmasken zwischen beiden auseinander - und
    /// sie muss byte-genau mit der Sortierung der Web-Companion-Implementierung übereinstimmen.</summary>
    public IReadOnlyList<uint> OrderedSpellIds { get; private set; } = new List<uint>();

    public void Load(string dataDirectory)
    {
        this.Spells = this.LoadDictionary<Spell>(Path.Combine(dataDirectory, "spells.json"), s => s.Id);
        this.Monsters = this.LoadDictionary<Monster>(Path.Combine(dataDirectory, "monsters.json"), m => m.Id);
        this.Locations = this.LoadDictionary<Location>(Path.Combine(dataDirectory, "locations.json"), l => l.Id);
        this.Sources = this.LoadList<SpellSource>(Path.Combine(dataDirectory, "sources.json"));
        this.Loadouts = this.LoadList<Loadout>(Path.Combine(dataDirectory, "loadouts.json"));
        this.OrderedSpellIds = this.Spells.Keys.OrderBy(id => id).ToList();

        // Bewusst als Information geloggt (nicht nur bei Fehlern): eine leere Zahl
        // hier ist der schnellste Hinweis darauf, dass z.B. der Comparison-Tab leer
        // bleibt, weil keine Spells geladen wurden - statt darüber rätseln zu müssen.
        this.log.Information(
            $"SpellDataService.Load(\"{dataDirectory}\"): {this.Spells.Count} Spells, " +
            $"{this.Monsters.Count} Monster, {this.Locations.Count} Orte, " +
            $"{this.Sources.Count} Quellen, {this.Loadouts.Count} Loadouts geladen.");
    }

    /// <summary>Alle bekannten Quellen (Monster + Fundort) für einen Spell, sofern vorhanden.
    /// Mit <paramref name="excludeTotems"/> = true werden alle totem-bezogenen Quellen
    /// (<see cref="SourceMethodExtensions.IsTotemRelated"/>) ausgelassen - für den
    /// "Totems ausblenden"-Filter in Comparison-/Lernplan-Tab. Ein Spell, der NUR über ein
    /// Totem lernbar ist, liefert dann eine leere Quellenliste (siehe MainWindow.FormatSourceSummary).</summary>
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

    /// <summary>True, wenn ein Spell zwar (ohne Totem-Filter) mindestens eine bekannte Quelle
    /// hat, aber ALLE davon totem-bezogen sind (<see cref="SourceMethodExtensions.IsTotemRelated"/>)
    /// - der Spell also nur über ein Totem lernbar ist. Spells OHNE jegliche bekannte Quelle
    /// (unabhängig vom Totem-Filter, z.B. Datenlücken) liefern hier bewusst false - das ist ein
    /// anderer Fall (fehlende Daten) und soll vom "Totems ausblenden"-Filter NICHT betroffen
    /// sein, siehe MainWindow.DrawComparisonTab.</summary>
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
            // Passiert derzeit übergangsweise für spells.json: Models/Spell.cs wurde schon auf
            // NameDe/NameEn/NameFr/NameJa umgestellt, die ausgelieferte spells.json hat aber noch
            // das alte "Name"-Feld, bis sie manuell (siehe TEMP-Export-Command) aktualisiert wird.
            // Bewusst NICHT das ganze Plugin daran abstürzen lassen (JsonException aus
            // Deserialize würde sonst ungefangen bis in den Plugin-Konstruktor durchschlagen) -
            // stattdessen hier klar loggen und mit leerer Liste weitermachen.
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
