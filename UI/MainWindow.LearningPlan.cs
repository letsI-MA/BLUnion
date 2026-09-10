using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;

namespace BLUnion.UI;

public sealed partial class MainWindow
{
    private void DrawLearningPlanTab()
    {
        this.DrawLastMessage();

        var allSpellIds = this.spellDataService.Spells.Keys;
        var partyStatus = this.syncProvider.GetKnownPartyStatus();

        if (partyStatus.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Format(
                UiStrings.Key.NoPlayerDataLoaded, this.displayLanguage, UiStrings.Get(UiStrings.Key.TabSync, this.displayLanguage)));
            return;
        }

        var missing = this.comparisonService.GetCommonlyMissingSpells(allSpellIds, partyStatus);

        if (missing.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.AllSpellsKnownByAll, this.displayLanguage));
            return;
        }

        ImGui.Checkbox(UiStrings.Get(UiStrings.Key.HideTotemsToggle, this.displayLanguage), ref this.excludeTotems);

        var groups = this.comparisonService.GroupMissingSpellsByMonster(missing, this.spellDataService, this.excludeTotems)
            .Where(g => g.CoveredMissingSpellIds.Count >= 2)
            .ToList();

        this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.LearnableAtMonstersHeader, this.displayLanguage));
        ImGui.Separator();

        if (groups.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.NoMonsterCoversTwoMissing, this.displayLanguage));
            return;
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint(
            "##LearningPlanFilter",
            UiStrings.Get(UiStrings.Key.SpellFilterHint, this.displayLanguage),
            ref this.learningPlanFilterText,
            128);
        ImGui.Separator();

        foreach (var group in groups)
        {
            this.spellDataService.Monsters.TryGetValue(group.MonsterId, out var monster);
            var monsterName = monster is not null
                ? this.GetMonsterName(monster)
                : UiStrings.Format(UiStrings.Key.MonsterFallback, this.displayLanguage, group.MonsterId);

            Location? location = null;
            if (monster is not null)
                this.spellDataService.Locations.TryGetValue(monster.LocationId, out location);

            var spellRows = group.CoveredMissingSpellIds
                .Select(spellId =>
                {
                    var hasSpell = this.spellDataService.Spells.TryGetValue(spellId, out var spell);
                    return new
                    {
                        Name = hasSpell ? this.GetSpellName(spell!) : UiStrings.Format(UiStrings.Key.SpellFallback, this.displayLanguage, spellId),
                        SpellbookOrder = hasSpell ? spell!.SpellbookOrder : int.MaxValue,
                        IconId = hasSpell ? spell!.IconId : 0u,
                    };
                })
                .Where(r => SpellFilter.Matches(r.Name, r.SpellbookOrder, this.learningPlanFilterText))
                .OrderBy(r => r.SpellbookOrder)
                .ToList();

            if (spellRows.Count == 0)
                continue;

            ImGui.TextUnformatted($"{monsterName} — {this.FormatLocation(location)}");
            ImGui.TextWrapped(UiStrings.Format(UiStrings.Key.LearnableAtMonsterCount, this.displayLanguage, spellRows.Count));

            foreach (var row in spellRows)
            {
                var orderText = row.SpellbookOrder == int.MaxValue ? "—" : $"#{row.SpellbookOrder:D3}";

                this.DrawSpellIcon(row.IconId);
                ImGui.SameLine();
                ImGui.TextUnformatted($"{orderText}  {row.Name}");
            }

            ImGui.Separator();
        }
    }
}
