namespace BLUnion.Models;

public sealed class Monster
{
    public required uint Id { get; init; }

    public required string NameDe { get; init; }

    public required string NameEn { get; init; }

    public required string NameFr { get; init; }

    public required string NameJa { get; init; }

    public string? Notes { get; init; }

    public uint LocationId { get; init; }

    public string GetName(DisplayLanguage language) =>
        DisplayLanguageText.Select(language, this.NameDe, this.NameEn, this.NameFr, this.NameJa);
}
