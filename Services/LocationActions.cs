using System.Globalization;
using System.Text.RegularExpressions;
using BLUnion.Models;

namespace BLUnion.Services;

// Ziel für "Auf Karte markieren"/"Link in Chat" - Koordinaten wie auf der Spielkarte (z.B. 9.5, 21).
public sealed record MapLinkTarget(uint TerritoryTypeId, uint MapId, float X, float Y);

// Was zu einem Ort gehört (siehe MainWindow.LocationActions.cs):
// - DutyIds: Duties des Orts (1 = Einzel-Duty, >1 = Varianten), siehe GetNameAction.
// - MapLink: nur Open World mit Gebiet, Karte und lesbaren Koordinaten.
// - UnparsableCoordinates: Koordinaten vorhanden, aber nicht lesbar -> kein Knopf, UI loggt eine Warnung.
public sealed record LocationActionSet(IReadOnlyList<uint> DutyIds, MapLinkTarget? MapLink, bool UnparsableCoordinates)
{
    public bool IsMultiDuty => this.DutyIds.Count > 1;
}

public enum LocationNameKind
{
    // Normaler Text (kein Ziel, Teleport-Einstellung aus, Duty ohne IDs).
    PlainText,

    // Einzel-Duty: Klick öffnet den Duty Finder.
    DutyFinder,

    // Einzel-Duty, nicht freigeschaltet: nicht klickbar, nur Tooltip.
    DutyFinderLocked,

    // Mehrere Varianten: Klick öffnet ein Menü mit den Varianten.
    DutyFinderMenu,

    // Open World: Klick teleportiert zum Aetheryten.
    Teleport,

    // Open World, Aetheryt nicht freigeschaltet: nicht klickbar, nur Tooltip.
    TeleportLocked,
}

// Was ein Klick auf einen Orts-/Duty-Namen tut (siehe LocationActions.GetNameAction).
public sealed record LocationNameAction(LocationNameKind Kind, IReadOnlyList<uint> DutyIds, AetheryteCandidate? Aetheryte)
{
    public bool IsClickable => this.Kind is LocationNameKind.DutyFinder or LocationNameKind.DutyFinderMenu or LocationNameKind.Teleport;
}

// Ein Eintrag im Varianten-Menü einer Mehrfach-Duty. Enabled = false: nicht freigeschaltet (ausgegraut).
public sealed record DutyVariantMenuItem(uint ContentFinderConditionId, string Label, string FullName, bool Enabled);

// Reine Logik hinter den klickbaren Orts-/Duty-Namen und Karten-Knöpfen, bewusst ohne Dalamud-
// Abhängigkeit (testbar in BLUnion.Tests). Duty Finder und Karte werden nur GEÖFFNET (keine Anmeldung);
// der einzige Spiel-Aufruf ist ein Teleport pro Klick auf einen Open-World-Ort (abschaltbar).
public static class LocationActions
{
    // Zwei Zahlen mit Punkt als Dezimaltrenner, getrennt durch Komma/Leerzeichen/Schrägstrich, optional
    // mit "X:"/"Y:" davor: "9, 21", "9,21", "9.5, 21.3", "X: 9.5 Y: 21.3".
    private static readonly Regex CoordinatesPattern = new(
        @"^\s*(?:x\s*:?\s*)?(?<x>\d+(?:\.\d+)?)\s*[,/\s]\s*(?:y\s*:?\s*)?(?<y>\d+(?:\.\d+)?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Spielkarten-Koordinaten liegen zwischen 1 und ~42 - alles außerhalb ist ein Datenfehler.
    private const float MaxMapCoordinate = 50f;

    public static bool TryParseCoordinates(string? text, out float x, out float y)
    {
        x = y = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = CoordinatesPattern.Match(text);
        if (!match.Success
            || !float.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedX)
            || !float.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedY)
            || parsedX is <= 0 or > MaxMapCoordinate || parsedY is <= 0 or > MaxMapCoordinate)
            return false;

        (x, y) = (parsedX, parsedY);
        return true;
    }

    public static LocationActionSet Get(Location? location)
    {
        if (location is null)
            return new LocationActionSet([], null, false);

        var dutyIds = location.GetContentFinderConditionIds();

        // Karte nur für Open World - Duties haben keine Koordinaten, und ein Flag in einer Instanz wäre sinnlos.
        if (dutyIds.Count > 0 || location.DutyName is not null || location.Coordinates is null)
            return new LocationActionSet(dutyIds, null, false);

        if (!TryParseCoordinates(location.Coordinates, out var x, out var y))
            return new LocationActionSet(dutyIds, null, true);

        var mapLink = location is { TerritoryTypeId: { } territoryTypeId, MapId: { } mapId }
            ? new MapLinkTarget(territoryTypeId, mapId, x, y)
            : null;
        return new LocationActionSet(dutyIds, mapLink, false);
    }

    // Klick-Verhalten des Orts-/Duty-Namens. Duties öffnen den Duty Finder (Einzel-Duty direkt, mehrere
    // Varianten über ein Menü); Open-World-Orte teleportieren - nur mit eingeschalteter Einstellung und
    // gefundenem Aetheryten. aetheryteUnlocked: false = nicht freigeschaltet, null = nicht beurteilbar
    // (dann bleibt der Name klickbar, das Spiel lehnt einen unmöglichen Teleport selbst ab).
    // singleDutyUnlocked: dasselbe für eine Einzel-Duty (Varianten prüft das Menü einzeln, siehe GetVariantMenu).
    public static LocationNameAction GetNameAction(
        Location? location, bool teleportEnabled, AetheryteCandidate? aetheryte, bool? aetheryteUnlocked, bool? singleDutyUnlocked = null)
    {
        if (location is null)
            return new LocationNameAction(LocationNameKind.PlainText, [], null);

        var dutyIds = location.GetContentFinderConditionIds();
        if (dutyIds.Count > 1)
            return new LocationNameAction(LocationNameKind.DutyFinderMenu, dutyIds, null);

        if (dutyIds.Count == 1)
            return new LocationNameAction(singleDutyUnlocked == false ? LocationNameKind.DutyFinderLocked : LocationNameKind.DutyFinder, dutyIds, null);

        if (location.DutyName is not null || !teleportEnabled || aetheryte is null)
            return new LocationNameAction(LocationNameKind.PlainText, [], null);

        return new LocationNameAction(aetheryteUnlocked == false ? LocationNameKind.TeleportLocked : LocationNameKind.Teleport, [], aetheryte);
    }

    // Varianten-Menü: Beschriftung = auf den Unterschied gekürzter Name (siehe ShortenVariantNames),
    // Basis-Variante = normalLabel, fehlender Spieltext = fallbackLabel(Nummer ab 1).
    public static IReadOnlyList<DutyVariantMenuItem> GetVariantMenu(
        IReadOnlyList<uint> dutyIds, IReadOnlyList<string?> gameNames, Func<uint, bool?> isUnlocked, string normalLabel,
        Func<int, string> fallbackLabel)
    {
        var fullNames = dutyIds.Select((_, index) => gameNames[index] is { Length: > 0 } name ? name : fallbackLabel(index + 1)).ToList();
        var labels = ShortenVariantNames(fullNames);

        return dutyIds
            .Select((cfcId, index) => new DutyVariantMenuItem(cfcId, labels[index] ?? normalLabel, fullNames[index], isUnlocked(cfcId) != false))
            .ToList();
    }

    // Beschriftung der Varianten: der gemeinsame Anfang bis zum letzten Trenner ("(", "-", ":")
    // fällt weg, z.B. "Thornmarch (Hard)" / "Thornmarch (Extreme)" -> "Hard" / "Extreme",
    // "The Binding Coil of Bahamut - Turn 1" / "- Turn 2" -> "Turn 1" / "Turn 2". Ist ein Name genau
    // der gemeinsame Anfang (Normal-Variante "The Bowl of Embers"), wird er null -> Aufrufer zeigt
    // "Normal". Ohne Trenner (z.B. Japanisch) oder bei doppelten/leeren Ergebnissen bleiben die vollen Namen.
    public static IReadOnlyList<string?> ShortenVariantNames(IReadOnlyList<string> names)
    {
        if (names.Count < 2)
            return names;

        var prefixLength = names.Skip(1).Aggregate(names[0].Length, (length, name) => CommonPrefixLength(names[0], name, length));
        var prefix = names[0][..prefixLength];

        int cut;
        if (names.Any(name => name.Trim().Length == prefix.Trim().Length))
        {
            cut = prefixLength;
        }
        else
        {
            cut = prefix.LastIndexOfAny(Separators) + 1;
            if (cut == 0)
                return names;
        }

        var shortened = names
            .Select(name => name[Math.Min(cut, name.Length)..].Trim(TrimCharacters))
            .Select(name => name.Length == 0 ? null : name)
            .ToList();

        var nonEmpty = shortened.Where(name => name is not null).ToList();
        if (shortened.Count(name => name is null) > 1 || nonEmpty.Distinct(StringComparer.CurrentCultureIgnoreCase).Count() != nonEmpty.Count)
            return names;

        return shortened;
    }

    private static readonly char[] Separators = ['(', '（', '-', '–', ':', '：'];

    private static readonly char[] TrimCharacters = [' ', '(', ')', '（', '）', '-', '–', ':', '：'];

    private static int CommonPrefixLength(string a, string b, int max)
    {
        var length = Math.Min(max, Math.Min(a.Length, b.Length));
        for (var i = 0; i < length; i++)
        {
            if (char.ToLowerInvariant(a[i]) != char.ToLowerInvariant(b[i]))
                return i;
        }

        return length;
    }
}
