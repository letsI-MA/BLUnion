using System.Collections.Concurrent;
using BLUnion.Models;

namespace BLUnion.Services;

// Rohtexte aus den Spieldaten, bereits als Klartext (siehe GameTextCleaner). null oder leer =
// "kein Eintrag" - GameTextService fällt dann auf die JSON-Texte zurück. Eigenes Interface, damit
// die Fallback-Kette ohne Dalamud-Runtime testbar ist (siehe GameTextServiceTests).
public interface IGameTextSource
{
    string? GetActionName(uint actionId, DisplayLanguage language);

    string? GetAozActionDescription(uint aozActionId, DisplayLanguage language);

    string? GetBNpcName(uint bnpcNameId, DisplayLanguage language);

    string? GetPlaceName(uint placeNameId, DisplayLanguage language);

    uint? GetTerritoryPlaceNameId(uint territoryTypeId);

    string? GetContentFinderConditionName(uint contentFinderConditionId, DisplayLanguage language);

    // Erforderliche Klassenstufe einer Duty (ContentFinderCondition.ClassJobLevelRequired), null/0 = unbekannt.
    int? GetContentFinderConditionRequiredLevel(uint contentFinderConditionId);

    // Name eines Event-NPCs (ENpcResident).
    string? GetENpcName(uint eNpcResidentId, DisplayLanguage language);
}

// Anzeigetexte für Spells/Monster/Orte in der gewählten Sprache. Fallback-Kette:
// Spieltext (über die Game-IDs aus tools/data-enrich) -> JSON-Text der Sprache -> englischer
// JSON-Text. Leere Texte werden nie angezeigt, sondern gelten als "nicht vorhanden".
// Ergebnisse werden pro Eintrag+Sprache gecacht - die UI fragt jeden Frame, das Sheet-Lookup
// passiert trotzdem nur einmal.
public sealed class GameTextService
{
    private enum TextKind
    {
        SpellName,
        SpellDescription,
        MonsterName,
        ZoneName,
        NpcName,
        DutyName,
    }

    // ENpcResident von Wayward Gaheel Ja (Totem-Tausch in Ul'dah). Per Lumina ermittelt
    // (tools/data-enrich-Spieldaten, Stand 2026.09.15): drei ENpcResident-Zeilen heißen auf Englisch
    // "Wayward Gaheel Ja" - 1026925 und 1026957 sind in allen vier Sprachen identisch, 1026933 ist eine
    // andere Variante (FR "Mamool Ja hagard", JA "気の抜けたマムージャ族"). Genommen: die niedrigere.
    public const uint WaywardGaheelJaENpcResidentId = 1026925;

    private const string WaywardGaheelJaEnglishName = "Wayward Gaheel Ja";

    private readonly IGameTextSource source;
    private readonly ConcurrentDictionary<(TextKind Kind, uint Id, DisplayLanguage Language), string?> cache = new();
    private readonly ConcurrentDictionary<uint, int?> requiredLevelCache = new();

    public GameTextService(IGameTextSource source)
    {
        this.source = source;
    }

    public string GetSpellName(Spell spell, DisplayLanguage language) =>
        this.Resolve(TextKind.SpellName, spell.Id, language, () => spell.ActionId is { } actionId
            ? this.source.GetActionName(actionId, language)
            : null, spell.GetName(language), spell.NameEn)!;

    // Beschreibungen gibt es im JSON nur auf Deutsch - die sind für alle Sprachen der letzte
    // Fallback (bisheriges Verhalten), null nur ohne jede Beschreibung.
    public string? GetSpellDescription(Spell spell, DisplayLanguage language) =>
        this.Resolve(TextKind.SpellDescription, spell.Id, language, () => spell.AozActionId is { } aozActionId
            ? this.source.GetAozActionDescription(aozActionId, language)
            : null, spell.Description, null, capitalize: false);

    public string GetMonsterName(Monster monster, DisplayLanguage language) =>
        this.Resolve(TextKind.MonsterName, monster.Id, language, () => monster.BNpcNameIds is { Count: > 0 } bnpcNameIds
            ? this.JoinBNpcNames(bnpcNameIds, language)
            : monster.BNpcNameId is { } bnpcNameId
                ? this.source.GetBNpcName(bnpcNameId, language)
                : null, monster.GetName(language), monster.NameEn)!;

    // Sammeleintrag: alle Teile müssen auflösbar sein, sonst null -> kompletter JSON-Fallback.
    // Ein einzelner fehlender Teil würde sonst stillschweigend einen Monsternamen verschlucken; den
    // JSON-Namen der Sprache ("Goblin Fisher & Gambler") pro Teil zu zerlegen ist nicht zuverlässig.
    private string? JoinBNpcNames(IReadOnlyList<uint> bnpcNameIds, DisplayLanguage language)
    {
        var names = new List<string>(bnpcNameIds.Count);
        foreach (var bnpcNameId in bnpcNameIds)
        {
            var name = this.source.GetBNpcName(bnpcNameId, language)?.Trim();
            if (string.IsNullOrEmpty(name))
                return null;

            names.Add(CapitalizeFirstLetter(name, language));
        }

        return string.Join(" / ", names);
    }

    // Duty-Name (ContentFinderCondition) hat Vorrang vor dem Ortsnamen - das Gebiet einer Duty heißt
    // oft anders (z.B. The Dragon's Neck liegt im Gebiet "Halatali"). Ohne PlaceNameId wird der
    // Ortsname über das TerritoryType aufgelöst.
    public string GetZoneName(Location location, DisplayLanguage language) =>
        this.Resolve(TextKind.ZoneName, location.Id, language, () =>
        {
            if (location.ContentFinderConditionId is { } cfcId
                && this.source.GetContentFinderConditionName(cfcId, language) is { Length: > 0 } dutyName)
                return dutyName;

            var placeNameId = location.PlaceNameId
                ?? (location.TerritoryTypeId is { } territoryTypeId ? this.source.GetTerritoryPlaceNameId(territoryTypeId) : null);
            return placeNameId is { } id ? this.source.GetPlaceName(id, language) : null;
        }, location.GetZoneName(language), location.ZoneNameEn)!;

    // Fallback: englischer Name (es gibt keinen JSON-Text für NPCs).
    public string GetWaywardGaheelJaName(DisplayLanguage language) =>
        this.Resolve(TextKind.NpcName, WaywardGaheelJaENpcResidentId, language,
            () => this.source.GetENpcName(WaywardGaheelJaENpcResidentId, language), null, WaywardGaheelJaEnglishName)!;

    // Nur für Duties (ContentFinderConditionId(s) gesetzt), sonst null. Bei mehreren Varianten die
    // niedrigste bekannte Stufe (ab da ist der Ort lernbar). Sprachunabhängig, einmal pro Duty gecacht.
    public int? GetRequiredLevel(Location location) =>
        location.GetContentFinderConditionIds()
            .Select(cfcId => this.requiredLevelCache.GetOrAdd(cfcId, id => this.source.GetContentFinderConditionRequiredLevel(id) is > 0 and var level ? level : null))
            .Min();

    // Name einer einzelnen Duty (z.B. einer Variante für den Duty-Finder-Button), null ohne Spieltext.
    public string? GetDutyName(uint contentFinderConditionId, DisplayLanguage language) =>
        this.Resolve(TextKind.DutyName, contentFinderConditionId, language,
            () => this.source.GetContentFinderConditionName(contentFinderConditionId, language), null, null);

    private string? Resolve(
        TextKind kind, uint id, DisplayLanguage language, Func<string?> fromGame, string? jsonText, string? englishJsonText,
        bool capitalize = true) =>
        this.cache.GetOrAdd((kind, id, language), _ =>
        {
            var gameText = fromGame()?.Trim();
            if (!string.IsNullOrEmpty(gameText))
                return capitalize ? CapitalizeFirstLetter(gameText, language) : gameText;

            if (!string.IsNullOrWhiteSpace(jsonText))
                return jsonText;

            return string.IsNullOrWhiteSpace(englishJsonText) ? null : englishJsonText;
        });

    // Die Sheets liefern Namen teils klein ("the Dragon's Neck", "le Col du dragon") - im Spiel
    // steht am Anzeigebeginn ein Großbuchstabe. Japanisch hat keine Groß-/Kleinschreibung.
    internal static string CapitalizeFirstLetter(string text, DisplayLanguage language)
    {
        if (language == DisplayLanguage.Japanese || text.Length == 0 || !char.IsLower(text[0]))
            return text;

        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
