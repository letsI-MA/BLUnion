namespace BLUnion.Models;

/// <summary>
/// Eine mögliche Lernquelle für einen Spell. Ein Spell kann mehrere
/// SpellSource-Einträge haben (mehrere Monster/Methoden).
/// </summary>
public sealed class SpellSource
{
    public required uint SpellId { get; init; }

    public required uint MonsterId { get; init; }

    public required SourceMethod Method { get; init; }
}
