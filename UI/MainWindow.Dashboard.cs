using BLUnion.Services;
using Dalamud.Bindings.ImGui;

namespace BLUnion.UI;

public sealed partial class MainWindow
{
    private void DrawDashboardTab()
    {
        var learnedSpellCount = this.localSpellUnlockService.GetLearnedSpellIds().Count;
        var totalSpellCount = this.spellDataService.Spells.Count;
        var progressPercent = totalSpellCount > 0 ? learnedSpellCount * 100 / totalSpellCount : 0;

        ImGui.TextUnformatted(UiStrings.Format(
            UiStrings.Key.DashboardProgressFormat, this.displayLanguage, learnedSpellCount, totalSpellCount, progressPercent));
        ImGui.ProgressBar(totalSpellCount > 0 ? (float)learnedSpellCount / totalSpellCount : 0f);

        ImGui.TextUnformatted(UiStrings.Format(
            UiStrings.Key.DashboardPartyFormat, this.displayLanguage, this.partyService.GetBlueMagePartyMembers().Count));

        if (this.configuration.LiveSyncEnabled)
        {
            ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.DashboardLiveSyncEnabled, this.displayLanguage));

            var dataCenter = this.liveSyncService.LastKnownOwnProfile?.DataCenter;
            if (!string.IsNullOrEmpty(dataCenter))
                ImGui.TextUnformatted(UiStrings.Format(UiStrings.Key.DashboardDataCenterFormat, this.displayLanguage, dataCenter));
        }
        else
        {
            ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.DashboardLiveSyncDisabled, this.displayLanguage));
        }

        ImGui.Separator();

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.DashboardGoToProgressButton, this.displayLanguage)))
            this.pendingActiveCategoryTabId = "TabCategoryProgress";

        ImGui.SameLine();
        if (ImGui.Button(UiStrings.Get(UiStrings.Key.DashboardGoToSyncGroupsButton, this.displayLanguage)))
            this.pendingActiveCategoryTabId = "TabCategorySyncGroups";

        ImGui.SameLine();
        if (ImGui.Button(UiStrings.Get(UiStrings.Key.DashboardGoToReferenceButton, this.displayLanguage)))
            this.pendingActiveCategoryTabId = "TabCategoryReference";
    }
}
