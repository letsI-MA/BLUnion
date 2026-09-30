using BLUnion.Services;
using BLUnion.UI;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;

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
    private readonly OnboardingWindow onboardingWindow;
    private readonly PresenceService presenceService;

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
        IGameGui gameGui,
        ISeStringEvaluator seStringEvaluator,
        IPlayerState playerState,
        IFramework framework,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;

        ECommonsMain.Init(pluginInterface, this);

        this.partyService = new PartyService(partyList, objectTable, dataManager, playerState);
        this.spellDataService = new SpellDataService(log);
        this.comparisonService = new ComparisonService();
        this.localSpellUnlockService = new LocalSpellUnlockService(log, dataManager, unlockState, objectTable);
        this.syncProvider = new ManualCodeSyncProvider(this.spellDataService);

        this.configuration = this.pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.configuration.Initialize(this.pluginInterface);

        // Bestandsnutzer mit Live-Sync gelten als entschieden - sie sehen die Abfrage beim ersten Start nie.
        if (Onboarding.MarkExistingUserAsDecided(this.configuration))
            this.configuration.Save();

        this.liveSyncService = new LiveSyncService(
            this.partyService,
            this.spellDataService,
            this.localSpellUnlockService,
            this.syncProvider,
            this.configuration,
            log);

        var dataDir = Path.Combine(this.pluginInterface.AssemblyLocation.DirectoryName!, "Data");
        log.Information($"Lade Spell-/Monster-/Source-/Location-Daten aus \"{dataDir}\".");
        this.spellDataService.Load(dataDir);

        // Namen/Beschreibungen in der Anzeigesprache aus den Spieldaten (siehe GameTextService).
        var gameTextService = new GameTextService(new LuminaGameTextSource(dataManager, seStringEvaluator, log));

        // Duty Finder / Karte / Map-Link im eigenen Chat - öffnet nur Fenster (siehe GameUiLinkService).
        var gameUiLinkService = new GameUiLinkService(gameGui, chatGui, dataManager, unlockState, clientState, log);

        this.mainWindow = new MainWindow(
            this.partyService,
            this.spellDataService,
            gameTextService,
            gameUiLinkService,
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

        this.onboardingWindow = new OnboardingWindow(this.configuration, () => this.mainWindow.CurrentDisplayLanguage);
        this.windowSystem.AddWindow(this.onboardingWindow);

        // Online-Status-Signale des eigenen Charakters (nur mit Zustimmung, siehe PresenceService).
        this.presenceService = new PresenceService(framework, this.partyService, this.liveSyncService, this.configuration);

        this.commandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Opens BLUnion.",
        });

        this.pluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        this.pluginInterface.UiBuilder.Draw += this.mainWindow.TryAutoPublishOwnStatus;
        this.pluginInterface.UiBuilder.OpenMainUi += this.OpenMainWindow;
    }

    private void OnCommand(string command, string args) => this.OpenMainWindow();

    // Beim Öffnen die Abfrage beim ersten Start zeigen, solange noch keine Entscheidung getroffen wurde.
    private void OpenMainWindow()
    {
        this.mainWindow.IsOpen = true;

        if (Onboarding.ShouldAsk(this.configuration))
            this.onboardingWindow.IsOpen = true;
    }

    public void Dispose()
    {
        this.pluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        this.pluginInterface.UiBuilder.Draw -= this.mainWindow.TryAutoPublishOwnStatus;
        this.pluginInterface.UiBuilder.OpenMainUi -= this.OpenMainWindow;
        this.mainWindow.Dispose();
        this.windowSystem.RemoveAllWindows();
        this.commandManager.RemoveHandler(CommandName);

        // Vor LiveSyncService: schickt beim Entladen noch ein letztes offline (kurzer Timeout).
        this.presenceService.Dispose();
        this.liveSyncService.Dispose();

        ECommonsMain.Dispose();
    }
}
