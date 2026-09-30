using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;

namespace BLUnion.UI;

public sealed partial class MainWindow
{
    private enum LearningPlanLocationFilter
    {
        All,
        Duties,
        OpenWorld,
    }

    // Anzahl der Orts-Einträge, die beim ersten Anzeigen aufgeklappt sind.
    private const int LearningPlanInitiallyOpenEntries = 5;

    private LearningPlanLocationFilter learningPlanLocationFilter = LearningPlanLocationFilter.All;

    // Berechnung siehe LearningPlanService: nach Ort (Duty/Zone) gruppiert, alle fehlenden Spells,
    // sortiert nach Nutzen; Masked Carnivale und Totems in eigenen Abschnitten. Betrachtete Spieler
    // wie bisher: alle bekannten Einträge (lokaler Spieler + Party/Vergleich).
    private void DrawLearningPlanTab()
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

        var blueMageLevel = this.partyService.GetLocalBlueMageLevel();
        var plan = LearningPlanService.Build(
            partyStatus, this.spellDataService, location => this.gameTextService.GetZoneName(location, this.displayLanguage), blueMageLevel);

        if (plan.MissingSpellCount == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.AllSpellsKnownByAll, this.displayLanguage));
            return;
        }

        ImGui.TextWrapped(UiStrings.Format(UiStrings.Key.LearningPlanSummaryFormat, this.displayLanguage, plan.MissingSpellCount, plan.TotalBenefit));

        this.DrawLearningPlanFilterRadio(LearningPlanLocationFilter.All, UiStrings.Key.LearningPlanFilterAll);
        ImGui.SameLine();
        this.DrawLearningPlanFilterRadio(LearningPlanLocationFilter.Duties, UiStrings.Key.LearningPlanFilterDuties);
        ImGui.SameLine();
        this.DrawLearningPlanFilterRadio(LearningPlanLocationFilter.OpenWorld, UiStrings.Key.LearningPlanFilterOpenWorld);
        ImGui.SameLine();
        ImGui.Checkbox(UiStrings.Get(UiStrings.Key.HideTotemsToggle, this.displayLanguage), ref this.excludeTotems);

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint(
            "##LearningPlanFilter",
            UiStrings.Get(UiStrings.Key.SpellFilterHint, this.displayLanguage),
            ref this.learningPlanFilterText,
            128);
        ImGui.Separator();

        this.DrawLearningPlanLocations(plan, blueMageLevel);
        this.DrawLearningPlanCarnivale(plan);

        if (!this.excludeTotems)
            this.DrawLearningPlanTotems(plan, partyStatus);
    }

    private void DrawLearningPlanFilterRadio(LearningPlanLocationFilter filter, UiStrings.Key label)
    {
        if (ImGui.RadioButton(UiStrings.Get(label, this.displayLanguage), this.learningPlanLocationFilter == filter))
            this.learningPlanLocationFilter = filter;
    }

    private void DrawLearningPlanLocations(LearningPlan plan, int? blueMageLevel)
    {
        this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.LearningPlanLocationsHeader, this.displayLanguage));

        // Der Filter schränkt nur ein, welche Orte angezeigt werden - Gruppierung/Reihenfolge bleiben.
        var entries = plan.Locations
            .Where(entry => this.learningPlanLocationFilter switch
            {
                LearningPlanLocationFilter.Duties => entry.IsDuty,
                LearningPlanLocationFilter.OpenWorld => !entry.IsDuty,
                _ => true,
            })
            .Where(entry => entry.Monsters.Any(monster => monster.Spells.Any(this.MatchesLearningPlanFilter)))
            .ToList();

        if (entries.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.LearningPlanNoLocations, this.displayLanguage));
            ImGui.Separator();
            return;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var kind = UiStrings.Get(entry.IsDuty ? UiStrings.Key.SourceMethodDuty : UiStrings.Key.SourceMethodOpenWorld, this.displayLanguage);
            var titleSuffix = UiStrings.Format(UiStrings.Key.LearningPlanLocationEntrySuffixFormat, this.displayLanguage, kind, entry.Benefit);

            // Once: nur beim ersten Anzeigen - danach bleibt der vom Nutzer gewählte Zustand erhalten.
            // Die Kopfzeile hat kein eigenes Label: Ortsname (klickbar, siehe DrawLocationName) und Rest
            // werden darübergezeichnet. AllowItemOverlap: ein Klick auf den Namen klappt nicht auf/zu.
            var headerPosition = ImGui.GetCursorScreenPos();
            ImGui.SetNextItemOpen(index < LearningPlanInitiallyOpenEntries, ImGuiCond.Once);
            var isOpen = ImGui.CollapsingHeader($"##LearningPlanLocation{entry.Key}", ImGuiTreeNodeFlags.AllowItemOverlap);
            var afterHeaderPosition = ImGui.GetCursorScreenPos();

            ImGui.SetCursorScreenPos(headerPosition + new System.Numerics.Vector2(ImGui.GetTreeNodeToLabelSpacing(), ImGui.GetStyle().FramePadding.Y));
            this.DrawLocationName(
                entry.RepresentativeLocation,
                this.gameTextService.GetZoneName(entry.RepresentativeLocation, this.displayLanguage),
                $"LearningPlan{entry.Key}",
                zoneLevel: true);
            ImGui.SameLine(0, ImGui.CalcTextSize(" ").X);
            ImGui.TextUnformatted(titleSuffix);
            ImGui.SetCursorScreenPos(afterHeaderPosition);

            if (!isOpen)
                continue;

            ImGui.Indent();

            // Hinweis statt Ausblenden, wenn die Duty über dem aktuellen Blaumagier-Level liegt.
            if (blueMageLevel is { } level && this.gameTextService.GetRequiredLevel(entry.RepresentativeLocation) is { } required && required > level)
                ImGui.TextColored(ErrorMessageColor, UiStrings.Format(UiStrings.Key.LearningPlanRequiredLevelHint, this.displayLanguage, required, level));

            this.DrawHintText(UiStrings.Get(UiStrings.Key.LearningPlanBeneficiariesLabel, this.displayLanguage) + " " + string.Join("; ",
                entry.Beneficiaries.Select(beneficiary => $"{beneficiary.PlayerName} ({string.Join(", ", beneficiary.SpellIds.Select(this.FormatLearningPlanSpellName))})")));

            foreach (var planMonster in entry.Monsters)
            {
                var spells = planMonster.Spells.Where(this.MatchesLearningPlanFilter).ToList();
                if (spells.Count == 0)
                    continue;

                var monsterLine = this.GetMonsterName(planMonster.Monster);
                if (planMonster.Location.Coordinates is { } coordinates)
                    monsterLine += $" ({coordinates})";

                ImGui.TextUnformatted(monsterLine);
                this.DrawMapLinkButtons(
                    planMonster.Location, this.GetMonsterName(planMonster.Monster), $"LearningPlan{entry.Key}_{planMonster.Monster.Id}", showTeleportIcon: true);
                foreach (var spell in spells)
                    this.DrawLearningPlanSpellRow(spell, $"{entry.Key}_{planMonster.Monster.Id}");
            }

            ImGui.Unindent();
        }

        ImGui.Separator();
    }

    private void DrawLearningPlanCarnivale(LearningPlan plan)
    {
        var entries = plan.Carnivale.Where(entry => this.MatchesLearningPlanFilter(entry.Spell)).ToList();
        if (entries.Count == 0)
            return;

        this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.LearningPlanCarnivaleHeader, this.displayLanguage));

        foreach (var entry in entries)
        {
            var stageText = entry.RequiredStages is { } stages
                ? UiStrings.Format(UiStrings.Key.LearningPlanCarnivaleProgressFormat, this.displayLanguage, stages)
                : entry.Stage is { } stage
                    ? this.gameTextService.GetZoneName(stage, this.displayLanguage)
                    : UiStrings.Get(UiStrings.Key.UnknownLocation, this.displayLanguage);

            ImGui.TextUnformatted($"{stageText} - {this.GetMonsterName(entry.Monster)}");
            this.DrawLearningPlanSpellRow(entry.Spell, $"Carnivale_{entry.Monster.Id}");
        }

        ImGui.Separator();
    }

    private void DrawLearningPlanTotems(LearningPlan plan, IReadOnlyList<PlayerSpellStatus> partyStatus)
    {
        var entries = plan.Totems.Where(entry => this.MatchesLearningPlanFilter(entry.Spell)).ToList();
        if (entries.Count == 0)
            return;

        this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.LearningPlanTotemsHeader, this.displayLanguage));

        var localLearnedCount = partyStatus.FirstOrDefault(player => player.IsLocalPlayer)?.LearnedSpellIds.Count;
        var blueMageLevel = this.partyService.GetLocalBlueMageLevel();

        foreach (var entry in entries)
        {
            this.DrawLearningPlanSpellRow(entry.Spell, "Totem");

            ImGui.Indent();
            switch (entry.Condition.Kind)
            {
                case TotemConditionKind.LearnedSpells:
                    this.DrawHintText(UiStrings.Format(
                        UiStrings.Key.LearningPlanTotemSpellCountFormat, this.displayLanguage, entry.Condition.Required, localLearnedCount?.ToString() ?? "?"));
                    break;
                case TotemConditionKind.Level:
                    this.DrawHintText(UiStrings.Format(
                        UiStrings.Key.LearningPlanTotemLevelFormat, this.displayLanguage, entry.Condition.Required, blueMageLevel?.ToString() ?? "?"));
                    break;
            }

            if (entry.Met == true)
                ImGui.TextColored(SuccessMessageColor, UiStrings.Format(
                    UiStrings.Key.LearningPlanTotemReady, this.displayLanguage, this.gameTextService.GetWaywardGaheelJaName(this.displayLanguage)));

            ImGui.Unindent();
        }

        ImGui.Separator();
    }

    // Eine Spell-Zeile (Icon, Nummer, Name, wer ihn braucht) - Klick springt ins Spellbook.
    private void DrawLearningPlanSpellRow(PlanSpell planSpell, string idSuffix)
    {
        var hasSpell = this.spellDataService.Spells.TryGetValue(planSpell.SpellId, out var spell);
        var orderText = hasSpell ? $"#{spell!.SpellbookOrder:D3}" : "—";

        this.DrawSpellIcon(hasSpell ? spell!.IconId : 0u);
        ImGui.SameLine();

        var label = $"{orderText}  {this.FormatLearningPlanSpellName(planSpell.SpellId)} - {string.Join(", ", planSpell.PlayersMissingIt)}" +
            $"##LearningPlanSpell{idSuffix}_{planSpell.SpellId}";
        if (ImGui.Selectable(label))
            this.JumpToSpellInSpellbook(planSpell.SpellId);
    }

    private string FormatLearningPlanSpellName(uint spellId) =>
        this.spellDataService.Spells.TryGetValue(spellId, out var spell)
            ? this.GetSpellName(spell)
            : UiStrings.Format(UiStrings.Key.SpellFallback, this.displayLanguage, spellId);

    private bool MatchesLearningPlanFilter(PlanSpell planSpell) =>
        this.spellDataService.Spells.TryGetValue(planSpell.SpellId, out var spell)
            ? SpellFilter.Matches(this.GetSpellName(spell), spell.SpellbookOrder, this.learningPlanFilterText)
            : SpellFilter.Matches(this.FormatLearningPlanSpellName(planSpell.SpellId), int.MaxValue, this.learningPlanFilterText);
}
