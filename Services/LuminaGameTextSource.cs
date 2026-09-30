using BLUnion.Models;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace BLUnion.Services;

// IGameTextSource über die Spieldaten (IDataManager, Sheets in der gewünschten Client-Sprache).
// Monsternamen laufen über ISeStringEvaluator.EvaluateObjStr statt direkt über BNpcName.Singular:
// die deutschen/französischen Singular-Texte enthalten Grammatik-Platzhalter (z.B.
// "abalathisch[a] Wamoura"), die erst der Evaluator wie im Spiel auflöst.
// Fehler (fehlende Sprache, unbekannte Zeile) liefern null -> JSON-Fallback in GameTextService.
public sealed class LuminaGameTextSource : IGameTextSource
{
    private readonly IDataManager dataManager;
    private readonly ISeStringEvaluator seStringEvaluator;
    private readonly IPluginLog log;

    public LuminaGameTextSource(IDataManager dataManager, ISeStringEvaluator seStringEvaluator, IPluginLog log)
    {
        this.dataManager = dataManager;
        this.seStringEvaluator = seStringEvaluator;
        this.log = log;
    }

    public string? GetActionName(uint actionId, DisplayLanguage language) =>
        this.Read(() => this.dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>(ToClientLanguage(language))
            .GetRowOrDefault(actionId)?.Name, $"Action {actionId}");

    public string? GetAozActionDescription(uint aozActionId, DisplayLanguage language) =>
        this.Read(() => this.dataManager.GetExcelSheet<AozActionTransient>(ToClientLanguage(language))
            .GetRowOrDefault(aozActionId)?.Description, $"AozActionTransient {aozActionId}");

    public string? GetBNpcName(uint bnpcNameId, DisplayLanguage language) =>
        this.Read(() => this.seStringEvaluator.EvaluateObjStr(ObjectKind.BattleNpc, bnpcNameId, ToClientLanguage(language)),
            $"BNpcName {bnpcNameId}");

    public string? GetPlaceName(uint placeNameId, DisplayLanguage language) =>
        this.Read(() => this.dataManager.GetExcelSheet<PlaceName>(ToClientLanguage(language))
            .GetRowOrDefault(placeNameId)?.Name, $"PlaceName {placeNameId}");

    public uint? GetTerritoryPlaceNameId(uint territoryTypeId)
    {
        try
        {
            var placeNameId = this.dataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryTypeId)?.PlaceName.RowId;
            return placeNameId is > 0 ? placeNameId : null;
        }
        catch (Exception ex)
        {
            this.log.Debug(ex, $"LuminaGameTextSource: TerritoryType {territoryTypeId} nicht lesbar.");
            return null;
        }
    }

    public string? GetContentFinderConditionName(uint contentFinderConditionId, DisplayLanguage language) =>
        this.Read(() => this.dataManager.GetExcelSheet<ContentFinderCondition>(ToClientLanguage(language))
            .GetRowOrDefault(contentFinderConditionId)?.Name, $"ContentFinderCondition {contentFinderConditionId}");

    private string? Read(Func<ReadOnlySeString?> read, string what)
    {
        try
        {
            return read() is { } value ? GameTextCleaner.ToPlainText(value) : null;
        }
        catch (Exception ex)
        {
            // Nur Debug: GameTextService cacht das Ergebnis, der Fehler taucht also nicht pro Frame auf.
            this.log.Debug(ex, $"LuminaGameTextSource: {what} nicht lesbar - JSON-Fallback.");
            return null;
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
