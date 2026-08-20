namespace BLUnion.Models;

/// <summary>
/// Ein fremder Eintrag aus dem Gruppenfinder (Phase 2 "Live-Sync", GET /profiles/browse) - siehe
/// LiveSyncService.TriggerBrowseAsync/UI.MainWindow.DrawGroupFinderTab. Bewusst ein eigenes,
/// schlankes Modell statt direkt <see cref="PlayerSpellStatus"/> zu verwenden: der Gruppenfinder
/// liefert zusätzliche Felder (World, Verfügbarkeit, Notiz, gewünschte Mitspieleranzahl), die
/// PlayerSpellStatus nicht kennt und auch nicht kennen soll (das bleibt der reine
/// Vergleichs-Datensatz) - erst der Klick auf "In Vergleich aufnehmen" baut daraus gezielt ein
/// PlayerSpellStatus für die bestehende Merge-/Dedup-Logik (siehe MainWindow).
/// </summary>
public sealed record GroupFinderEntry
{
    public required string CharacterName { get; init; }

    public required string World { get; init; }

    /// <summary>Bereits aus spellBitmaskBase64 dekodiert (siehe
    /// ManualCodeSyncProvider.DecodeBitmask) - MainWindow zeigt daraus nur noch die Anzahl
    /// ("X/Y gelernt") an bzw. reicht die Menge unverändert an ein neues PlayerSpellStatus
    /// weiter, wenn der Nutzer "In Vergleich aufnehmen" klickt.</summary>
    public required HashSet<uint> LearnedSpellIds { get; init; }

    public required IReadOnlyList<AvailabilityTag> AvailabilityTags { get; init; }

    public required string Note { get; init; }

    /// <summary>0 = "egal wie viele" (siehe Worker-Datenmodell) - MainWindow übersetzt das beim
    /// Anzeigen in den entsprechenden Hinweistext statt einer nackten "0".</summary>
    public required int WantedPlayerCount { get; init; }
}
