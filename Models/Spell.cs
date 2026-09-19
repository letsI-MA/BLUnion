namespace BLUnion.Models;

public sealed class Spell
{
    public required uint Id { get; init; }

    public required string NameDe { get; init; }

    public required string NameEn { get; init; }

    public required string NameFr { get; init; }

    public required string NameJa { get; init; }

    public int Stars { get; init; }

    public uint IconId { get; init; }

    public int SpellbookOrder { get; init; }

    public string? Description { get; init; }

    public string GetName(DisplayLanguage language) =>
        DisplayLanguageText.Select(language, this.NameDe, this.NameEn, this.NameFr, this.NameJa);
}
