using System.Collections.Concurrent;
using BLUnion.Models;
using Dalamud.Game;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Telepo = FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo;
using UIState = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace BLUnion.Services;

// Spiel-Aufrufe für die Klick-Aktionen aus LocationActions: Duty Finder mit ausgewählter Duty öffnen
// (keine Anmeldung), Karte mit Flag öffnen, Map-Link im EIGENEN Chatfenster (IChatGui.Print, nichts wird
// gesendet) - und als einzige echte Aktion GENAU EIN Teleport pro Klick (Telepo), ohne Wiederholung und
// ohne Folgeaktionen außer dem Setzen der Kartenflagge. Wo das Spiel den Teleport verbietet (Duty, Kampf,
// Crafting ...), lehnt es ihn selbst ab - hier gibt es keine Umgehung. Siehe BLUnion.json.
public sealed class GameUiLinkService
{
    // ContentFinderCondition.ContentLinkType: 1 = InstanceContent (Dungeons, Trials, Raids).
    private const byte InstanceContentLinkType = 1;

    // MapMarker.DataType 3 = Aetheryt (DataKey = Aetheryte-Zeile).
    private const byte AetheryteMapMarkerDataType = 3;

    // TerritoryIntendedUse 13/14 = Wohngebiet außen/innen.
    private static readonly HashSet<uint> HousingIntendedUses = [13, 14];

    private readonly ConcurrentDictionary<(uint Id, DisplayLanguage Language), string?> aetheryteNames = new();

    private IReadOnlyList<AetheryteCandidate>? aetherytes;

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

    // Alle Aetheryten aus den Spieldaten, einmal geladen. Fehler -> leere Liste (Ortsnamen bleiben Text).
    public IReadOnlyList<AetheryteCandidate> GetAetherytes() => this.aetherytes ??= this.LoadAetherytes();

    private List<AetheryteCandidate> LoadAetherytes()
    {
        try
        {
            var territories = this.dataManager.GetExcelSheet<TerritoryType>();
            var maps = this.dataManager.GetExcelSheet<Map>();
            var markers = this.dataManager.GetSubrowExcelSheet<MapMarker>();
            var result = new List<AetheryteCandidate>();

            foreach (var row in this.dataManager.GetExcelSheet<Aetheryte>())
            {
                var territoryId = row.Territory.RowId;
                if (row.RowId == 0 || territoryId == 0)
                    continue;

                var kind = !row.IsAetheryte || row.Invisible ? AetheryteKind.AethernetShard
                    : territories.GetRowOrDefault(territoryId) is { } territory && HousingIntendedUses.Contains(territory.TerritoryIntendedUse.RowId)
                        ? AetheryteKind.Housing
                        : AetheryteKind.Aetheryte;

                // Position: Aetheryt-Marker der Karte (Pixel 0-2048) -> Kartenkoordinaten wie im Spiel.
                float? mapX = null, mapY = null;
                if (kind == AetheryteKind.Aetheryte && maps.GetRowOrDefault(row.Map.RowId) is { } map
                    && markers.TryGetRow(map.MapMarkerRange, out var markerRows))
                {
                    foreach (var marker in markerRows)
                    {
                        if (marker.DataType != AetheryteMapMarkerDataType || marker.DataKey.RowId != row.RowId)
                            continue;

                        var scale = map.SizeFactor / 100f;
                        mapX = (41f / scale * (marker.X / 2048f)) + 1;
                        mapY = (41f / scale * (marker.Y / 2048f)) + 1;
                        break;
                    }
                }

                result.Add(new AetheryteCandidate(row.RowId, territoryId, kind, mapX, mapY));
            }

            return result;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "GameUiLinkService: Aetheryten nicht lesbar - kein Teleport per Klick.");
            return [];
        }
    }

    // Übersetzter Name des Aetheryten (PlaceName), null ohne Spieltext.
    public string? GetAetheryteName(uint aetheryteId, DisplayLanguage language) =>
        this.aetheryteNames.GetOrAdd((aetheryteId, language), key =>
        {
            try
            {
                var name = this.dataManager.GetExcelSheet<Aetheryte>(ToClientLanguage(key.Language))
                    .GetRowOrDefault(key.Id)?.PlaceName.ValueNullable?.Name;
                var text = name is { } value ? GameTextCleaner.ToPlainText(value).Trim() : null;
                return string.IsNullOrEmpty(text) ? null : GameTextService.CapitalizeFirstLetter(text, key.Language);
            }
            catch (Exception ex)
            {
                this.log.Debug(ex, $"GameUiLinkService: Name von Aetheryte {key.Id} nicht lesbar.");
                return null;
            }
        });

    // null = nicht beurteilbar (nicht eingeloggt, Fehler) -> Name bleibt klickbar.
    public unsafe bool? IsAetheryteUnlocked(uint aetheryteId)
    {
        if (!this.clientState.IsLoggedIn)
            return null;

        try
        {
            var uiState = UIState.Instance();
            return uiState is null ? null : uiState->IsAetheryteUnlocked(aetheryteId);
        }
        catch (Exception ex)
        {
            this.log.Debug(ex, $"GameUiLinkService: Freischaltung von Aetheryte {aetheryteId} nicht prüfbar.");
            return null;
        }
    }

    // GENAU EIN Teleport-Aufruf, keine Wiederholung. false = vom Spiel abgelehnt oder nicht möglich.
    public unsafe bool Teleport(uint aetheryteId)
    {
        try
        {
            var telepo = Telepo.Instance();
            return telepo is not null && telepo->Teleport(aetheryteId, 0);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, $"GameUiLinkService: Teleport zu Aetheryte {aetheryteId} fehlgeschlagen.");
            return false;
        }
    }

    // Setzt nur die Kartenflagge (AgentMap.SetFlagMapMarker), ohne die Karte zu öffnen.
    public unsafe void SetMapFlag(MapLinkTarget target)
    {
        try
        {
            var agent = AgentMap.Instance();
            if (agent is null)
                return;

            // MapLinkPayload rechnet Karten- in Weltkoordinaten um (RawX/RawY = Welt x 1000).
            var payload = new MapLinkPayload(target.TerritoryTypeId, target.MapId, target.X, target.Y);
            agent->SetFlagMapMarker(
                target.TerritoryTypeId, target.MapId, new System.Numerics.Vector3(payload.RawX / 1000f, 0, payload.RawY / 1000f));
        }
        catch (Exception ex)
        {
            this.log.Error(ex, $"GameUiLinkService: Kartenflagge für {target} nicht zu setzen.");
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

    private static ClientLanguage ToClientLanguage(DisplayLanguage language) => language switch
    {
        DisplayLanguage.German => ClientLanguage.German,
        DisplayLanguage.French => ClientLanguage.French,
        DisplayLanguage.Japanese => ClientLanguage.Japanese,
        _ => ClientLanguage.English,
    };
}
