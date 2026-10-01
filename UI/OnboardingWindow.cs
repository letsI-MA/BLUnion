using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace BLUnion.UI;

// Abfrage beim ersten Start: Live-Sync oder nur manuelle Codes (Logik: Services/Onboarding.cs). Wird
// beim Öffnen des Hauptfensters gezeigt, solange keine Entscheidung getroffen wurde - Schließen ohne
// Auswahl ändert nichts, beim nächsten Öffnen kommt die Abfrage erneut. Beide Wahlmöglichkeiten stehen
// gleichwertig nebeneinander (gleiche Spaltenbreite, gleich große Buttons). "Online-Status teilen" steht
// in der Live-Sync-Spalte und wirkt nur zusammen mit Live-Sync (siehe Onboarding.ApplyChoice).
public sealed class OnboardingWindow : Window
{
    private static readonly System.Numerics.Vector4 HintTextColor = new(0.65f, 0.65f, 0.65f, 1f);

    private readonly Configuration configuration;
    private readonly Func<DisplayLanguage> getDisplayLanguage;

    private bool sharePresence;

    public OnboardingWindow(Configuration configuration, Func<DisplayLanguage> getDisplayLanguage)
        : base("BLUnion###BLUnionOnboarding", ImGuiWindowFlags.NoCollapse)
    {
        this.configuration = configuration;
        this.getDisplayLanguage = getDisplayLanguage;

        this.Size = new System.Numerics.Vector2(620, 330);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    // Checkbox immer AUS beim Öffnen (Default laut Aufgabenstellung).
    public override void OnOpen() => this.sharePresence = false;

    public override void Draw()
    {
        var language = this.getDisplayLanguage();
        this.WindowName = $"{UiStrings.Get(UiStrings.Key.OnboardingWindowTitle, language)}###BLUnionOnboarding";

        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.OnboardingIntro, language));
        ImGui.Separator();

        if (ImGui.BeginTable("OnboardingChoices", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.OnboardingLiveSyncDataText, language));
            ImGui.Checkbox(UiStrings.Get(UiStrings.Key.SharePresenceToggle, language), ref this.sharePresence);
            ImGui.PushStyleColor(ImGuiCol.Text, HintTextColor);
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.SharePresenceHint, language));
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.SharePresenceListedOnlyHint, language));
            ImGui.PopStyleColor();

            ImGui.TableSetColumnIndex(1);
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.OnboardingManualText, language));

            // Buttons in eigener Zeile, damit beide auf gleicher Höhe und gleich breit stehen.
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            if (ImGui.Button(UiStrings.Get(UiStrings.Key.OnboardingEnableLiveSyncButton, language), new System.Numerics.Vector2(-1, 0)))
                this.Choose(enableLiveSync: true);

            ImGui.TableSetColumnIndex(1);
            if (ImGui.Button(UiStrings.Get(UiStrings.Key.OnboardingManualButton, language), new System.Numerics.Vector2(-1, 0)))
                this.Choose(enableLiveSync: false);

            ImGui.EndTable();
        }

        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, HintTextColor);
        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.OnboardingChangeLaterHint, language));
        ImGui.PopStyleColor();
    }

    private void Choose(bool enableLiveSync)
    {
        Onboarding.ApplyChoice(this.configuration, enableLiveSync, this.sharePresence);
        this.configuration.Save();
        this.IsOpen = false;
    }
}
