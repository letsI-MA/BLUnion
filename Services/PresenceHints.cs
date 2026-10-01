namespace BLUnion.Services;

// Anzeige-Logik rund um "Online-Status teilen" (Settings-Warnung, Bestandsnutzer-Hinweis im
// Hauptfenster) - ohne Dalamud-Abhängigkeit, getestet in BLUnion.Tests (PresenceTests).
public static class PresenceHints
{
    // Settings: Option an, aber der eigene Eintrag ist nicht gelistet (oder es gibt noch kein Profil) -
    // dann sieht niemand den Status. listed = null: Stand noch unbekannt (noch kein Push in dieser
    // Sitzung) -> keine Warnung, statt eine womöglich falsche zu zeigen.
    public static bool ShowNotListedWarning(Configuration configuration, bool hasProfile, bool? listed) =>
        configuration.LiveSyncEnabled && configuration.SharePresence && (!hasProfile || listed == false);

    // Einmaliger Hinweis für Bestandsnutzer, die das Onboarding (und damit die Option) nie gesehen haben:
    // nur mit Live-Sync (ohne lässt sich die Option nicht einschalten), nicht wenn sie schon aktiv ist,
    // nicht solange das Onboarding noch aussteht und nie wieder nach Schließen/Klick.
    public static bool ShowNewOptionNotice(Configuration configuration) =>
        !configuration.SharePresenceNoticeDismissed
        && !configuration.SharePresence
        && configuration.LiveSyncEnabled
        && !Onboarding.ShouldAsk(configuration);

    public static void DismissNewOptionNotice(Configuration configuration) =>
        configuration.SharePresenceNoticeDismissed = true;
}
