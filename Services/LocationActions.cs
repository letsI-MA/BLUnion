using System.Globalization;
using System.Text.RegularExpressions;
using BLUnion.Models;

namespace BLUnion.Services;

// Ziel für "Auf Karte markieren"/"Link in Chat" - Koordinaten wie auf der Spielkarte (z.B. 9.5, 21).
public sealed record MapLinkTarget(uint TerritoryTypeId, uint MapId, float X, float Y);

// Welche Knöpfe zu einem Ort gehören (siehe MainWindow.LocationActions.cs):
// - DutyIds: 0 = kein Duty-Finder-Knopf, 1 = "Im Duty Finder öffnen", >1 = ein Knopf je Variante.
// - MapLink: nur Open World mit Gebiet, Karte und lesbaren Koordinaten.
// - UnparsableCoordinates: Koordinaten vorhanden, aber nicht lesbar -> kein Knopf, UI loggt eine Warnung.
public sealed record LocationActionSet(IReadOnlyList<uint> DutyIds, MapLinkTarget? MapLink, bool UnparsableCoordinates)
{
    public bool IsMultiDuty => this.DutyIds.Count > 1;
}

// Reine Logik hinter den Duty-Finder-/Karten-Knöpfen, bewusst ohne Dalamud-Abhängigkeit (testbar in
// BLUnion.Tests). Die Knöpfe ÖFFNEN nur Fenster/Karten - Anmelden, Teleportieren o.ä. gibt es nicht.
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

    // Beschriftung der Varianten-Knöpfe: der gemeinsame Anfang bis zum letzten Trenner ("(", "-", ":")
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
