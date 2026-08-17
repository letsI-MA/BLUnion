using BLUnion.Models;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

// IUnlockState ist in Dalamud aktuell (API Level 15, siehe unten) noch als "experimental"
// markiert. Das ist der offizielle, dokumentierte Weg, um Unlock-Status abzufragen -
// im Gegensatz zu rohem, undokumentiertem FFXIVClientStructs-Feldzugriff. Trade-off:
// die Methoden-Signatur/das Verhalten könnte sich in einer künftigen Dalamud-Version
// noch ändern, bevor der Service stabilisiert wird.
#pragma warning disable Dalamud001

namespace BLUnion.Services;

/// <summary>
/// Ermittelt, welche Blue-Mage-Spells der EIGENE Client gelernt hat.
///
/// Ansatz (siehe Machbarkeitsanalyse Punkt 2):
/// Es gibt kein öffentlich dokumentiertes Bitfeld direkt in FFXIVClientStructs'
/// PlayerState/UIState für Blue-Mage-Unlocks. Es gibt aber seit Dalamud API Level 14
/// (Spielpatch 7.4) den offiziellen Service <see cref="IUnlockState"/> mit
/// <c>IsAozActionUnlocked(AozAction)</c> - laut XML-Doku im Dalamud-Release wörtlich:
/// "Determines whether the specified AozAction (Blue Mage Action) is unlocked."
///
/// Verifiziert gegen die tatsächlich lokal installierte Dalamud-Version:
///   - Dalamud.dll Version 15.0.3.2 (API Level 15, Commit 83042016d0e9996dc44c9f7fd96a8d33a5e586f2,
///     %AppData%\XIVLauncher\addon\Hooks\dev)
///   - Lumina.Excel 7.5.1 (aus Dalamud.deps.json)
///   - Dalamud.xml aus demselben Ordner enthält
///     "M:Dalamud.Plugin.Services.IUnlockState.IsAozActionUnlocked(Lumina.Excel.Sheets.AozAction)"
///     inkl. obigem Doku-Text.
///
/// Mapping AozAction -> echte Action-Id: Das "AozAction"-Sheet (siehe EXDSchema,
/// https://github.com/xivdev/EXDSchema, Branch "latest", Datei AozAction.yml) hat die
/// Felder "Action" (Link auf das reguläre Action-Sheet) und "Rank". D.h. die RowId von
/// AozAction ist nur ein Spell-Slot-Index (1-Rank-Reihenfolge), NICHT die Action-Id -
/// die tatsächliche, in Models/Spell.cs als Spell.Id verwendete Action-Sheet-Id steht
/// in aozActionRow.Action.RowId.
///
/// Bleibt offen / manuell zu prüfen: `IsAozActionUnlocked` ist als "experimental"
/// markiert (Dalamud001). Falls sich das Verhalten mit einer künftigen Dalamud-Version
/// ändert, hier zuerst https://dalamud.dev/versions/ (Changelog) und die XML-Doku im
/// jeweiligen Hooks/dev-Ordner gegenprüfen. Außerdem: das Ergebnis vor produktivem
/// Einsatz (z.B. bevor es in den Party-Vergleich einfließt) einmal manuell im Spiel
/// gegen das eigene Spellbook abgleichen.
/// </summary>
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

    /// <summary>
    /// Liefert die Ids (Action-Sheet-Ids) aller vom lokalen Spieler gelernten
    /// Blue-Mage-Spells. Liefert eine leere Menge, wenn kein Spieler eingeloggt ist.
    /// </summary>
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
                continue; // Leerzeile im Sheet.

            if (!this.unlockState.IsAozActionUnlocked(aozAction))
                continue;

            var actionId = aozAction.Action.RowId;
            if (actionId != 0)
                learnedSpellIds.Add(actionId);
        }

        return learnedSpellIds;
    }

    /// <summary>
    /// Baut daraus den vollständigen Status-Datensatz für den eigenen Charakter.
    /// </summary>
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
