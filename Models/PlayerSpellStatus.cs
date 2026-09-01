namespace BLUnion.Models;

public sealed record PlayerSpellStatus
{
    public required string CharacterName { get; init; }

    public required HashSet<uint> LearnedSpellIds { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public bool IsLocalPlayer { get; init; }

    public string? World { get; init; }
}
