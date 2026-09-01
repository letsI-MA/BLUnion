using BLUnion.Models;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

#pragma warning disable Dalamud001

namespace BLUnion.Services;

public sealed class LocalSpellUnlockService
{
    private readonly IPluginLog log;
    private readonly IDataManager dataManager;
    private readonly IUnlockState unlockState;
    private readonly IObjectTable objectTable;

    public LocalSpellUnlockService(
        IPluginLog log,
        IDataManager dataManager,
        IUnlockState unlockState,
        IObjectTable objectTable)
    {
        this.log = log;
        this.dataManager = dataManager;
        this.unlockState = unlockState;
        this.objectTable = objectTable;
    }

    public HashSet<uint> GetLearnedSpellIds()
    {
        if (this.objectTable.LocalPlayer is null)
        {
            this.log.Warning(
                "LocalSpellUnlockService.GetLearnedSpellIds(): kein LocalPlayer vorhanden " +
                "(nicht eingeloggt?) - liefere leere Menge.");
            return new HashSet<uint>();
        }

        var learnedSpellIds = new HashSet<uint>();
        var aozActionSheet = this.dataManager.GetExcelSheet<AozAction>();

        foreach (var aozAction in aozActionSheet)
        {
            if (aozAction.RowId == 0)
                continue;

            if (!this.unlockState.IsAozActionUnlocked(aozAction))
                continue;

            var actionId = aozAction.Action.RowId;
            if (actionId != 0)
                learnedSpellIds.Add(actionId);
        }

        return learnedSpellIds;
    }

    public PlayerSpellStatus GetLocalPlayerStatus(string characterName)
    {
        return new PlayerSpellStatus
        {
            CharacterName = characterName,
            LearnedSpellIds = this.GetLearnedSpellIds(),
            IsLocalPlayer = true,
        };
    }
}
