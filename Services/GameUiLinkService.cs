using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace BLUnion.Services;

// Öffnet Spielfenster für die Knöpfe aus LocationActions - und NUR das: Duty Finder mit ausgewählter
// Duty (keine Anmeldung), Karte mit Flag (kein Teleport), Map-Link im EIGENEN Chatfenster (IChatGui.Print,
// nichts wird gesendet). Siehe "no gameplay automation" in BLUnion.json.
public sealed class GameUiLinkService
{
    // ContentFinderCondition.ContentLinkType: 1 = InstanceContent (Dungeons, Trials, Raids).
    private const byte InstanceContentLinkType = 1;

    private readonly IGameGui gameGui;
    private readonly IChatGui chatGui;
    private readonly IDataManager dataManager;
    private readonly IUnlockState unlockState;
    private readonly IClientState clientState;
    private readonly IPluginLog log;

    public GameUiLinkService(
        IGameGui gameGui, IChatGui chatGui, IDataManager dataManager, IUnlockState unlockState, IClientState clientState, IPluginLog log)
    {
        this.gameGui = gameGui;
        this.chatGui = chatGui;
        this.dataManager = dataManager;
        this.unlockState = unlockState;
        this.clientState = clientState;
        this.log = log;
    }

    // null = nicht beurteilbar (nicht eingeloggt, keine InstanceContent-Duty, Fehler) -> Knopf bleibt aktiv.
    public bool? IsDutyUnlocked(uint contentFinderConditionId)
    {
        if (!this.clientState.IsLoggedIn)
            return null;

        try
        {
            if (this.dataManager.GetExcelSheet<ContentFinderCondition>().GetRowOrDefault(contentFinderConditionId) is not { } cfc
                || cfc.ContentLinkType != InstanceContentLinkType
                || this.dataManager.GetExcelSheet<InstanceContent>().GetRowOrDefault(cfc.Content.RowId) is not { } instanceContent)
                return null;

            return this.unlockState.IsInstanceContentUnlocked(instanceContent);
        }
        catch (Exception ex)
        {
            this.log.Debug(ex, $"GameUiLinkService: Freischaltung von ContentFinderCondition {contentFinderConditionId} nicht prüfbar.");
            return null;
        }
    }

    public unsafe void OpenDutyFinder(uint contentFinderConditionId)
    {
        try
        {
            var agent = AgentContentsFinder.Instance();
            if (agent is null)
            {
                this.log.Warning("GameUiLinkService: AgentContentsFinder nicht verfügbar.");
                return;
            }

            // Öffnet nur das Duty-Finder-Fenster mit dieser Duty ausgewählt - angemeldet wird nicht.
            agent->OpenRegularDuty(contentFinderConditionId);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, $"GameUiLinkService: Duty Finder für ContentFinderCondition {contentFinderConditionId} nicht zu öffnen.");
        }
    }

    public void OpenMap(MapLinkTarget target)
    {
        try
        {
            this.gameGui.OpenMapWithMapLink(new MapLinkPayload(target.TerritoryTypeId, target.MapId, target.X, target.Y));
        }
        catch (Exception ex)
        {
            this.log.Error(ex, $"GameUiLinkService: Karte für {target} nicht zu öffnen.");
        }
    }

    // Anklickbarer Map-Link nur im eigenen Chatfenster - Print sendet nichts an den Server.
    public void PrintMapLink(MapLinkTarget target, string label)
    {
        try
        {
            var message = new SeStringBuilder()
                .AddText($"{label}: ")
                .Append(SeString.CreateMapLink(target.TerritoryTypeId, target.MapId, target.X, target.Y))
                .Build();
            this.chatGui.Print(message, "BLUnion");
        }
        catch (Exception ex)
        {
            this.log.Error(ex, $"GameUiLinkService: Map-Link für {target} nicht auszugeben.");
        }
    }
}
