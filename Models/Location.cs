namespace BLUnion.Models;

public sealed class Location
{
    public required uint Id { get; init; }

    /// <summary>Zonenname in allen 4 offiziellen FFXIV-Clientsprachen (siehe <see cref="DisplayLanguage"/>),
    /// z.B. "The Aurum Vale"/"Goldklamm"/"Le Val d'Aurum". Über die FFXIV-Collect-API ermittelt -
    /// DE/FR verifiziert, JA liefert diese API für Ortsnamen NICHT (nur EN/DE/FR) und ist hier
    /// vorübergehend gleich <see cref="ZoneNameEn"/>, bis ein Lumina-basierter Export
    /// (PlaceName-Sheet) nachzieht. <see cref="DutyName"/> bleibt bewusst einsprachig (nicht Teil
    /// dieser Aufgabe).</summary>
    public required string ZoneNameDe { get; init; }

    public required string ZoneNameEn { get; init; }

    public required string ZoneNameFr { get; init; }

    public required string ZoneNameJa { get; init; }

    /// <summary>Optional: Koordinaten im Format "x, y", sofern in freier Wildbahn sinnvoll.</summary>
    public string? Coordinates { get; init; }

    /// <summary>Optional: Dungeon/Trial-Name, falls nicht in offener Welt. Bewusst nur Englisch
    /// (nicht Teil dieser Aufgabe, siehe <see cref="ZoneNameDe"/>).</summary>
    public string? DutyName { get; init; }

    /// <summary>Liefert <see cref="ZoneNameDe"/>/<see cref="ZoneNameEn"/>/<see cref="ZoneNameFr"/>/
    /// <see cref="ZoneNameJa"/> passend zur gewählten <see cref="DisplayLanguage"/>.</summary>
    public string GetZoneName(DisplayLanguage language) => language switch
    {
        DisplayLanguage.German => this.ZoneNameDe,
        DisplayLanguage.English => this.ZoneNameEn,
        DisplayLanguage.French => this.ZoneNameFr,
        DisplayLanguage.Japanese => this.ZoneNameJa,
        _ => this.ZoneNameEn,
    };
}
