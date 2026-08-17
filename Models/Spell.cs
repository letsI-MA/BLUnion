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

    public required string Name { get; init; }

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

    public string? Description { get; init; }
}
