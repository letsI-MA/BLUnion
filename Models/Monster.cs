namespace BLUnion.Models;

public sealed class Monster
{
    public required uint Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Optionaler Hinweis, z.B. Mindestlevel oder Spawn-Bedingung.</summary>
    public string? Notes { get; init; }

    public uint LocationId { get; init; }
}
