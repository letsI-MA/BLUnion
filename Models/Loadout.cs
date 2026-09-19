namespace BLUnion.Models;

public enum LoadoutContentType
{
    MaskedCarnivale,
    Fates,
}

public sealed class Loadout
{
    public required string Id { get; init; }

    public required LoadoutContentType ContentType { get; init; }

    public required string NameDe { get; init; }

    public required string NameEn { get; init; }

    public required string NameFr { get; init; }

    public required string NameJa { get; init; }

    public required List<uint> SpellIds { get; init; }

    public string? Description { get; init; }

    public string? SourceNote { get; init; }

    public string? SourceUrl { get; init; }

    public string GetName(DisplayLanguage language) =>
        DisplayLanguageText.Select(language, this.NameDe, this.NameEn, this.NameFr, this.NameJa);
}
