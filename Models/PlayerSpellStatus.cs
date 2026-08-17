namespace BLUnion.Models;

/// <summary>
/// Der bekannte BLU-Spellstatus eines Spielers - entweder der eigene (lokal
/// ermittelt) oder der eines Party-Mitglieds (per Sync-Provider erhalten).
/// </summary>
public sealed record PlayerSpellStatus
{
    public required string CharacterName { get; init; }

    /// <summary>Ids aller gelernten Spells.</summary>
    public required HashSet<uint> LearnedSpellIds { get; init; }

    /// <summary>Zeitpunkt, zu dem dieser Status ermittelt/empfangen wurde.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True, wenn das der eigene Client ist (nicht per Sync empfangen).</summary>
    public bool IsLocalPlayer { get; init; }
}
