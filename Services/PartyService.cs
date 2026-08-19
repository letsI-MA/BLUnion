using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;

namespace BLUnion.Services;

public sealed record PartyMemberInfo(string Name, uint ObjectId, bool IsBlueMage, byte Level);

/// <summary>
/// Liest die aktuelle Party und filtert Blue Mages heraus.
/// Nutzt ausschließlich stabile, offizielle Dalamud-APIs (IPartyList, IObjectTable).
/// </summary>
public sealed class PartyService
{
    /// <summary>
    /// ClassJob-Sheet-RowId für Blue Mage.
    ///
    /// War vorher ein Textabgleich über die Abbreviation ("BLU"), das war
    /// sprachabhängig und brach auf nicht-englischen Clients: im deutschen Client
    /// heißt die Abkürzung "BMA" (Name "Blaumagier"), nicht "BLU" - siehe Bugreport
    /// vom 2026-08-17 (Exception "Konnte ClassJob 'BLU' nicht im ClassJob-Sheet
    /// finden.") und den daraufhin geloggten Dump aller ClassJob-Rows, der RowId 36
    /// mit Abbreviation="BMA"/Name="Blaumagier" auf dem deutschen Client zeigte.
    /// ClassJob-RowIds sind sprachunabhängig und ändern sich im Spiel nicht mehr,
    /// nachdem sie vergeben wurden - daher hier als Konstante statt per Text-Suche.
    /// </summary>
    private const uint BlueMageClassJobId = 36;

    private readonly IPartyList partyList;
    private readonly IObjectTable objectTable;

    public PartyService(IPartyList partyList, IObjectTable objectTable)
    {
        this.partyList = partyList;
        this.objectTable = objectTable;
    }

    /// <summary>
    /// Liefert alle aktuellen Party-Mitglieder inkl. Blue-Mage-Flag.
    /// Wenn man solo/nicht in einer Party ist, wird nur der eigene Charakter geliefert.
    /// </summary>
    public IReadOnlyList<PartyMemberInfo> GetPartyMembers()
    {
        var result = new List<PartyMemberInfo>();

        if (this.partyList.Length == 0)
        {
            // Solo: nur eigenen Charakter berücksichtigen.
            var localPlayer = this.objectTable.LocalPlayer;
            if (localPlayer is not null)
            {
                result.Add(new PartyMemberInfo(
                    localPlayer.Name.TextValue,
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
                member.EntityId,
                member.ClassJob.RowId == BlueMageClassJobId,
                member.Level));
        }

        return result;
    }

    public IReadOnlyList<PartyMemberInfo> GetBlueMagePartyMembers()
        => this.GetPartyMembers().Where(m => m.IsBlueMage).ToList();

    /// <summary>
    /// Liefert den Namen des EIGENEN Charakters direkt aus dem ObjectTable - unabhängig von der
    /// Party-Reihenfolge/Blue-Mage-Filterung. Bewusst NICHT mehr über
    /// GetPartyMembers().FirstOrDefault(m => m.IsBlueMage) ermittelt (siehe Bugreport
    /// 2026-08-19): das griff bei mehreren Blue Mages in der Party fälschlich den ERSTEN Blue
    /// Mage der Party-Liste statt des eigenen Charakters - dadurch bekamen exportierte Sync-
    /// Codes teils den Namen eines anderen Party-Mitglieds, wodurch beim Import mehrere Codes
    /// unter demselben (falschen) Namen landeten und sich gegenseitig überschrieben, statt sich
    /// als separate Einträge zu addieren.
    /// </summary>
    public string? GetLocalPlayerName() => this.objectTable.LocalPlayer?.Name.TextValue;
}
