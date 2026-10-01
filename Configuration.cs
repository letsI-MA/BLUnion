using BLUnion.Models;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace BLUnion;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool LiveSyncEnabled { get; set; }

    // Abfrage beim ersten Start beantwortet (siehe Services/Onboarding.cs) - Bestandsnutzer mit Live-Sync
    // werden beim Laden automatisch als entschieden markiert.
    public bool OnboardingCompleted { get; set; }

    // "Online-Status teilen" (nur zusammen mit Live-Sync, siehe PresenceService) - setzt sharePresence am Profil.
    public bool SharePresence { get; set; }

    // Einmaliger Hinweis auf die Option "Online-Status teilen" für Bestandsnutzer (siehe PresenceHints) -
    // true nach Schließen/Klick oder wenn das Onboarding die Option schon gezeigt hat.
    public bool SharePresenceNoticeDismissed { get; set; }

    public bool AutoImportSyncCodesFromPartyChat { get; set; } = true;

    // null = automatisch anhand der Client-Sprache (siehe MainWindow-Konstruktor).
    public DisplayLanguage? DisplayLanguage { get; set; } = null;

    public bool AutoShareToPartyChat { get; set; } = true;

    // Klick auf einen Open-World-Ortsnamen teleportiert zum Aetheryten (siehe GameUiLinkService.Teleport).
    public bool TeleportOnLocationClick { get; set; } = true;

    public Dictionary<string, string> LiveSyncEditTokens { get; set; } = new();

    public Dictionary<string, string> GroupFinderOwnGroupIds { get; set; } = new();

    public Dictionary<string, string> GroupFinderGroupEditTokens { get; set; } = new();

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterfaceToUse)
    {
        this.pluginInterface = pluginInterfaceToUse;
    }

    public void Save()
    {
        this.pluginInterface?.SavePluginConfig(this);
    }
}
