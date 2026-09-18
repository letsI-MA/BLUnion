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

        ECommonsMain.Init(pluginInterface, this);

        this.partyService = new PartyService(partyList, objectTable);
        this.spellDataService = new SpellDataService(log);
        this.comparisonService = new ComparisonService();
        this.localSpellUnlockService = new LocalSpellUnlockService(log, dataManager, unlockState, objectTable);
        this.syncProvider = new ManualCodeSyncProvider(this.spellDataService);

        this.configuration = this.pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.configuration.Initialize(this.pluginInterface);

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
        this.mainWindow.Dispose();
        this.windowSystem.RemoveAllWindows();
        this.commandManager.RemoveHandler(CommandName);

        this.liveSyncService.Dispose();

        ECommonsMain.Dispose();
    }
}
