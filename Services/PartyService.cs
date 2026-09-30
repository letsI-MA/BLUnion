using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace BLUnion.Services;

public sealed record PartyMemberInfo(string Name, string World, uint ObjectId, bool IsBlueMage, byte Level);

public sealed class PartyService
{
    private const uint BlueMageClassJobId = 36;

    private readonly IPartyList partyList;
    private readonly IObjectTable objectTable;
    private readonly ExcelSheet<World> worldSheet;
    private readonly IPlayerState playerState;
    private readonly IDataManager dataManager;

    public PartyService(IPartyList partyList, IObjectTable objectTable, IDataManager dataManager, IPlayerState playerState)
    {
        this.partyList = partyList;
        this.objectTable = objectTable;
        this.dataManager = dataManager;
        this.playerState = playerState;
        this.worldSheet = dataManager.GetExcelSheet<World>();
    }

    // Blaumagier-Level des lokalen Charakters - auch wenn gerade ein anderer Job aktiv ist
    // (IPlayerState.GetClassJobLevel). null, solange die Spielerdaten nicht geladen sind oder der
    // Blaumagier noch nicht freigeschaltet ist (Level 0).
    public int? GetLocalBlueMageLevel()
    {
        if (!this.playerState.IsLoaded)
            return null;

        var blueMage = this.dataManager.GetExcelSheet<ClassJob>().GetRowOrDefault(BlueMageClassJobId);
        if (blueMage is null)
            return null;

        var level = this.playerState.GetClassJobLevel(blueMage.Value);
        return level > 0 ? level : null;
    }

    // Einheitliche Mitgliederliste für alle Aufrufer (Chat-Filter, Live-Sync-Party-Polling,
    // Party-Übersicht, Gruppen-Publish) - die Quelle (normale Party, Cross-World-Party oder nur der
    // lokale Spieler) ist nach außen nicht sichtbar, siehe MergePartyMembers.
    public IReadOnlyList<PartyMemberInfo> GetPartyMembers() =>
        MergePartyMembers(this.ReadPartyListMembers(), this.ReadCrossRealmMembers(), this.ReadLocalPlayer());

    // Reine Auswahl-Logik, ohne Spielzugriff testbar (siehe PartyServiceTests): IPartyList hat
    // Vorrang; ist sie leer (so verhält sie sich in einer Cross-World-Party), zählt die
    // Cross-Realm-Gruppe; ohne beides nur der lokale Spieler (Solo) - wie bisher.
    public static IReadOnlyList<PartyMemberInfo> MergePartyMembers(
        IReadOnlyList<PartyMemberInfo> partyListMembers,
        IReadOnlyList<PartyMemberInfo> crossRealmMembers,
        PartyMemberInfo? localPlayer)
    {
        if (partyListMembers.Count > 0)
            return partyListMembers;

        if (crossRealmMembers.Count > 0)
            return crossRealmMembers;

        return localPlayer is null ? [] : [localPlayer];
    }

    private List<PartyMemberInfo> ReadPartyListMembers()
    {
        var result = new List<PartyMemberInfo>();
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

    // Nur lesender Zugriff auf InfoProxyCrossRealm (FFXIVClientStructs), keine Game-Aktionen.
    // Gelesen wird nur die EIGENE Gruppe (LocalPlayerGroupIndex) - in einer Cross-World-Allianz
    // gehören die anderen Gruppen nicht zur Party (analog zu IPartyList, die in einer normalen
    // Allianz auch nur die eigene Party enthält).
    // ClientStructs-API gegen die installierte FFXIVClientStructs.dll per Reflection geprüft
    // (IsCrossRealmParty/GetGroupMemberCount/GetGroupMember, CrossRealmMember.NameString/HomeWorld/
    // ClassJobId/Level/EntityId). NICHT im Spiel verifiziert: ob LocalPlayerGroupIndex außerhalb
    // einer Allianz immer 0 ist und ob EntityId für Spieler in anderen Zonen 0/ungültig ist
    // (ObjectId wird von keinem Aufrufer für Cross-World-Mitglieder gebraucht).
    private unsafe List<PartyMemberInfo> ReadCrossRealmMembers()
    {
        var result = new List<PartyMemberInfo>();
        if (!InfoProxyCrossRealm.IsCrossRealmParty())
            return result;

        var proxy = InfoProxyCrossRealm.Instance();
        if (proxy is null)
            return result;

        int groupIndex = proxy->LocalPlayerGroupIndex;
        var memberCount = InfoProxyCrossRealm.GetGroupMemberCount(groupIndex);
        for (uint memberIndex = 0; memberIndex < memberCount; memberIndex++)
        {
            var member = InfoProxyCrossRealm.GetGroupMember(memberIndex, groupIndex);
            if (member is null)
                continue;

            var name = member->NameString;
            if (string.IsNullOrEmpty(name))
                continue;

            result.Add(new PartyMemberInfo(
                name,
                this.GetWorldName(member->HomeWorld),
                member->EntityId,
                member->ClassJobId == BlueMageClassJobId,
                member->Level));
        }

        return result;
    }

    private PartyMemberInfo? ReadLocalPlayer()
    {
        var localPlayer = this.objectTable.LocalPlayer;
        if (localPlayer is null)
            return null;

        return new PartyMemberInfo(
            localPlayer.Name.TextValue,
            GetWorldName(localPlayer.HomeWorld),
            localPlayer.EntityId,
            localPlayer.ClassJob.RowId == BlueMageClassJobId,
            localPlayer.Level);
    }

    private static string GetWorldName(RowRef<World> worldRow) =>
        worldRow.ValueNullable?.Name.ToString() ?? string.Empty;

    private string GetWorldName(short worldId) =>
        worldId > 0 && this.worldSheet.GetRowOrDefault((uint)worldId) is { } world
            ? world.Name.ToString()
            : string.Empty;

    public IReadOnlyList<PartyMemberInfo> GetBlueMagePartyMembers()
        => this.GetPartyMembers().Where(m => m.IsBlueMage).ToList();

    public string? GetLocalPlayerName() => this.objectTable.LocalPlayer?.Name.TextValue;

    public string? GetLocalPlayerWorld()
    {
        var localPlayer = this.objectTable.LocalPlayer;
        return localPlayer is null ? null : GetWorldName(localPlayer.HomeWorld);
    }

    // Auch in einer Cross-World-Party wahr (IPartyList ist dort leer) - betrifft u.a. das
    // automatische Teilen im Party-Chat (TryAutoShareToPartyChat), "/p" funktioniert dort ebenfalls.
    public bool IsInParty => this.partyList.Length > 0 || IsInCrossRealmParty();

    private static unsafe bool IsInCrossRealmParty() => InfoProxyCrossRealm.IsCrossRealmParty();
}
