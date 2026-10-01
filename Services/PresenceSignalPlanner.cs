namespace BLUnion.Services;

public enum PresenceState
{
    Online,
    Offline,
}

public readonly record struct PresenceCharacter(string CharacterName, string World);

public sealed record PresenceSignal(PresenceCharacter Character, PresenceState State);

// Entscheidet, wann das Plugin welches Online-Status-Signal schickt (siehe PresenceService) - ohne
// Dalamud-Abhängigkeit, getestet in BLUnion.Tests. Wird regelmäßig mit dem gerade eingeloggten
// Charakter aufgerufen (null = ausgeloggt):
// - Login (bzw. Einstellung/Profil wird verfügbar) -> online
// - Logout (bzw. Einstellung aus / Profil weg) -> offline
// - solange eingeloggt: alle 60 Minuten online (Lebenszeichen; der Worker wertet "online" nur 75
//   Minuten lang als aktuell)
// - Charakterwechsel: offline für den alten, online für den neuen Charakter
// - Shutdown (Plugin wird entladen): ein letztes offline
// Fehlgeschlagene Signale werden nicht wiederholt - der Planer kennt keine Ergebnisse, das nächste
// reguläre Lebenszeichen korrigiert den Stand.
public sealed class PresenceSignalPlanner
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(60);

    // Charakter, für den zuletzt "online" gemeldet wurde (null = keiner).
    private PresenceCharacter? announced;
    private DateTimeOffset lastOnlineAt;

    // Bedingung für canSend (siehe PresenceService): Option an, Live-Sync aktiv, Profil (Token) vorhanden
    // und keine Änderung von sharePresence mehr unterwegs. Letzteres sorgt beim Einschalten für die
    // richtige Reihenfolge: erst das Profil-PUT mit sharePresence = true, dann sofort (nächster Tick)
    // "online" - vorher würde der Worker das Signal mit 403 ablehnen und es käme erst mit dem nächsten
    // Lebenszeichen. Beim Ausschalten ist SharePresence sofort false -> sofort "offline".
    public static bool CanSend(bool liveSyncEnabled, bool sharePresence, bool hasEditToken, bool sharePresenceUpdatePending) =>
        liveSyncEnabled && sharePresence && hasEditToken && !sharePresenceUpdatePending;

    // canSend: Einstellung "Online-Status teilen" an, Live-Sync aktiv und Profil (Token) vorhanden.
    public IReadOnlyList<PresenceSignal> Update(DateTimeOffset now, PresenceCharacter? current, Func<PresenceCharacter, bool> canSend)
    {
        var signals = new List<PresenceSignal>();

        if (this.announced is { } previous && (current != previous || !canSend(previous)))
        {
            signals.Add(new PresenceSignal(previous, PresenceState.Offline));
            this.announced = null;
        }

        if (current is { } character && canSend(character))
        {
            if (this.announced is null)
            {
                signals.Add(new PresenceSignal(character, PresenceState.Online));
                this.announced = character;
                this.lastOnlineAt = now;
            }
            else if (now - this.lastOnlineAt >= HeartbeatInterval)
            {
                signals.Add(new PresenceSignal(character, PresenceState.Online));
                this.lastOnlineAt = now;
            }
        }

        return signals;
    }

    // Plugin wird entladen: offline für den gemeldeten Charakter (falls es einen gibt).
    public PresenceSignal? Shutdown()
    {
        if (this.announced is not { } previous)
            return null;

        this.announced = null;
        return new PresenceSignal(previous, PresenceState.Offline);
    }
}
