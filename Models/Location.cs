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

    public string GetZoneName(DisplayLanguage language) =>
        DisplayLanguageText.Select(language, this.ZoneNameDe, this.ZoneNameEn, this.ZoneNameFr, this.ZoneNameJa);
}
