namespace BLUnion.Models;

/// <summary>
/// Der zuletzt vom Worker über einen erfolgreichen Push zurückgemeldete Stand des EIGENEN
/// Live-Sync-Profils (siehe LiveSyncService.LastKnownOwnProfile) - insbesondere für
/// UI.MainWindow.DrawGroupFinderTab gebraucht, um das Data Center anzuzeigen, OHNE es ein
/// zweites Mal lokal aus der World herzuleiten (siehe Aufgabenstellung Phase 2: das würde die
/// World->DC-Zuordnung aus worker/src/worlds.ts im C#-Code duplizieren).
///
/// Bewusst NUR aus einer Server-ANTWORT befüllt (PUT-Response), nie aus lokal im UI gesetzten
/// Werten vorweggenommen - so bleibt es tatsächlich "der zuletzt BEKANNTE", nicht "der zuletzt
/// GEWÜNSCHTE" Stand (die UI-Initialisierung in MainWindow nutzt genau diesen Unterschied, um
/// die Checkbox/Tags/Notiz beim ersten Öffnen des Gruppenfinder-Tabs korrekt aus dem tatsächlich
/// gespeicherten Serverstand vorzubelegen statt aus Programmstart-Defaults).
/// </summary>
public sealed record OwnProfileSnapshot
{
    public required string DataCenter { get; init; }

    public required bool VisibleInGroupFinder { get; init; }

    public required IReadOnlyList<AvailabilityTag> AvailabilityTags { get; init; }

    public required string Note { get; init; }

    public required int WantedPlayerCount { get; init; }
}
