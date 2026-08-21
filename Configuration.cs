using Dalamud.Configuration;
using Dalamud.Plugin;

namespace BLUnion;

/// <summary>
/// Persistente Plugin-Einstellungen (Standard-Dalamud-Muster: <see cref="IPluginConfiguration"/>
/// + <see cref="IDalamudPluginInterface.GetPluginConfig"/>/<see cref="IDalamudPluginInterface.SavePluginConfig"/>,
/// verifiziert gegen die installierte Dalamud-API-15-Version). Bisher gab es im Projekt KEINE
/// persistente Konfiguration - alle bisherigen UI-Zustände (Sprache, Auto-Share-Checkbox, ...)
/// waren bewusst nur In-Memory für die laufende Sitzung. Live-Sync braucht dagegen zwingend
/// Persistenz: der Edit-Token (siehe <see cref="LiveSyncEditTokens"/>) darf nicht verloren gehen,
/// sonst kann das eigene Server-Profil nach einem Plugin-/Spiel-Neustart nicht mehr bearbeitet
/// oder gelöscht werden.
/// </summary>
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>Für künftige Migrationslogik (siehe Dalamud-Konvention) - aktuell noch keine
    /// nötig, Version 1 ist der Ausgangsstand.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Ob Live-Sync (Cloudflare-Worker-Backend, siehe LiveSyncService) aktiv ist.
    /// Bewusst Default AUS: anders als die rein lokale Auto-Share-Checkbox (Feature 2) ist das
    /// eine neue externe Server-Abhängigkeit, die der Nutzer aktiv/informiert einschalten soll
    /// (siehe UiStrings.Key.LiveSyncEnabledHint für den entsprechenden Transparenz-Hinweis in
    /// der UI - der eigene Status wird dabei unauthentifiziert per Name+World abrufbar).</summary>
    public bool LiveSyncEnabled { get; set; }

    /// <summary>Edit-Tokens für Live-Sync-Profile, Key "CharakterName@World", Value der
    /// Klartext-Token (siehe LiveSyncService/worker/README.md - der SERVER speichert nie den
    /// Klartext, nur dessen Hash; hier lokal beim Client ist der Klartext dagegen der einzige
    /// Weg, das eigene Profil später noch bearbeiten/löschen zu können).
    ///
    /// BEWUSST ein Dictionary und NICHT ein einzelnes string-Feld: ein Dalamud-Profil (und damit
    /// diese Konfigurationsdatei) kann von mehreren Charakteren auf demselben PC genutzt werden
    /// (z.B. ein Account mit mehreren Chars, die abwechselnd gespielt werden). Ein einzelnes
    /// Token-Feld würde beim Charakterwechsel den Token des zuvor gespielten Charakters
    /// überschreiben - dessen Server-Profil wäre dann dauerhaft "verwaist" (nicht mehr
    /// bearbeitbar oder löschbar, bis es nach 90 Tagen von selbst abläuft).</summary>
    public Dictionary<string, string> LiveSyncEditTokens { get; set; } = new();

    /// <summary>Die groupId der eigenen, zuletzt veröffentlichten Gruppen-Listung (Phase 2
    /// "Gruppenfinder", siehe LiveSyncService.PublishGroup/worker/src/index.ts PUT /group/:groupId),
    /// Key "CharakterName@World" des VERÖFFENTLICHENDEN Charakters (über BuildTokenKey - siehe
    /// LiveSyncEditTokens-Doc oben zur Begründung: mehrere Charaktere pro Config-Datei möglich,
    /// dasselbe Dalamud-Profil kann von mehreren Charakteren auf demselben PC genutzt werden).
    /// Value ist die groupId selbst, NICHT der Edit-Token (der steht getrennt in
    /// <see cref="GroupFinderGroupEditTokens"/>) - so lässt sich bei einem erneuten Klick auf
    /// "Gruppe veröffentlichen" prüfen, ob für den aktuellen Charakter bereits eine Gruppe
    /// existiert (dann PUT-Update auf dieselbe groupId) oder ob eine neue angelegt werden muss.</summary>
    public Dictionary<string, string> GroupFinderOwnGroupIds { get; set; } = new();

    /// <summary>Edit-Tokens der eigenen veröffentlichten Gruppen-Listungen, Key die groupId
    /// (nicht "CharakterName@World" wie bei <see cref="LiveSyncEditTokens"/>/<see cref="GroupFinderOwnGroupIds"/> -
    /// die groupId ist bereits eindeutig, ein zusätzlicher Charakter-Bezug im Key wäre hier
    /// redundant), Value der Klartext-Token (siehe LiveSyncEditTokens-Doc: der SERVER speichert
    /// nie den Klartext, nur dessen Hash - hier lokal ist der Klartext der einzige Weg, die
    /// Gruppen-Listung später noch zu aktualisieren oder zu löschen).</summary>
    public Dictionary<string, string> GroupFinderGroupEditTokens { get; set; } = new();

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    /// <summary>Muss einmalig nach dem Laden aufgerufen werden (siehe Plugin.cs) - erst danach
    /// funktioniert <see cref="Save"/>. Getrennt vom eigentlichen Laden (das läuft über
    /// <see cref="IDalamudPluginInterface.GetPluginConfig"/> in Plugin.cs), weil dieses Objekt
    /// selbst keinen Konstruktor-Zugriff auf die PluginInterface braucht, um deserialisiert zu
    /// werden.</summary>
    public void Initialize(IDalamudPluginInterface pluginInterfaceToUse)
    {
        this.pluginInterface = pluginInterfaceToUse;
    }

    public void Save()
    {
        this.pluginInterface?.SavePluginConfig(this);
    }
}
