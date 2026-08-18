namespace BLUnion.Models;

public sealed class Monster
{
    public required uint Id { get; init; }

    /// <summary>Name in allen 4 offiziellen FFXIV-Clientsprachen (siehe <see cref="DisplayLanguage"/>).
    /// Für die "Totem"/"Masked Carnivale"/"Learned First"-Pseudo-Einträge (kein echtes Monster,
    /// siehe <see cref="Notes"/>) handübersetzt; für echte Monster über die FFXIV-Collect-API
    /// (https://ffxivcollect.com) ermittelt - DE/FR verifiziert, JA liefert diese API für
    /// Monster-/Ortsnamen NICHT (nur EN/DE/FR) und ist hier vorübergehend gleich <see cref="NameEn"/>,
    /// bis ein Lumina-basierter Export (BNpcName-Sheet) nachzieht.</summary>
    public required string NameDe { get; init; }

    public required string NameEn { get; init; }

    public required string NameFr { get; init; }

    public required string NameJa { get; init; }

    /// <summary>Optionaler Hinweis, z.B. Mindestlevel oder Spawn-Bedingung.</summary>
    public string? Notes { get; init; }

    public uint LocationId { get; init; }

    /// <summary>Liefert <see cref="NameDe"/>/<see cref="NameEn"/>/<see cref="NameFr"/>/<see cref="NameJa"/>
    /// passend zur gewählten <see cref="DisplayLanguage"/>.</summary>
    public string GetName(DisplayLanguage language) => language switch
    {
        DisplayLanguage.German => this.NameDe,
        DisplayLanguage.English => this.NameEn,
        DisplayLanguage.French => this.NameFr,
        DisplayLanguage.Japanese => this.NameJa,
        _ => this.NameEn,
    };
}
