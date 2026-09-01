namespace BLUnion.Models;

public sealed class Location
{
    public required uint Id { get; init; }

    public required string ZoneNameDe { get; init; }

    public required string ZoneNameEn { get; init; }

    public required string ZoneNameFr { get; init; }

    public required string ZoneNameJa { get; init; }

    public string? Coordinates { get; init; }

    public string? DutyName { get; init; }

    public string GetZoneName(DisplayLanguage language) => language switch
    {
        DisplayLanguage.German => this.ZoneNameDe,
        DisplayLanguage.English => this.ZoneNameEn,
        DisplayLanguage.French => this.ZoneNameFr,
        DisplayLanguage.Japanese => this.ZoneNameJa,
        _ => this.ZoneNameEn,
    };
}
