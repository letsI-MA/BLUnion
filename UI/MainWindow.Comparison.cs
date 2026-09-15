using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;

namespace BLUnion.UI;

public sealed partial class MainWindow
{
    private void DrawComparisonTab()
    {
        this.DrawLastMessage();

        var allSpellIds = this.spellDataService.Spells.Keys;
        var partyStatus = this.syncProvider.GetKnownPartyStatus();

        if (partyStatus.Count == 0)
        {
            this.DrawEmptyState(
                UiStrings.Format(UiStrings.Key.NoPlayerDataLoaded, this.displayLanguage, UiStrings.Get(UiStrings.Key.TabSettings, this.displayLanguage)),
                (UiStrings.Get(UiStrings.Key.DashboardGoToSettingsButton, this.displayLanguage), () => this.pendingActiveCategoryTabId = "TabSettings"));
            return;
        }

        var missing = this.comparisonService.GetCommonlyMissingSpells(allSpellIds, partyStatus)
            .OrderByDescending(m => m.PlayersMissingIt.Count)
            .ThenBy(m => this.spellDataService.Spells.TryGetValue(m.SpellId, out var s) ? s.SpellbookOrder : int.MaxValue)
            .ToList();

        this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.CommonlyMissingHeader, this.displayLanguage));
        ImGui.Separator();

        if (missing.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.AllSpellsKnownByAll, this.displayLanguage));
            return;
        }

        var rows = missing.Select(entry =>
        {
            var hasSpell = this.spellDataService.Spells.TryGetValue(entry.SpellId, out var spell);
            return new
            {
                Entry = entry,
                Name = hasSpell ? this.GetSpellName(spell!) : UiStrings.Format(UiStrings.Key.SpellFallback, this.displayLanguage, entry.SpellId),
                SpellbookOrder = hasSpell ? spell!.SpellbookOrder : int.MaxValue,
                IconId = hasSpell ? spell!.IconId : 0u,
            };
        }).ToList();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint(
            "##ComparisonFilter", UiStrings.Get(UiStrings.Key.SpellFilterHint, this.displayLanguage), ref this.comparisonFilterText, 128);
        ImGui.Checkbox(UiStrings.Get(UiStrings.Key.HideTotemsToggle, this.displayLanguage), ref this.excludeTotems);
        ImGui.Separator();

        var filteredRows = rows
            .Where(r => SpellFilter.Matches(r.Name, r.SpellbookOrder, this.comparisonFilterText))
            .Where(r => !this.excludeTotems || !this.spellDataService.IsOnlyLearnableViaTotem(r.Entry.SpellId))
            .ToList();

        const ImGuiTableFlags tableFlags =
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;

        if (ImGui.BeginTable("MissingSpellsTable", 5, tableFlags, new System.Numerics.Vector2(0, ScrollingTableHeight)))
        {
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 28);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnNumber, this.displayLanguage), ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnSpell, this.displayLanguage));
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnMissingFor, this.displayLanguage), ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnSources, this.displayLanguage));
            // Kopfzeile bleibt beim vertikalen Scrollen sichtbar (0 Spalten, 1 Zeile eingefroren) -
            // MUSS vor TableHeadersRow() aufgerufen werden, siehe ImGui-Doku zu ScrollY-Tabellen.
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            foreach (var row in filteredRows)
            {
                var entry = row.Entry;
                var orderText = row.SpellbookOrder == int.MaxValue ? "—" : $"#{row.SpellbookOrder:D3}";
                var sources = this.spellDataService.GetSourcesForSpell(entry.SpellId, this.excludeTotems).ToList();

                ImGui.TableNextRow();
                this.HighlightRowByUrgency(entry.PlayersMissingIt.Count, partyStatus.Count);

                ImGui.TableSetColumnIndex(0);
                this.DrawSpellIcon(row.IconId);

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(orderText);

                ImGui.TableSetColumnIndex(2);
                if (ImGui.Selectable(row.Name, false, ImGuiSelectableFlags.SpanAllColumns))
                    this.JumpToSpellInSpellbook(entry.SpellId);

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(UiStrings.Format(
                        UiStrings.Key.TooltipMissingFor, this.displayLanguage, string.Join(", ", entry.PlayersMissingIt)));

                    foreach (var (monster, location, method) in sources)
                    {
                        ImGui.TextUnformatted(UiStrings.Format(
                            UiStrings.Key.TooltipSourceLine, this.displayLanguage, this.GetMonsterName(monster), method.GetDisplayName(), this.FormatLocation(location)));
                    }

                    ImGui.EndTooltip();
                }

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(entry.PlayersMissingIt.Count.ToString());

                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted(this.FormatSourceSummary(sources));
            }

            ImGui.EndTable();
        }

        // Legende zur Zeilenfärbung aus HighlightRowByUrgency unten (statt Tooltip auf der
        // Kopfzeile) - so bleibt sie sichtbar, ohne dass man extra über die Kopfzeile hovern muss.
        this.DrawHintText(UiStrings.Get(UiStrings.Key.ComparisonUrgencyLegend, this.displayLanguage));
    }

    private void HighlightRowByUrgency(int playersMissingCount, int totalPlayerCount)
    {
        if (totalPlayerCount == 0)
            return;

        if (playersMissingCount == totalPlayerCount)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(UrgencyAllMissingColor));
        }
        else if (playersMissingCount > totalPlayerCount / 2.0)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(UrgencyMajorityMissingColor));
        }
    }
}
