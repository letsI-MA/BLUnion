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

    // Game-ID aus tools/data-enrich (optional, siehe GameTextService).
    public uint? BNpcNameId { get; init; }

    // Für Sammeleinträge mit mehreren Monstern ("Goblin Fisher & Gambler") - je Teil eine ID, in
    // der Reihenfolge des Namens. Hat Vorrang vor BNpcNameId (siehe GameTextService.GetMonsterName).
    public IReadOnlyList<uint>? BNpcNameIds { get; init; }

    public string GetName(DisplayLanguage language) =>
        DisplayLanguageText.Select(language, this.NameDe, this.NameEn, this.NameFr, this.NameJa);
}
