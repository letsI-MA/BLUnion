using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace BLUnion.Services;

public sealed record PartyMemberInfo(string Name, string World, uint ObjectId, bool IsBlueMage, byte Level);

public sealed class PartyService
{
    private const uint BlueMageClassJobId = 36;

    private readonly IPartyList partyList;
    private readonly IObjectTable objectTable;

    public PartyService(IPartyList partyList, IObjectTable objectTable)
    {
        this.partyList = partyList;
        this.objectTable = objectTable;
    }

    public IReadOnlyList<PartyMemberInfo> GetPartyMembers()
    {
        var result = new List<PartyMemberInfo>();

        if (this.partyList.Length == 0)
        {
            var localPlayer = this.objectTable.LocalPlayer;
            if (localPlayer is not null)
            {
                result.Add(new PartyMemberInfo(
                    localPlayer.Name.TextValue,
                    GetWorldName(localPlayer.HomeWorld),
                    localPlayer.EntityId,
                    localPlayer.ClassJob.RowId == BlueMageClassJobId,
                    localPlayer.Level));
            }

            return result;
        }

        foreach (var member in this.partyList)
        {
            result.Add(new PartyMemberInfo(
                member.Name.TextValue,
                GetWorldName(member.World),
                member.EntityId,
                member.ClassJob.RowId == BlueMageClassJobId,
                member.Level));
        }

        return result;
    }

    private static string GetWorldName(RowRef<World> worldRow) =>
        worldRow.ValueNullable?.Name.ToString() ?? string.Empty;

    public IReadOnlyList<PartyMemberInfo> GetBlueMagePartyMembers()
        => this.GetPartyMembers().Where(m => m.IsBlueMage).ToList();

    public string? GetLocalPlayerName() => this.objectTable.LocalPlayer?.Name.TextValue;

    public string? GetLocalPlayerWorld()
    {
        var localPlayer = this.objectTable.LocalPlayer;
        return localPlayer is null ? null : GetWorldName(localPlayer.HomeWorld);
    }

    public bool IsInParty => this.partyList.Length > 0;
}
