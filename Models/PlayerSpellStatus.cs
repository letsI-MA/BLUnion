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

    /// <summary>World des Charakters - NICHT immer bekannt: der manuelle "BLU:"-Sync-Code
    /// (siehe ManualCodeSyncProvider) trägt keine World-Info, ein daraus importierter oder aus
    /// DevTestFixtures geladener Status hat dieses Feld daher immer null. Nur dort gesetzt, wo
    /// die Quelle die World tatsächlich kennt: automatischer Party-Fetch über Live-Sync (siehe
    /// LiveSyncService.FetchPartyMemberProfilesAsync, Quelle PartyService/PartyMemberInfo.World)
    /// und "In Vergleich aufnehmen" im Gruppenfinder (siehe UI.MainWindow.DrawGroupFinderTab,
    /// Quelle GroupFinderEntry.World). Wird für die neue Gruppen-Veröffentlichung gebraucht
    /// (siehe LiveSyncService.PublishGroup) - nur Mitglieder mit bekannter World lassen sich in
    /// eine Gruppen-Listung aufnehmen.</summary>
    public string? World { get; init; }
}
