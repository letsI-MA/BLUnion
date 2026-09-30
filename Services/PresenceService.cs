using Dalamud.Plugin.Services;

namespace BLUnion.Services;

// Schickt die Online-Status-Signale des eigenen Charakters (Logik: PresenceSignalPlanner). Prüft
// einmal pro Sekunde im Framework-Tick, welcher Charakter eingeloggt ist - Login, Logout und
// Charakterwechsel ergeben sich daraus ohne eigene Event-Behandlung. Nur mit Einstellung
// "Online-Status teilen", aktivem Live-Sync und vorhandenem Profil (Token). Signale gehen
// fire-and-forget raus und werden bei Fehlschlag nicht wiederholt.
public sealed class PresenceService : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    // Letzter offline-Versuch beim Entladen - kurz, damit das Entladen nicht spürbar hängt.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly IFramework framework;
    private readonly PartyService partyService;
    private readonly LiveSyncService liveSyncService;
    private readonly Configuration configuration;
    private readonly PresenceSignalPlanner planner = new();

    private DateTimeOffset lastCheckAt;

    public PresenceService(IFramework framework, PartyService partyService, LiveSyncService liveSyncService, Configuration configuration)
    {
        this.framework = framework;
        this.partyService = partyService;
        this.liveSyncService = liveSyncService;
        this.configuration = configuration;

        this.framework.Update += this.OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework updatedFramework)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - this.lastCheckAt < CheckInterval)
            return;

        this.lastCheckAt = now;

        foreach (var signal in this.planner.Update(now, this.GetCurrentCharacter(), this.CanSend))
            _ = this.liveSyncService.SendPresenceAsync(signal, CancellationToken.None);
    }

    private PresenceCharacter? GetCurrentCharacter()
    {
        var name = this.partyService.GetLocalPlayerName();
        var world = this.partyService.GetLocalPlayerWorld();
        return string.IsNullOrEmpty(name) || string.IsNullOrEmpty(world) ? null : new PresenceCharacter(name, world);
    }

    private bool CanSend(PresenceCharacter character) =>
        this.configuration.LiveSyncEnabled
        && this.configuration.SharePresence
        && this.liveSyncService.HasEditToken(character.CharacterName, character.World);

    // Muss VOR LiveSyncService.Dispose laufen (siehe Plugin.Dispose) - sonst ist der HttpClient schon weg.
    public void Dispose()
    {
        this.framework.Update -= this.OnFrameworkUpdate;

        if (this.planner.Shutdown() is not { } signal || !this.CanSend(signal.Character))
            return;

        try
        {
            using var cancellation = new CancellationTokenSource(ShutdownTimeout);
            this.liveSyncService.SendPresenceAsync(signal, cancellation.Token).Wait(ShutdownTimeout);
        }
        catch
        {
            // Fehlschlag bewusst ignoriert - der Worker wertet "online" ohnehin nur 75 Minuten lang.
        }
    }
}
