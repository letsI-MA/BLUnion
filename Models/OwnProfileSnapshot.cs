namespace BLUnion.Models;

public sealed record OwnProfileSnapshot
{
    public required string DataCenter { get; init; }

    public required bool VisibleInGroupFinder { get; init; }

    public required IReadOnlyList<AvailabilityTag> AvailabilityTags { get; init; }

    public required string Note { get; init; }

    public required int WantedPlayerCount { get; init; }
}
