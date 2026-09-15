using BLUnion.Services;
using Dalamud.Bindings.ImGui;

namespace BLUnion.UI;

public sealed partial class MainWindow
{
    private void DrawHomeTab()
    {
        var partyStatus = this.syncProvider.GetKnownPartyStatus();

        if (partyStatus.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.HomeEmptyStateText, this.displayLanguage));
            ImGui.Separator();

            if (ImGui.Button(UiStrings.Get(UiStrings.Key.DashboardGoToGroupsButton, this.displayLanguage)))
                this.pendingActiveCategoryTabId = "TabGroupFinder";

            ImGui.SameLine();

            if (ImGui.Button(UiStrings.Get(UiStrings.Key.DashboardGoToSpellbookButton, this.displayLanguage)))
                this.pendingActiveCategoryTabId = "TabCategorySpellbook";
        }
        else
        {
            var totalSpellCount = this.spellDataService.Spells.Count;
            var averagePercent = totalSpellCount > 0
                ? (int)(partyStatus.Average(p => (double)p.LearnedSpellIds.Count / totalSpellCount) * 100)
                : 0;

            this.DrawCard(
                UiStrings.Format(UiStrings.Key.HomePartyProgressFormat, this.displayLanguage, averagePercent, partyStatus.Count),
                () => ImGui.ProgressBar(averagePercent / 100f));

            var allSpellIds = this.spellDataService.Spells.Keys;
            var missing = this.comparisonService.GetCommonlyMissingSpells(allSpellIds, partyStatus);

            // GetCommonlyMissingSpells liefert bereits absteigend nach PlayersMissingIt.Count
            // sortiert (siehe ComparisonService) - das erste Ergebnis mit mehr als einem betroffenen
            // Spieler ist also automatisch das mit den meisten betroffenen Spielern; ohne ein
            // solches Ergebnis wird stattdessen einfach das oberste (= von irgendwem fehlende)
            // Ergebnis genutzt.
            var nextTarget = missing.FirstOrDefault(m => m.PlayersMissingIt.Count > 1) ?? missing.FirstOrDefault();

            if (nextTarget is not null)
            {
                var hasSpell = this.spellDataService.Spells.TryGetValue(nextTarget.SpellId, out var spell);
                var spellName = hasSpell
                    ? this.GetSpellName(spell!)
                    : UiStrings.Format(UiStrings.Key.SpellFallback, this.displayLanguage, nextTarget.SpellId);
                var sources = this.spellDataService.GetSourcesForSpell(nextTarget.SpellId, excludeTotems: false).ToList();
                var sourceSummary = this.FormatSourceSummary(sources);

                this.DrawCard(
                    UiStrings.Get(UiStrings.Key.HomeNextBestTargetHeader, this.displayLanguage),
                    () =>
                    {
                        // Größer als das Standard-Listen-Icon (24x24, siehe DrawSpellIcon) - als
                        // einzelnes, hervorgehobenes "Hero"-Icon für genau diesen einen Zielspell
                        // soll es im Vergleich zu Listenzeilen (Party Overview/Comparison/
                        // Spellbook) bewusst mehr Gewicht bekommen, ohne die Card zu dominieren.
                        this.DrawSpellIcon(hasSpell ? spell!.IconId : 0u, new System.Numerics.Vector2(40, 40));
                        ImGui.SameLine();
                        ImGui.TextWrapped(UiStrings.Format(
                            UiStrings.Key.HomeNextBestTargetFormat, this.displayLanguage, spellName, nextTarget.PlayersMissingIt.Count, sourceSummary));
                    },
                    () =>
                    {
                        if (ImGui.Button(UiStrings.Get(UiStrings.Key.HomeTrackButton, this.displayLanguage)))
                        {
                            this.pendingActiveCategoryTabId = "TabCategoryParty";
                            this.pendingActiveSubTabId = "TabLearningPlan";
                        }
                    });
            }

            if (ImGui.Button(UiStrings.Get(UiStrings.Key.DashboardGoToPartyButton, this.displayLanguage)))
                this.pendingActiveCategoryTabId = "TabCategoryParty";

            ImGui.SameLine();

            if (ImGui.Button(UiStrings.Get(UiStrings.Key.DashboardGoToSpellbookButton, this.displayLanguage)))
                this.pendingActiveCategoryTabId = "TabCategorySpellbook";

            ImGui.SameLine();

            if (ImGui.Button(UiStrings.Get(UiStrings.Key.DashboardGoToGroupsButton, this.displayLanguage)))
                this.pendingActiveCategoryTabId = "TabGroupFinder";
        }

        ImGui.Separator();
        DrawSectionGap();
        this.DrawSyncQuickActionsCard();
    }

    // Export/Import sind seit Phase 8 hinter Settings -> "Erweitert: Sync" versteckt (siehe
    // DrawSyncSection in MainWindow.Sync.cs), aber für alle ohne aktiviertes Live-Sync der einzige
    // Weg, überhaupt Party-Daten zu bekommen - deshalb hier zusätzlich direkt erreichbar, UNABHÄNGIG
    // vom Empty-State/Populated-State-Zweig oben (siehe Aufrufstelle in DrawHomeTab). Nutzt bewusst
    // dieselben ausgelagerten Methoden wie DrawSyncSection (ExportAndShareOwnStatus/
    // DrawImportCodeInput) statt die Logik zu duplizieren.
    private void DrawSyncQuickActionsCard()
    {
        this.DrawLastMessage();

        this.DrawCard(
            UiStrings.Get(UiStrings.Key.HomeSyncCardTitle, this.displayLanguage),
            () =>
            {
                if (ImGui.Button(UiStrings.Get(UiStrings.Key.DetermineAndExportButton, this.displayLanguage)))
                    this.ExportAndShareOwnStatus();

                ImGui.Separator();

                this.DrawImportCodeInput();
            });
    }
}
