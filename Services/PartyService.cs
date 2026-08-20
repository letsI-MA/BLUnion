using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace BLUnion.Services;

/// <summary>World-Name des Mitglieds (leerer String, falls nicht ermittelbar) - für Live-Sync
/// zwingend gebraucht (siehe LiveSyncService: Profil-Lookup-Key ist world+characterName, wichtig
/// für Cross-World-Partys, wo nicht alle Mitglieder auf derselben World stehen).</summary>
public sealed record PartyMemberInfo(string Name, string World, uint ObjectId, bool IsBlueMage, byte Level);

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

    /// <summary>Liest den World-Namen aus einem Lumina-<see cref="RowRef{T}"/> (World-Sheet) -
    /// gemeinsame Stelle für <see cref="IPlayerCharacter.HomeWorld"/> (lokaler Spieler) und
    /// <see cref="IPartyMember.World"/> (Party-Mitglieder), beide vom selben RowRef&lt;World&gt;-Typ
    /// (verifiziert gegen Dalamud.xml der installierten API-15-Version 15.0.3.2, siehe
    /// BLUnion.csproj-Kommentar zur API-Version). <see cref="RowRef{T}.ValueNullable"/> statt
    /// <see cref="RowRef{T}.Value"/>, damit eine (theoretisch mögliche) ungültige/fehlende
    /// Row-Referenz keine Exception wirft, sondern nur einen leeren Namen liefert.</summary>
    private static string GetWorldName(RowRef<World> worldRow) =>
        worldRow.ValueNullable?.Name.ToString() ?? string.Empty;

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

    /// <summary>Analog zu <see cref="GetLocalPlayerName"/>, nur für die World - ebenfalls direkt
    /// aus dem ObjectTable (nicht über die Party-Mitgliederliste), aus demselben Grund: für
    /// Live-Sync (siehe LiveSyncService) muss der Lookup-Key (world+characterName) zweifelsfrei
    /// zum EIGENEN Charakter gehören, unabhängig von Party-Reihenfolge/-Filterung.</summary>
    public string? GetLocalPlayerWorld()
    {
        var localPlayer = this.objectTable.LocalPlayer;
        return localPlayer is null ? null : GetWorldName(localPlayer.HomeWorld);
    }

    /// <summary>
    /// True, wenn der lokale Spieler aktuell Mitglied einer regulären Party ist (mindestens 1
    /// Eintrag in <see cref="IPartyList"/> - solo zählt hier bewusst NICHT als Party, anders als
    /// bei <see cref="GetPartyMembers"/>, das im Solo-Fall den eigenen Charakter als Ersatz
    /// liefert). Gebraucht für das automatische Teilen des Export-Codes im Party-Chat (siehe
    /// MainWindow.TryAutoShareToPartyChat): ein "/p "-Chatbefehl ohne Party würde vom Spiel nur
    /// mit einer Systemfehlermeldung quittiert - das wird hier im Vorfeld vermieden, statt den
    /// Fehler erst vom Spiel zurückgemeldet zu bekommen.
    /// </summary>
    public bool IsInParty => this.partyList.Length > 0;
}
