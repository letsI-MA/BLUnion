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

        this.DrawLoadoutContentTypeSelector();

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

    // Ein RadioButton pro Enum-Wert (Reihenfolge = Enum-Reihenfolge); bricht in die nächste Zeile um,
    // sobald der nächste Button nicht mehr in die verfügbare Breite passt.
    private void DrawLoadoutContentTypeSelector()
    {
        var style = ImGui.GetStyle();
        var rightEdge = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var first = true;

        foreach (var contentType in Enum.GetValues<LoadoutContentType>())
        {
            var label = UiStrings.Get(GetLoadoutContentTypeKey(contentType), this.displayLanguage);

            if (!first)
            {
                var buttonWidth = ImGui.GetFrameHeight() + style.ItemInnerSpacing.X + ImGui.CalcTextSize(label).X;
                if (ImGui.GetItemRectMax().X + style.ItemSpacing.X + buttonWidth <= rightEdge)
                    ImGui.SameLine();
            }

            if (ImGui.RadioButton(label, this.loadoutContentTypeFilter == contentType))
                this.loadoutContentTypeFilter = contentType;

            first = false;
        }
    }

    private static UiStrings.Key GetLoadoutContentTypeKey(LoadoutContentType contentType) => contentType switch
    {
        LoadoutContentType.MaskedCarnivale => UiStrings.Key.LoadoutContentTypeMaskedCarnivale,
        LoadoutContentType.Tank => UiStrings.Key.LoadoutContentTypeTank,
        LoadoutContentType.Healer => UiStrings.Key.LoadoutContentTypeHealer,
        LoadoutContentType.OffHealer => UiStrings.Key.LoadoutContentTypeOffHealer,
        LoadoutContentType.DamageDealer => UiStrings.Key.LoadoutContentTypeDamageDealer,
        _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, null),
    };

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

            var learned = learnedSpellIds.Contains(spellId);
            if (learned)
                ImGui.PushStyleColor(ImGuiCol.Text, SuccessMessageColor);

            var clicked = ImGui.Selectable($"{name}##LoadoutSpell{loadout.Id}_{spellId}");

            if (learned)
                ImGui.PopStyleColor();

            if (clicked)
                this.JumpToSpellInSpellbook(spellId);
        }
    }

    private string GetLoadoutName(Loadout loadout) => loadout.GetName(this.displayLanguage);
}
