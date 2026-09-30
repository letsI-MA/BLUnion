namespace BLUnion.Services;

// Entscheidung beim ersten Start: Live-Sync oder nur manuelle Codes (siehe UI/OnboardingWindow.cs).
// Bewusst ohne Dalamud-Abhängigkeit (getestet in BLUnion.Tests), arbeitet nur auf Configuration.
public static class Onboarding
{
    // Bestandsnutzer, die Live-Sync schon verwenden (aktiv oder mit gespeichertem Token), gelten als
    // entschieden und werden nie gefragt.
    public static bool IsExistingLiveSyncUser(Configuration configuration) =>
        configuration.LiveSyncEnabled || configuration.LiveSyncEditTokens.Count > 0;

    public static bool ShouldAsk(Configuration configuration) =>
        !configuration.OnboardingCompleted && !IsExistingLiveSyncUser(configuration);

    // Beim Laden: Bestandsnutzer einmalig als entschieden markieren, damit die Abfrage auch später
    // (z.B. nach Abschalten von Live-Sync) nie erscheint. true = Configuration wurde geändert.
    public static bool MarkExistingUserAsDecided(Configuration configuration)
    {
        if (configuration.OnboardingCompleted || !IsExistingLiveSyncUser(configuration))
            return false;

        configuration.OnboardingCompleted = true;
        return true;
    }

    // Übernimmt die Auswahl aus dem Fenster. Online-Status nur zusammen mit Live-Sync.
    public static void ApplyChoice(Configuration configuration, bool enableLiveSync, bool sharePresence)
    {
        configuration.LiveSyncEnabled = enableLiveSync;
        configuration.SharePresence = enableLiveSync && sharePresence;
        configuration.OnboardingCompleted = true;
    }
}
