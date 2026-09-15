using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;

namespace BLUnion.UI;

public sealed partial class MainWindow
{
    private void DrawSettingsTab()
    {
        this.DrawLastMessage();

        this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.DisplayLanguageHeader, this.displayLanguage));
        ImGui.Separator();

        foreach (var language in Enum.GetValues<DisplayLanguage>())
        {
            if (ImGui.RadioButton(GetNativeLanguageName(language), this.displayLanguage == language))
                this.displayLanguage = language;
        }

        ImGui.Separator();
        this.DrawHintText(UiStrings.Get(UiStrings.Key.DisplayLanguageHint, this.displayLanguage));

        ImGui.Separator();
        ImGui.Checkbox(UiStrings.Get(UiStrings.Key.AutoShareToPartyChatToggle, this.displayLanguage), ref this.autoShareToPartyChat);
        this.DrawHintText(UiStrings.Get(UiStrings.Key.AutoShareToPartyChatHint, this.displayLanguage));

        ImGui.Separator();

        var liveSyncEnabled = this.configuration.LiveSyncEnabled;
        if (ImGui.Checkbox(UiStrings.Get(UiStrings.Key.LiveSyncEnabledToggle, this.displayLanguage), ref liveSyncEnabled))
        {
            this.configuration.LiveSyncEnabled = liveSyncEnabled;
            this.configuration.Save();
        }

        this.DrawHintText(UiStrings.Get(UiStrings.Key.LiveSyncEnabledHint, this.displayLanguage));

        var hasLiveSyncProfile = this.liveSyncService.HasEditTokenForLocalCharacter();
        ImGui.BeginDisabled(!hasLiveSyncProfile);
        if (ImGui.Button(UiStrings.Get(UiStrings.Key.LiveSyncDeleteProfileButton, this.displayLanguage)))
            this.liveSyncService.DeleteOwnProfile();
        ImGui.EndDisabled();

        ImGui.Separator();

        // Sync (Export/Import/Auto-Import/Dev-Tools/Web-Companion, siehe DrawSyncSection in
        // MainWindow.Sync.cs) ist hierher verschoben worden - vorher eigener Top-Level-Tab, jetzt
        // ein Standard-zugeklappter "Erweitert"-Abschnitt, da diese Bedienfelder nach der
        // Live-Sync-Umstellung nur noch für den manuellen Code-Austausch (Fallback) gebraucht
        // werden, nicht mehr im normalen Alltagsfluss.
        if (ImGui.CollapsingHeader(UiStrings.Get(UiStrings.Key.SettingsAdvancedSyncHeader, this.displayLanguage)))
            this.DrawSyncSection();
    }

    private static string GetNativeLanguageName(DisplayLanguage language) => language switch
    {
        DisplayLanguage.German => "Deutsch",
        DisplayLanguage.English => "English",
        DisplayLanguage.French => "Français",
        DisplayLanguage.Japanese => "日本語",
        _ => language.ToString(),
    };
}
