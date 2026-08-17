namespace BLUnion.Models;

/// <summary>
/// Eine mögliche Lernquelle für einen Spell. Ein Spell kann mehrere
/// SpellSource-Einträge haben (mehrere Monster/Methoden).
/// </summary>
public sealed class SpellSource
{
    public required uint SpellId { get; init; }

    public required uint MonsterId { get; init; }

    /// <summary>z.B. "Open World", "Dungeon-Trash", "Totem", "Trial-Boss".</summary>
    public required string Method { get; init; }
}
