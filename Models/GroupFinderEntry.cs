namespace BLUnion.Models;

public sealed record GroupFinderEntry
{
    public required string CharacterName { get; init; }

    public required string World { get; init; }

    public required HashSet<uint> LearnedSpellIds { get; init; }

    public required IReadOnlyList<AvailabilityTag> AvailabilityTags { get; init; }

    public required string Note { get; init; }

    public required int WantedPlayerCount { get; init; }

    // Spells, die dieser Solo-Spieler farmen möchte (siehe DrawGroupTargetSpellDetailPopup in
    // UI/MainWindow.GroupFinder.cs für den Abgleich gegen den eigenen Lernstatus) - Pendant zu
    // GroupFinderGroupEntry.TargetSpellIds, hier vom einzelnen Spieler selbst ausgewählt statt vom
    // Gruppen-Ersteller.
    public required IReadOnlyList<uint> TargetSpellIds { get; init; }

    // Online-Status - nur gesetzt, wenn der Spieler ihn teilt (sonst beide null). LastSeen ist vom
    // Worker auf 15 Minuten gerundet.
    public bool? Online { get; init; }

    public DateTimeOffset? LastSeen { get; init; }
}
