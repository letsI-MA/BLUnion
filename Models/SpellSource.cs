namespace BLUnion.Models;

public sealed class SpellSource
{
    public required uint SpellId { get; init; }

    public required uint MonsterId { get; init; }

    public required SourceMethod Method { get; init; }
}
