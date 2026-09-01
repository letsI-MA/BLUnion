namespace BLUnion.Models;

public sealed record GroupFinderEntry
{
    public required string CharacterName { get; init; }

    public required string World { get; init; }

    public required HashSet<uint> LearnedSpellIds { get; init; }

    public required IReadOnlyList<AvailabilityTag> AvailabilityTags { get; init; }

    public required string Note { get; init; }

    public required int WantedPlayerCount { get; init; }
}
