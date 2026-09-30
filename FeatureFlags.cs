namespace BLUnion;

// Zentrale Schalter für ausgeblendete, aber vollständig erhaltene Features.
public static class FeatureFlags
{
    // Gruppen im Group Finder (Gruppe veröffentlichen/löschen, Gruppen durchsuchen). false: die UI blendet
    // Modus-Umschalter, Gruppen-Veröffentlichung und Gruppen-Browse aus und löst keine Worker-Aufrufe für
    // Gruppen aus (/group, /groups/browse). Einzel-Einträge bleiben unverändert. Der Gruppen-Code in
    // LiveSyncService/MainWindow.GroupFinder.cs bleibt kompiliert und getestet - zum Reaktivieren nur hier
    // auf true setzen (Gegenstücke: GROUPS_ENABLED in docs/index.html und worker/wrangler.toml).
    // static readonly statt const: vermeidet Warnungen zu unerreichbarem Code an den Aufrufstellen.
    public static readonly bool GroupsEnabled = false;
}
