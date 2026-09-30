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

    // Game-IDs aus tools/data-enrich (alle optional, siehe GameTextService). Coordinates bleiben
    // handgepflegt - die liefert kein Sheet.
    public uint? TerritoryTypeId { get; init; }

    public uint? MapId { get; init; }

    public uint? PlaceNameId { get; init; }

    public uint? ContentFinderConditionId { get; init; }

    public string GetZoneName(DisplayLanguage language) =>
        DisplayLanguageText.Select(language, this.ZoneNameDe, this.ZoneNameEn, this.ZoneNameFr, this.ZoneNameJa);
}
