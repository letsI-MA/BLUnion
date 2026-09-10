namespace BLUnion.Services;

/// <summary>
/// Ob EINE Ziel-Spell-ID einer Gruppen-Listung (siehe Models.GroupFinderGroupEntry.TargetSpellIds)
/// bereits vom lokalen Spieler gelernt wurde. Reine Daten, keine Spell-Metadaten (Name/Icon) - die
/// löst der Aufrufer (siehe UI/MainWindow.GroupFinder.cs) bei Bedarf selbst über SpellDataService
/// auf, exakt wie ComparisonService.MissingSpellInfo nur die SpellId zurückgibt.
/// </summary>
public sealed record TargetSpellStatus(uint SpellId, bool IsLearned);

/// <summary>
/// Gleicht die targetSpellIds EINER Gruppen-Listung gegen den Lernstatus DES LOKALEN Spielers ab
/// (siehe DrawGroupTargetSpellDetailPopup in UI/MainWindow.GroupFinder.cs) - bewusst als eigener,
/// kleiner Service statt diese Logik direkt in MainWindow zu duplizieren (siehe Aufgabenstellung).
///
/// Bezieht sich - anders als ComparisonService.GetCommonlyMissingSpells (Gesamtstatus ALLER
/// Party-/Sync-Mitglieder gegen ALLE bekannten Spells) - ausschließlich auf die Ziel-Liste EINER
/// einzelnen Gruppen-Listung und NUR auf den eigenen Lernstatus, nicht auf den der übrigen
/// Gruppenmitglieder (deren einzelne Spell-Stände sind für die Zielanzeige nicht relevant - die
/// Gruppe hat sich ja bereits auf ein gemeinsames Ziel geeinigt, das Popup zeigt nur, was DER
/// BETRACHTENDE Spieler davon noch beitragen muss).
/// </summary>
public sealed class GroupTargetSpellService
{
    public IReadOnlyList<TargetSpellStatus> GetTargetSpellStatus(
        IEnumerable<uint> targetSpellIds,
        IReadOnlySet<uint> localLearnedSpellIds)
    {
        return targetSpellIds
            .Select(spellId => new TargetSpellStatus(spellId, localLearnedSpellIds.Contains(spellId)))
            .ToList();
    }
}
