using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;

namespace BLUnion.UI;

public sealed partial class MainWindow
{
    private void DrawSpellbookTab()
    {
        this.DrawLastMessage();

        var learnedSpellIds = this.localSpellUnlockService.GetLearnedSpellIds();

        if (ImGui.RadioButton(
                UiStrings.Get(UiStrings.Key.SpellbookFilterAll, this.displayLanguage), this.spellbookFilterMode == SpellbookFilterMode.All))
            this.spellbookFilterMode = SpellbookFilterMode.All;

        ImGui.SameLine();

        if (ImGui.RadioButton(
                UiStrings.Get(UiStrings.Key.SpellbookFilterLearned, this.displayLanguage), this.spellbookFilterMode == SpellbookFilterMode.Learned))
            this.spellbookFilterMode = SpellbookFilterMode.Learned;

        ImGui.SameLine();

        if (ImGui.RadioButton(
                UiStrings.Get(UiStrings.Key.SpellbookFilterMissing, this.displayLanguage), this.spellbookFilterMode == SpellbookFilterMode.Missing))
            this.spellbookFilterMode = SpellbookFilterMode.Missing;

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint(
            "##SpellbookFilter", UiStrings.Get(UiStrings.Key.SpellFilterHint, this.displayLanguage), ref this.spellbookFilterText, 128);
        ImGui.Separator();

        var rows = this.spellDataService.Spells.Values
            .OrderBy(s => s.SpellbookOrder)
            .Where(s => this.spellbookFilterMode switch
            {
                SpellbookFilterMode.Learned => learnedSpellIds.Contains(s.Id),
                SpellbookFilterMode.Missing => !learnedSpellIds.Contains(s.Id),
                _ => true,
            })
            .Where(s => SpellFilter.Matches(this.GetSpellName(s), s.SpellbookOrder, this.spellbookFilterText))
            .ToList();

        if (rows.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.SpellbookNoResults, this.displayLanguage));
            return;
        }

        // Einmal pro Draw-Aufruf berechnet (nicht pro Zeile!) - GetCommonlyMissingSpells iteriert
        // intern über alle Spells x alle Party-Mitglieder, ein Aufruf pro Tabellenzeile wäre O(n²).
        var partyMissingBySpellId = this.GetPartyMissingBySpellId();

        if (ImGui.GetContentRegionAvail().X >= TwoColumnLayoutMinWidth)
        {
            this.DrawSpellbookMasterDetailLayout(rows, learnedSpellIds, partyMissingBySpellId);
            return;
        }

        const ImGuiTableFlags tableFlags =
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;

        if (ImGui.BeginTable("SpellbookTable", 6, tableFlags, new System.Numerics.Vector2(0, ScrollingTableHeight)))
        {
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 28);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnNumber, this.displayLanguage), ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnSpell, this.displayLanguage));
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnStars, this.displayLanguage), ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnLearned, this.displayLanguage), ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnSources, this.displayLanguage));
            // Kopfzeile bleibt beim vertikalen Scrollen sichtbar - siehe gleiches Muster/Doc in
            // DrawComparisonTab.
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            foreach (var spell in rows)
            {
                var isLearned = learnedSpellIds.Contains(spell.Id);

                var sources = this.spellDataService.GetSourcesForSpell(spell.Id, excludeTotems: false).ToList();
                var partyMissingEntry = !isLearned && partyMissingBySpellId is not null && partyMissingBySpellId.TryGetValue(spell.Id, out var narrowMissingEntry)
                    ? narrowMissingEntry
                    : null;

                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                this.DrawSpellIcon(spell.IconId);

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted($"#{spell.SpellbookOrder:D3}");

                ImGui.TableSetColumnIndex(2);
                ImGui.Selectable(this.GetSpellName(spell), false, ImGuiSelectableFlags.SpanAllColumns);

                if (ImGui.IsItemHovered() && (spell.Description is not null || sources.Count > 0 || partyMissingEntry is not null))
                {
                    ImGui.BeginTooltip();

                    if (spell.Description is not null)
                    {
                        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35);
                        ImGui.TextUnformatted(spell.Description);

                        if (this.displayLanguage != DisplayLanguage.German)
                            this.DrawHintText(UiStrings.Get(UiStrings.Key.SpellbookDescriptionGermanOnlyHint, this.displayLanguage));

                        ImGui.PopTextWrapPos();

                        if (sources.Count > 0)
                            ImGui.Separator();
                    }

                    foreach (var (monster, location, method) in sources)
                    {
                        ImGui.TextUnformatted(UiStrings.Format(
                            UiStrings.Key.TooltipSourceLine, this.displayLanguage, this.GetMonsterName(monster), method.GetDisplayName(), this.FormatLocation(location)));
                    }

                    if (partyMissingEntry is not null)
                    {
                        // Reiner Hinweistext ohne Button, bewusst anders als im Master-Detail-Panel
                        // (siehe DrawSpellbookDetailContent): ImGui-Tooltips nehmen keine Eingaben
                        // entgegen, ein Button wäre hier nicht klickbar.
                        if (spell.Description is not null || sources.Count > 0)
                            ImGui.Separator();

                        ImGui.TextWrapped(UiStrings.Format(
                            UiStrings.Key.SpellbookNeededByPartyFormat, this.displayLanguage, partyMissingEntry.PlayersMissingIt.Count));
                    }

                    ImGui.EndTooltip();
                }

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(new string('★', spell.Stars) + new string('☆', Math.Max(0, 5 - spell.Stars)));

                ImGui.TableSetColumnIndex(4);
                if (isLearned)
                    ImGui.TextColored(SuccessMessageColor, "✓");
                else
                    ImGui.TextColored(NotLearnedColor, "–");

                ImGui.TableSetColumnIndex(5);
                ImGui.TextUnformatted(this.FormatSourceSummary(sources));
            }

            ImGui.EndTable();
        }
    }

    private void DrawSpellbookMasterDetailLayout(List<Spell> rows, IReadOnlySet<uint> learnedSpellIds, Dictionary<uint, MissingSpellInfo>? partyMissingBySpellId)
    {
        if (this.selectedSpellbookSpell is null || !rows.Contains(this.selectedSpellbookSpell))
            this.selectedSpellbookSpell = rows.FirstOrDefault();

        ImGui.BeginChild("SpellbookList", new System.Numerics.Vector2(MasterDetailListWidth, 0), true);
        foreach (var spell in rows)
        {
            this.DrawSpellIcon(spell.IconId);
            ImGui.SameLine();

            if (ImGui.Selectable(this.GetSpellName(spell) + "##SpellbookListItem" + spell.Id, ReferenceEquals(spell, this.selectedSpellbookSpell)))
                this.selectedSpellbookSpell = spell;
        }

        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("SpellbookDetail", System.Numerics.Vector2.Zero, true);
        if (this.selectedSpellbookSpell is not null)
            this.DrawSpellbookDetailContent(this.selectedSpellbookSpell, learnedSpellIds, partyMissingBySpellId);

        ImGui.EndChild();
    }

    private void DrawSpellbookDetailContent(Spell spell, IReadOnlySet<uint> learnedSpellIds, Dictionary<uint, MissingSpellInfo>? partyMissingBySpellId)
    {
        var isLearned = learnedSpellIds.Contains(spell.Id);
        var sources = this.spellDataService.GetSourcesForSpell(spell.Id, excludeTotems: false).ToList();

        this.DrawSpellIcon(spell.IconId);
        ImGui.SameLine();
        ImGui.TextUnformatted(this.GetSpellName(spell));

        ImGui.TextUnformatted(new string('★', spell.Stars) + new string('☆', Math.Max(0, 5 - spell.Stars)));

        if (isLearned)
            ImGui.TextColored(SuccessMessageColor, "✓");
        else
            ImGui.TextColored(NotLearnedColor, "–");

        if (spell.Description is not null)
        {
            ImGui.Separator();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35);
            ImGui.TextUnformatted(spell.Description);

            if (this.displayLanguage != DisplayLanguage.German)
                this.DrawHintText(UiStrings.Get(UiStrings.Key.SpellbookDescriptionGermanOnlyHint, this.displayLanguage));

            ImGui.PopTextWrapPos();
        }

        ImGui.Separator();

        foreach (var (monster, location, method) in sources)
        {
            ImGui.TextUnformatted(UiStrings.Format(
                UiStrings.Key.TooltipSourceLine, this.displayLanguage, this.GetMonsterName(monster), method.GetDisplayName(), this.FormatLocation(location)));
        }

        if (!isLearned && partyMissingBySpellId is not null && partyMissingBySpellId.TryGetValue(spell.Id, out var missingEntry))
        {
            ImGui.Separator();
            ImGui.TextWrapped(UiStrings.Format(
                UiStrings.Key.SpellbookNeededByPartyFormat, this.displayLanguage, missingEntry.PlayersMissingIt.Count));

            if (ImGui.Button(UiStrings.Get(UiStrings.Key.SpellbookGoToComparisonButton, this.displayLanguage)))
            {
                this.pendingActiveCategoryTabId = "TabCategoryParty";
                this.pendingActiveSubTabId = "TabSpellComparison";
            }
        }
    }

    // Einmal pro Draw-Aufruf berechnet statt pro Spell (siehe Aufrufstellen) - GetCommonlyMissingSpells
    // iteriert intern über alle Spells x alle Party-Mitglieder.
    private Dictionary<uint, MissingSpellInfo>? GetPartyMissingBySpellId()
    {
        var partyStatus = this.syncProvider.GetKnownPartyStatus();
        if (partyStatus.Count == 0)
            return null;

        return this.comparisonService
            .GetCommonlyMissingSpells(this.spellDataService.Spells.Keys, partyStatus)
            .ToDictionary(m => m.SpellId);
    }
}
