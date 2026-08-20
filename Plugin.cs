using BLUnion.Services;
using BLUnion.UI;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;

// IUnlockState wird hier nur als Konstruktor-Parametertyp durchgereicht (siehe
// LocalSpellUnlockService für den eigentlichen, kommentierten Einsatz) - ist aber
// selbst als "experimental" markiert, daher auch hier die Warnung unterdrücken.
#pragma warning disable Dalamud001

namespace BLUnion;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/blunion";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly WindowSystem windowSystem = new("BLUnion");

    private readonly PartyService partyService;
    private readonly SpellDataService spellDataService;
    private readonly ComparisonService comparisonService;
    private readonly LocalSpellUnlockService localSpellUnlockService;
    private readonly ManualCodeSyncProvider syncProvider;
    private readonly Configuration configuration;
    private readonly LiveSyncService liveSyncService;

    private readonly MainWindow mainWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPartyList partyList,
        IDataManager dataManager,
        IObjectTable objectTable,
        IUnlockState unlockState,
        ITextureProvider textureProvider,
        IClientState clientState,
        IChatGui chatGui,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;

        // Muss vor jeder Nutzung von ECommons-Funktionalität laufen (hier: ECommons.Automation.
        // Chat.SendMessage in MainWindow.TryAutoShareToPartyChat, siehe csproj-Kommentar zur
        // PackageReference) - initialisiert u.a. ECommons' eigene Svc-Service-Zugriffe intern.
        // Keine Module angefordert (params Module[] leer gelassen): Chat.SendMessage braucht
        // keines der optionalen ECommons-Module (VfxTracking, ObjectFunctions, ...).
        ECommonsMain.Init(pluginInterface, this);

        this.partyService = new PartyService(partyList, objectTable);
        this.spellDataService = new SpellDataService(log);
        this.comparisonService = new ComparisonService();
        this.localSpellUnlockService = new LocalSpellUnlockService(log, dataManager, unlockState, objectTable);
        this.syncProvider = new ManualCodeSyncProvider(this.spellDataService);

        // Erstes Projekt-Feature mit persistenter Konfiguration (siehe Configuration.cs) - alle
        // bisherigen UI-Zustände waren bewusst nur In-Memory. GetPluginConfig() liefert beim
        // allerersten Start null, dann eine frische Configuration mit den dortigen Defaults.
        this.configuration = this.pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.configuration.Initialize(this.pluginInterface);

        this.liveSyncService = new LiveSyncService(
            this.partyService,
            this.spellDataService,
            this.localSpellUnlockService,
            this.syncProvider,
            this.configuration,
            log);

        // Statische Spell-/Monster-/Source-/Location-Daten liegen neben der Plugin-DLL.
        var dataDir = Path.Combine(this.pluginInterface.AssemblyLocation.DirectoryName!, "Data");
        log.Information($"Lade Spell-/Monster-/Source-/Location-Daten aus \"{dataDir}\".");
        this.spellDataService.Load(dataDir);

        this.mainWindow = new MainWindow(
            this.partyService,
            this.spellDataService,
            this.comparisonService,
            this.localSpellUnlockService,
            this.syncProvider,
            this.configuration,
            this.liveSyncService,
            textureProvider,
            clientState,
            chatGui,
            log);

        this.windowSystem.AddWindow(this.mainWindow);

        this.commandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Öffnet BLUnion.",
        });

        this.pluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        this.pluginInterface.UiBuilder.OpenMainUi += () => this.mainWindow.IsOpen = true;
    }

    private void OnCommand(string command, string args) => this.mainWindow.IsOpen = true;

    public void Dispose()
    {
        this.pluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        // Vor RemoveAllWindows: meldet u.a. den Feature-3-Chat-Hook ab (siehe MainWindow.Dispose),
        // falls "Als Gruppenanführer..." noch aktiviert war - sonst Speicherleck/doppeltes Feuern
        // bei einem Plugin-Reload.
        this.mainWindow.Dispose();
        this.windowSystem.RemoveAllWindows();
        this.commandManager.RemoveHandler(CommandName);

        // Entsorgt den internen HttpClient (siehe LiveSyncService.Dispose) - sonst bliebe er nach
        // einem Plugin-Reload als offener Handle bestehen.
        this.liveSyncService.Dispose();

        // Als Letztes: ECommonsMain.Init() lief zuerst im Konstruktor, .Dispose() räumt
        // entsprechend als Letztes wieder auf (u.a. die von ECommons intern gesetzten Hooks).
        ECommonsMain.Dispose();
    }
}
