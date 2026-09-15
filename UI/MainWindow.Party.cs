using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;

namespace BLUnion.UI;

public sealed partial class MainWindow
{
    private void DrawPartyOverviewTab()
    {
        this.DrawLastMessage();

        var partyStatus = this.syncProvider.GetKnownPartyStatus();

        if (partyStatus.Count == 0)
        {
            this.DrawEmptyState(
                UiStrings.Format(UiStrings.Key.NoPlayerDataLoaded, this.displayLanguage, UiStrings.Get(UiStrings.Key.TabSettings, this.displayLanguage)),
                (UiStrings.Get(UiStrings.Key.DashboardGoToSettingsButton, this.displayLanguage), () => this.pendingActiveCategoryTabId = "TabSettings"));
            return;
        }

        var totalSpellCount = this.spellDataService.Spells.Count;

        this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.PartyOverviewHeader, this.displayLanguage));
        ImGui.Separator();
        DrawSectionGap();

        const ImGuiTableFlags tableFlags =
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;

        if (ImGui.BeginTable("PartyOverviewTable", 3, tableFlags, new System.Numerics.Vector2(0, ScrollingTableHeight)))
        {
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnPlayer, this.displayLanguage));
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnProgress, this.displayLanguage), ImGuiTableColumnFlags.WidthFixed, 100);
            // Dritte Spalte ohne eigene Überschrift (gleiches Muster wie die Icon-Spalte in
            // DrawComparisonTab/DrawSpellbookTab) - der Zellentext "X fehlend" ist als kompaktes
            // Badge bereits selbsterklärend, siehe Aufgabenstellung.
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            foreach (var status in partyStatus)
                this.DrawPartyOverviewPlayerRow(status, totalSpellCount);

            ImGui.EndTable();
        }
    }

    // "allSpellIds minus LearnedSpellIds" direkt hier berechnet (siehe Aufgabenstellung: keine neue
    // Service-Methode nötig, PlayerSpellStatus.LearnedSpellIds ist bereits Teil von
    // GetKnownPartyStatus()) - analog zur bestehenden Fortschrittsberechnung in DrawStatusBar, nur
    // pro Party-Mitglied statt für den lokalen Spieler.
    private void DrawPartyOverviewPlayerRow(PlayerSpellStatus status, int totalSpellCount)
    {
        var missingSpells = this.spellDataService.Spells.Values
            .Where(s => !status.LearnedSpellIds.Contains(s.Id))
            .OrderBy(s => s.SpellbookOrder)
            .ToList();

        var name = status.CharacterName;
        if (status.IsLocalPlayer)
            name += UiStrings.Get(UiStrings.Key.YouSuffix, this.displayLanguage);

        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        // SpanFullWidth: die gesamte Zeile (nicht nur das Namens-Label) ist klickbar, siehe
        // Aufgabenstellung "Der Badge/die Zeile ist ... aufklappbar".
        var expanded = ImGui.TreeNodeEx($"{name}##PartyOverviewNode{status.CharacterName}", ImGuiTreeNodeFlags.SpanFullWidth);

        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(UiStrings.Format(
            UiStrings.Key.StatusBarProgressFormat, this.displayLanguage, status.LearnedSpellIds.Count, totalSpellCount));

        ImGui.TableSetColumnIndex(2);
        ImGui.TextUnformatted(UiStrings.Format(
            UiStrings.Key.PartyOverviewMissingCountFormat, this.displayLanguage, missingSpells.Count));

        if (!expanded)
            return;

        if (missingSpells.Count == 0)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            this.DrawHintText(UiStrings.Get(UiStrings.Key.PartyOverviewNoMissingSpells, this.displayLanguage));
        }
        else
        {
            foreach (var spell in missingSpells)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                this.DrawSpellIcon(spell.IconId);
                ImGui.SameLine();

                var label = this.GetSpellName(spell) + $"##PartyMissingSpell{status.CharacterName}_{spell.Id}";
                if (ImGui.Selectable(label, false, ImGuiSelectableFlags.SpanAllColumns))
                    this.JumpToSpellInSpellbook(spell.Id);
            }
        }

        ImGui.TreePop();
    }
}
