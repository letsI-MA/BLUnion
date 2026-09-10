using System.Diagnostics;
using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;

namespace BLUnion.UI;

public sealed partial class MainWindow
{
    private void DrawLoadoutsTab()
    {
        this.DrawLastMessage();

        if (ImGui.RadioButton(
                UiStrings.Get(UiStrings.Key.LoadoutContentTypeMaskedCarnivale, this.displayLanguage),
                this.loadoutContentTypeFilter == LoadoutContentType.MaskedCarnivale))
            this.loadoutContentTypeFilter = LoadoutContentType.MaskedCarnivale;

        ImGui.SameLine();

        if (ImGui.RadioButton(
                UiStrings.Get(UiStrings.Key.LoadoutContentTypeFates, this.displayLanguage),
                this.loadoutContentTypeFilter == LoadoutContentType.Fates))
            this.loadoutContentTypeFilter = LoadoutContentType.Fates;

        ImGui.Separator();

        var loadouts = this.spellDataService.Loadouts
            .Where(l => l.ContentType == this.loadoutContentTypeFilter)
            .ToList();

        if (loadouts.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.LoadoutsNoneForType, this.displayLanguage));
            return;
        }

        var learnedSpellIds = this.localSpellUnlockService.GetLearnedSpellIds();

        if (ImGui.GetContentRegionAvail().X < TwoColumnLayoutMinWidth)
        {
            foreach (var loadout in loadouts)
            {
                this.DrawLoadoutDetailContent(loadout, learnedSpellIds);
                ImGui.Separator();
            }

            return;
        }

        if (this.selectedLoadout is null || !loadouts.Contains(this.selectedLoadout))
            this.selectedLoadout = loadouts.FirstOrDefault();

        ImGui.BeginChild("LoadoutsList", new System.Numerics.Vector2(MasterDetailListWidth, 0), true);
        foreach (var loadout in loadouts)
        {
            if (ImGui.Selectable(this.GetLoadoutName(loadout) + "##LoadoutListItem" + loadout.Id, ReferenceEquals(loadout, this.selectedLoadout)))
                this.selectedLoadout = loadout;
        }

        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("LoadoutsDetail", System.Numerics.Vector2.Zero, true);
        if (this.selectedLoadout is not null)
            this.DrawLoadoutDetailContent(this.selectedLoadout, learnedSpellIds);

        ImGui.EndChild();
    }

    private void DrawLoadoutDetailContent(Loadout loadout, IReadOnlySet<uint> learnedSpellIds)
    {
        ImGui.TextUnformatted(this.GetLoadoutName(loadout));

        var learnedCount = loadout.SpellIds.Count(learnedSpellIds.Contains);
        ImGui.TextUnformatted(UiStrings.Format(
            UiStrings.Key.LoadoutProgressFormat, this.displayLanguage, learnedCount, loadout.SpellIds.Count));

        if (!string.IsNullOrEmpty(loadout.Description))
            ImGui.TextWrapped(loadout.Description);

        if (!string.IsNullOrEmpty(loadout.SourceNote))
        {
            ImGui.TextWrapped(UiStrings.Format(UiStrings.Key.LoadoutSourceLabel, this.displayLanguage, loadout.SourceNote));

            if (!string.IsNullOrEmpty(loadout.SourceUrl))
            {
                if (ImGui.Button($"{UiStrings.Get(UiStrings.Key.LoadoutOpenSourceButton, this.displayLanguage)}##LoadoutSource{loadout.Id}"))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(loadout.SourceUrl) { UseShellExecute = true });
                        this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.BrowserOpenedMessage, this.displayLanguage));
                    }
                    catch (Exception ex)
                    {
                        this.SetErrorMessage(UiStrings.Format(UiStrings.Key.GenericError, this.displayLanguage, ex.Message));
                    }
                }
            }
        }

        foreach (var spellId in loadout.SpellIds)
        {
            var hasSpell = this.spellDataService.Spells.TryGetValue(spellId, out var spell);
            var name = hasSpell ? this.GetSpellName(spell!) : UiStrings.Format(UiStrings.Key.SpellFallback, this.displayLanguage, spellId);

            this.DrawSpellIcon(hasSpell ? spell!.IconId : 0u);
            ImGui.SameLine();

            if (learnedSpellIds.Contains(spellId))
                ImGui.TextColored(SuccessMessageColor, name);
            else
                ImGui.TextUnformatted(name);
        }
    }

    private string GetLoadoutName(Loadout loadout) => loadout.GetName(this.displayLanguage);
}
