namespace BLUnion.Models;

/// <summary>
/// Ein einzelnes Mitglied einer Gruppen-Listung aus GET /groups/browse (siehe
/// LiveSyncService.TriggerGroupBrowseAsync/worker/src/index.ts handleGroupsBrowse).
/// </summary>
public sealed record GroupFinderGroupMember
{
    public required string World { get; init; }

    public required string CharacterName { get; init; }

    /// <summary>Bereits aus spellBitmaskBase64 dekodiert (siehe ManualCodeSyncProvider.DecodeBitmask),
    /// ANDERS als bei <see cref="GroupFinderEntry.LearnedSpellIds"/> aber NULLABLE: null, wenn der
    /// Worker beim Browse kein Einzelprofil zu diesem Mitglied finden konnte (gelöscht/abgelaufen,
    /// siehe worker handleGroupsBrowse - spellBitmaskBase64 dort explizit null statt eines Strings).
    /// MUSS als "unbekannt" behandelt werden, NICHT als leere HashSet: ein Mitglied ohne Profil
    /// kennt nicht etwa "keine Spells" - es ist schlicht nicht auswertbar. Eine leere HashSet würde
    /// die "gemeinsam fehlend"-Berechnung in MainWindow.DrawGroupFinderTab verfälschen (das
    /// Mitglied würde fälschlich als "kennt gar nichts" eingerechnet und jeder Spell erschiene als
    /// gemeinsam fehlend) - solche Mitglieder werden dort daher komplett aus der Berechnung
    /// ausgeschlossen statt mit einer leeren Menge eingerechnet.</summary>
    public required HashSet<uint>? LearnedSpellIds { get; init; }
}

/// <summary>
/// Ein fremder Gruppen-Eintrag aus dem Gruppenfinder (Phase 2 "Live-Sync", GET /groups/browse) -
/// siehe LiveSyncService.TriggerGroupBrowseAsync/UI.MainWindow.DrawGroupFinderTab. Eigenständiger,
/// zu <see cref="GroupFinderEntry"/> (Einzelprofile) PARALLELER Datenpfad - beide bleiben zwei
/// getrennte Listen im UI, keine Zusammenführung.
/// </summary>
public sealed record GroupFinderGroupEntry
{
    public required string GroupId { get; init; }

    public required IReadOnlyList<GroupFinderGroupMember> Members { get; init; }

    public required IReadOnlyList<AvailabilityTag> AvailabilityTags { get; init; }

    public required string Note { get; init; }

    /// <summary>0 = "egal wie viele" (siehe Worker-Datenmodell) - MainWindow übersetzt das beim
    /// Anzeigen in den entsprechenden Hinweistext statt einer nackten "0".</summary>
    public required int WantedPlayerCount { get; init; }
}
