using BLUnion.Services;

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

    // Sammeleinträge mit mehreren Duty-Varianten ("Thornmarch (Hard oder Extreme)") - je Variante
    // eine ID. ContentFinderConditionId bleibt dann leer, damit der Anzeigename der Sammelname bleibt
    // (siehe GameTextService.GetZoneName).
    public IReadOnlyList<uint>? ContentFinderConditionIds { get; init; }

    // Masked-Carnivale-Stage (Nummer im Spiel). Die Stages haben keine eigenen Game-IDs - der
    // Anzeigename kommt dann aus dem UiStrings-Template (MaskedCarnivaleStageFormat) statt aus den
    // ZoneName-Feldern, damit alle Stages in allen Sprachen gleich aussehen.
    public int? MaskedCarnivaleStage { get; init; }

    // Alle Duties dieses Orts: die Varianten, sonst die Einzel-Duty, sonst keine.
    public IReadOnlyList<uint> GetContentFinderConditionIds() =>
        this.ContentFinderConditionIds is { Count: > 0 } ids ? ids
        : this.ContentFinderConditionId is { } id ? [id]
        : [];

    public string GetZoneName(DisplayLanguage language) =>
        this.MaskedCarnivaleStage is { } stage
            ? UiStrings.Format(UiStrings.Key.MaskedCarnivaleStageFormat, language, stage)
            : DisplayLanguageText.Select(language, this.ZoneNameDe, this.ZoneNameEn, this.ZoneNameFr, this.ZoneNameJa);
}
