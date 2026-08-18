namespace BLUnion.Models;

/// <summary>
/// Statische Metadaten zu einem Blue-Mage-Spell. Wird aus spells.json geladen.
/// Die Id entspricht bewusst der Action-Sheet-Id aus Lumina, damit wir Icons
/// und Namen bei Bedarf direkt aus dem Spiel nachladen/validieren können,
/// statt eigene Texte/Icons zu pflegen.
/// </summary>
public sealed class Spell
{
    /// <summary>Action-Sheet-Id (Lumina), gleichzeitig eindeutiger Schlüssel.</summary>
    public required uint Id { get; init; }

    /// <summary>Name in allen 4 offiziellen FFXIV-Clientsprachen (siehe <see cref="DisplayLanguage"/>).
    /// Bewusst 4 einzelne Felder statt eines Dictionary - spells.json bleibt damit ohne
    /// zusätzliche (De-)Serialisierungslogik lesbar/diffbar, und fehlende Übersetzungen
    /// fallen als Compile-/Deserialisierungsfehler auf statt als leerer String zur Laufzeit.</summary>
    public required string NameDe { get; init; }

    public required string NameEn { get; init; }

    public required string NameFr { get; init; }

    public required string NameJa { get; init; }

    /// <summary>1-5 Sterne Lernschwierigkeit, wie im Spellbook angezeigt.</summary>
    public int Stars { get; init; }

    /// <summary>Icon-Id des zugehörigen Action-Sheet-Eintrags (Lumina), z.B. für UI-Icons.</summary>
    public uint IconId { get; init; }

    /// <summary>Echte Spellbook-UI-Anzeigereihenfolge ("#001"-"#124"), verifiziert gegen
    /// In-Game-Tooltips + Community-Quelle, gematcht über <see cref="IconId"/> (sprachunabhängig,
    /// eindeutig). Entspricht NICHT der RowId im AozAction-Sheet - das war eine frühere,
    /// falsifizierte Annahme (siehe git-Historie) - und ist auch nicht identisch mit
    /// <see cref="Id"/>.</summary>
    public int SpellbookOrder { get; init; }

    /// <summary>Bisher nur Deutsch - Mehrsprachigkeit für die Beschreibung ist bewusst nicht
    /// Teil dieser Aufgabe (nur die Namen wurden mehrsprachig gemacht).</summary>
    public string? Description { get; init; }

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
