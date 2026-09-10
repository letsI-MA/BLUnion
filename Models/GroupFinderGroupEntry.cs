namespace BLUnion.Models;

public sealed record GroupFinderGroupMember
{
    public required string World { get; init; }

    public required string CharacterName { get; init; }

    public required HashSet<uint>? LearnedSpellIds { get; init; }
}

public sealed record GroupFinderGroupEntry
{
    public required string GroupId { get; init; }

    public required IReadOnlyList<GroupFinderGroupMember> Members { get; init; }

    public required IReadOnlyList<AvailabilityTag> AvailabilityTags { get; init; }

    public required string Note { get; init; }

    public required int WantedPlayerCount { get; init; }

    // Spells, die diese Gruppe gemeinsam farmen möchte (siehe GroupTargetSpellService für den
    // Abgleich gegen den eigenen Lernstatus) - vom Ersteller beim Veröffentlichen ausgewählt
    // (DrawGroupPublishSection), unabhängig von den einzelnen Mitglieder-Spellständen.
    public required IReadOnlyList<uint> TargetSpellIds { get; init; }
}
