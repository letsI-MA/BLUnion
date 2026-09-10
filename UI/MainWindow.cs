using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;

namespace BLUnion.UI;

// Aufgeteilt nach Tab-Verantwortlichkeit in mehrere partial-class-Dateien (MainWindow.<Tab>.cs,
// siehe Aufgabenstellung "UI/MainWindow.cs ist mit ca. 1860 Zeilen zu groß geworden") - reine
// Struktur-/Datei-Aufteilung, KEINE Logikänderung. Diese Datei behält bewusst nur das, was
// tab-übergreifend/zentral ist: sämtliche Felder/Konstanten/Enums (unverändert HIER zentral
// belassen statt auf die Tab-Dateien verteilt, damit ihr Zustand an einer einzigen Stelle
// überblickbar bleibt), Konstruktor, PreDraw/PostDraw/Draw() als Haupt-Frame-Loop, die beiden nur
// von Draw() selbst aufgerufenen Helper (DrawCategoryAccentStripe/DrawStatusBar) sowie
// ApplyLiveSyncResult (reagiert auf LiveSyncService-Events, die sowohl vom Sync- als auch vom
// GroupFinder-Tab ausgelöst werden können, siehe dortige LiveSyncEventKind-Fälle) und Dispose().
//
// Tab-Dateien: MainWindow.Dashboard.cs, MainWindow.Comparison.cs, MainWindow.LearningPlan.cs,
// MainWindow.Loadouts.cs, MainWindow.Sync.cs, MainWindow.Spellbook.cs, MainWindow.GroupFinder.cs,
// MainWindow.Settings.cs - je genau eine Datei pro Draw*Tab()-Methode (plus deren private
// Detail-Helper, die NUR von dieser einen Tab-Methode aus erreichbar sind). Tab-übergreifend
// genutzte kleine Draw-/Format-Helper (DrawSpellIcon, DrawSectionHeader, DrawHintText,
// DrawCardGrid, DrawLastMessage/SetSuccessMessage/SetErrorMessage, GetSpellName/GetMonsterName
// etc.) liegen in MainWindow.Shared.cs.
public sealed partial class MainWindow : Window, IDisposable
{
    private readonly PartyService partyService;
    private readonly SpellDataService spellDataService;
    private readonly ComparisonService comparisonService;
    private readonly GroupTargetSpellService groupTargetSpellService;
    private readonly LocalSpellUnlockService localSpellUnlockService;
    private readonly ManualCodeSyncProvider syncProvider;
    private readonly Configuration configuration;
    private readonly LiveSyncService liveSyncService;
    private readonly ITextureProvider textureProvider;
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;

    private static readonly System.Numerics.Vector4 SuccessMessageColor = new(0.3f, 0.85f, 0.4f, 1);

    private static readonly System.Numerics.Vector4 ErrorMessageColor = new(1f, 0.4f, 0.4f, 1);

    private static readonly System.Numerics.Vector4 UrgencyAllMissingColor = new(0.75f, 0.3f, 0.25f, 0.3f);

    private static readonly System.Numerics.Vector4 UrgencyMajorityMissingColor = new(0.85f, 0.75f, 0.15f, 0.25f);

#if DEBUG
    private static readonly System.Numerics.Vector4 DevToolHeaderColor = new(0.3f, 0.75f, 1f, 1);
#endif

    private static readonly System.Numerics.Vector4 NotLearnedColor = new(0.6f, 0.6f, 0.6f, 1);

    private static readonly System.Numerics.Vector4 HintTextColor = new(0.65f, 0.65f, 0.65f, 1f);

    private static readonly System.Numerics.Vector4 SectionHeaderColor = new(0.95f, 0.75f, 0.3f, 1f);

    private static readonly System.Numerics.Vector4 CategoryAccentDashboardColor = new(0.55f, 0.55f, 0.6f, 1);

    private static readonly System.Numerics.Vector4 CategoryAccentProgressColor = new(0.3f, 0.55f, 0.95f, 1);

    private static readonly System.Numerics.Vector4 CategoryAccentSyncGroupsColor = new(0.75f, 0.45f, 0.95f, 1);

    private static readonly System.Numerics.Vector4 CategoryAccentReferenceColor = new(0.95f, 0.65f, 0.15f, 1);

    private static readonly System.Numerics.Vector4 CategoryAccentSettingsColor = new(0.8f, 0.35f, 0.3f, 1);

    private static readonly System.Numerics.Vector2 WindowPaddingValue = new(12f, 12f); // Standard ImGui: ~8,8

    private static readonly System.Numerics.Vector2 ItemSpacingValue = new(8f, 10f); // Standard ImGui: ~8,4

    private static readonly System.Numerics.Vector2 FramePaddingValue = new(6f, 4f); // Standard ImGui: ~4,3

    private const float TwoColumnLayoutMinWidth = 600f;

    private const float MasterDetailListWidth = 220f;

    // Feste ScrollY-Höhe für die Vergleichs-/Spellbook-Tabelle (siehe DrawComparisonTab/
    // DrawSpellbookTab) - genug für ca. 10 Zeilen sichtbar auf einmal, mit fixierter Kopfzeile
    // (ImGui.TableSetupScrollFreeze) statt die Tabelle unbegrenzt wachsen zu lassen.
    private const float ScrollingTableHeight = 320f;

    private static readonly TimeSpan AutoShareCooldown = TimeSpan.FromSeconds(10);

    private const string WebCompanionUrl = "https://letsi-ma.github.io/BLUnion/";

    private string importCodeBuffer = string.Empty;
    private string comparisonFilterText = string.Empty;
    private string learningPlanFilterText = string.Empty;
    private string spellbookFilterText = string.Empty;
    private string? lastError;

    private bool lastMessageIsError;

    private bool autoShareToPartyChat = true;

    private DateTimeOffset? lastAutoShareAt;

    private bool autoImportAsPartyLeader;

    private bool excludeTotems;

    private DisplayLanguage displayLanguage;

    private bool groupFinderTabWasActive;

    private static readonly TimeSpan GroupFinderAutoRefreshInterval = TimeSpan.FromSeconds(15);

    private DateTimeOffset? lastGroupFinderAutoRefreshAt;

    private bool groupFinderVisibilityInitialized;

    private string? pendingActiveCategoryTabId;

    private enum DashboardCategory
    {
        None,
        Dashboard,
        Progress,
        SyncGroups,
        Reference,
        Settings,
    }

    private DashboardCategory currentActiveCategory = DashboardCategory.None;

    private bool groupFinderVisible;
    private HashSet<AvailabilityTag> groupFinderTags = new();
    private string groupFinderNoteBuffer = string.Empty;
    private string groupFinderWantedPlayerCountBuffer = "0";

    private enum GroupMemberSource
    {
        Party,
        SyncList,
    }

    private GroupMemberSource groupMemberSource = GroupMemberSource.Party;

    private readonly HashSet<string> groupPublishSelectedMembers = new();

    private bool groupPublishVisible;
    private readonly HashSet<AvailabilityTag> groupPublishTags = new();
    private string groupPublishNoteBuffer = string.Empty;
    private string groupPublishWantedPlayerCountBuffer = "0";

    // Ziel-Spell-Auswahl beim Veröffentlichen einer Gruppen-Listung (siehe
    // DrawGroupPublishTargetSpellSection in MainWindow.GroupFinder.cs) - eigener Filtertext/
    // Totem-Toggle statt der bestehenden comparisonFilterText/excludeTotems-Felder, damit ein
    // Wechsel des Filters hier NICHT versehentlich den Vergleichs-/Lernplan-Tab mitbeeinflusst.
    private readonly HashSet<uint> groupPublishTargetSpellIds = new();
    private string groupPublishTargetSpellFilterText = string.Empty;
    private bool groupPublishTargetSpellHideTotems;

    private enum GroupPublishTargetSpellScope
    {
        OnlyMissing,
        All,
    }

    private GroupPublishTargetSpellScope groupPublishTargetSpellScope = GroupPublishTargetSpellScope.OnlyMissing;

    // Momentan im Detail-Popup angezeigte Gruppe (siehe DrawGroupTargetSpellDetailPopup) - hält
    // bewusst eine reine Objektreferenz auf den zum Klickzeitpunkt aktuellen LastGroupBrowseResults-
    // Eintrag (analog zu selectedLoadout/selectedSpellbookSpell) statt live per groupId
    // nachzuschlagen: ein Hintergrund-Refresh während das Popup offen ist, aktualisiert den Inhalt
    // dadurch bewusst NICHT, das Popup bleibt aber immerhin auf den zuletzt angeklickten Daten
    // konsistent nutzbar.
    private GroupFinderGroupEntry? groupTargetSpellDetailPopupEntry;

    private enum SpellbookFilterMode
    {
        All,
        Learned,
        Missing,
    }

    private SpellbookFilterMode spellbookFilterMode = SpellbookFilterMode.All;

    private Spell? selectedSpellbookSpell;

    private Loadout? selectedLoadout;

    private LoadoutContentType loadoutContentTypeFilter = LoadoutContentType.MaskedCarnivale;

    public MainWindow(
        PartyService partyService,
        SpellDataService spellDataService,
        ComparisonService comparisonService,
        GroupTargetSpellService groupTargetSpellService,
        LocalSpellUnlockService localSpellUnlockService,
        ManualCodeSyncProvider syncProvider,
        Configuration configuration,
        LiveSyncService liveSyncService,
        ITextureProvider textureProvider,
        IClientState clientState,
        IChatGui chatGui,
        IPluginLog log)
        : base("BLUnion###BLUnion")
    {
        this.partyService = partyService;
        this.spellDataService = spellDataService;
        this.comparisonService = comparisonService;
        this.groupTargetSpellService = groupTargetSpellService;
        this.localSpellUnlockService = localSpellUnlockService;
        this.syncProvider = syncProvider;
        this.configuration = configuration;
        this.liveSyncService = liveSyncService;
        this.textureProvider = textureProvider;
        this.chatGui = chatGui;
        this.log = log;

        this.displayLanguage = clientState.ClientLanguage switch
        {
            ClientLanguage.German => DisplayLanguage.German,
            ClientLanguage.English => DisplayLanguage.English,
            ClientLanguage.French => DisplayLanguage.French,
            ClientLanguage.Japanese => DisplayLanguage.Japanese,
            _ => DisplayLanguage.English,
        };

        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new System.Numerics.Vector2(420, 300),
            MaximumSize = new System.Numerics.Vector2(1200, 1200),
        };

        this.TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Heart,
            IconOffset = new System.Numerics.Vector2(2, 1),
            IconColor = new System.Numerics.Vector4(0.92f, 0.35f, 0.48f, 1f),
            Click = _ => Util.OpenLink("https://ko-fi.com/galderia"),
            ShowTooltip = () => ImGui.SetTooltip("Support on Ko-fi"),
        });
    }

    // Pushed here (not in Draw()) because Dalamud's WindowHost calls PreDraw() -> ImGui.Begin() -> Draw() -> ImGui.End() -> PostDraw().
    // WindowPadding is read by ImGui at Begin()-time, so it must already be on the style stack before Begin() runs to affect this
    // window's own edges; ItemSpacing/FramePadding would work either way, but are pushed together with it for one balanced pair.
    // PostDraw() always runs after Draw() - even on an early return inside Draw() - so the pop stays balanced without extra guards.
    public override void PreDraw()
    {
        base.PreDraw();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, WindowPaddingValue);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ItemSpacingValue);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, FramePaddingValue);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);

        base.PostDraw();
    }

    public override void Draw()
    {
        this.liveSyncService.Tick();
        if (this.liveSyncService.TryTakePendingResult(out var liveSyncEventKind, out var liveSyncDetail))
            this.ApplyLiveSyncResult(liveSyncEventKind, liveSyncDetail);

        this.WindowName = UiStrings.Get(UiStrings.Key.WindowTitle, this.displayLanguage) + "###BLUnion";

        if (ImGui.BeginTabBar("BLUnionTabs"))
        {
            this.DrawCategoryAccentStripe();

            var dashboardTabActiveThisFrame = ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabDashboard, this.displayLanguage) + "###TabDashboard");
            if (dashboardTabActiveThisFrame)
            {
                this.DrawStatusBar();
                this.DrawDashboardTab();
                ImGui.EndTabItem();
            }

            var progressCategoryFlags = this.pendingActiveCategoryTabId == "TabCategoryProgress"
                ? ImGuiTabItemFlags.SetSelected
                : ImGuiTabItemFlags.None;
            var progressCategoryActiveThisFrame = ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabCategoryProgress, this.displayLanguage) + "###TabCategoryProgress", progressCategoryFlags);
            if (progressCategoryActiveThisFrame)
            {
                this.DrawStatusBar();

                if (ImGui.BeginTabBar("ProgressSubTabs"))
                {
                    if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabSpellComparison, this.displayLanguage) + "###TabSpellComparison"))
                    {
                        this.DrawComparisonTab();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabLearningPlan, this.displayLanguage) + "###TabLearningPlan"))
                    {
                        this.DrawLearningPlanTab();
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }

                ImGui.EndTabItem();
            }

            var syncGroupsCategoryFlags = this.pendingActiveCategoryTabId == "TabCategorySyncGroups"
                ? ImGuiTabItemFlags.SetSelected
                : ImGuiTabItemFlags.None;
            var syncGroupsCategoryActiveThisFrame = ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabCategorySyncGroups, this.displayLanguage) + "###TabCategorySyncGroups", syncGroupsCategoryFlags);
            if (syncGroupsCategoryActiveThisFrame)
            {
                this.DrawStatusBar();

                if (ImGui.BeginTabBar("SyncGroupsSubTabs"))
                {
                    if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabSync, this.displayLanguage) + "###TabSync"))
                    {
                        this.DrawSyncTab();
                        ImGui.EndTabItem();
                    }

                    var groupFinderTabActiveThisFrame = ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabGroupFinder, this.displayLanguage) + "###TabGroupFinder");
                    if (groupFinderTabActiveThisFrame)
                    {
                        var now = DateTimeOffset.UtcNow;
                        var justOpened = !this.groupFinderTabWasActive;
                        if (justOpened || this.lastGroupFinderAutoRefreshAt is null
                            || now - this.lastGroupFinderAutoRefreshAt >= GroupFinderAutoRefreshInterval)
                        {
                            this.liveSyncService.TriggerBrowse();

                            this.liveSyncService.TriggerGroupBrowse();
                            this.lastGroupFinderAutoRefreshAt = now;
                        }

                        this.DrawGroupFinderTab();
                        ImGui.EndTabItem();
                    }

                    this.groupFinderTabWasActive = groupFinderTabActiveThisFrame;

                    ImGui.EndTabBar();
                }

                ImGui.EndTabItem();
            }

            var referenceCategoryFlags = this.pendingActiveCategoryTabId == "TabCategoryReference"
                ? ImGuiTabItemFlags.SetSelected
                : ImGuiTabItemFlags.None;
            var referenceCategoryActiveThisFrame = ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabCategoryReference, this.displayLanguage) + "###TabCategoryReference", referenceCategoryFlags);
            if (referenceCategoryActiveThisFrame)
            {
                this.DrawStatusBar();

                if (ImGui.BeginTabBar("ReferenceSubTabs"))
                {
                    if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabLoadouts, this.displayLanguage) + "###TabLoadouts"))
                    {
                        this.DrawLoadoutsTab();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabSpellbook, this.displayLanguage) + "###TabSpellbook"))
                    {
                        this.DrawSpellbookTab();
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }

                ImGui.EndTabItem();
            }

            var settingsTabActiveThisFrame = ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabSettings, this.displayLanguage) + "###TabSettings");
            if (settingsTabActiveThisFrame)
            {
                this.DrawStatusBar();
                this.DrawSettingsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();

            if (dashboardTabActiveThisFrame)
                this.currentActiveCategory = DashboardCategory.Dashboard;
            else if (progressCategoryActiveThisFrame)
                this.currentActiveCategory = DashboardCategory.Progress;
            else if (syncGroupsCategoryActiveThisFrame)
                this.currentActiveCategory = DashboardCategory.SyncGroups;
            else if (referenceCategoryActiveThisFrame)
                this.currentActiveCategory = DashboardCategory.Reference;
            else if (settingsTabActiveThisFrame)
                this.currentActiveCategory = DashboardCategory.Settings;
        }

        this.pendingActiveCategoryTabId = null;
    }

    private void DrawCategoryAccentStripe()
    {
        var color = this.currentActiveCategory switch
        {
            DashboardCategory.Dashboard => CategoryAccentDashboardColor,
            DashboardCategory.Progress => CategoryAccentProgressColor,
            DashboardCategory.SyncGroups => CategoryAccentSyncGroupsColor,
            DashboardCategory.Reference => CategoryAccentReferenceColor,
            DashboardCategory.Settings => CategoryAccentSettingsColor,
            _ => (System.Numerics.Vector4?)null,
        };

        if (color is not { } stripeColor)
            return;

        const float stripeHeight = 5f;
        var cursorScreenPos = ImGui.GetCursorScreenPos();
        var stripeWidth = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddRectFilled(
            cursorScreenPos,
            cursorScreenPos + new System.Numerics.Vector2(stripeWidth, stripeHeight),
            ImGui.ColorConvertFloat4ToU32(stripeColor));
        ImGui.Dummy(new System.Numerics.Vector2(0, stripeHeight));
    }

    private void DrawStatusBar()
    {
        ImGui.Spacing();

        var learnedSpellCount = this.localSpellUnlockService.GetLearnedSpellIds().Count;
        var totalSpellCount = this.spellDataService.Spells.Count;
        ImGui.TextUnformatted(UiStrings.Format(
            UiStrings.Key.StatusBarProgressFormat, this.displayLanguage, learnedSpellCount, totalSpellCount));

        var dataCenter = this.liveSyncService.LastKnownOwnProfile?.DataCenter;
        if (!string.IsNullOrEmpty(dataCenter))
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.StatusBarSeparator, this.displayLanguage));
            ImGui.SameLine();
            ImGui.TextUnformatted(dataCenter);
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.StatusBarSeparator, this.displayLanguage));
        ImGui.SameLine();
        if (this.configuration.LiveSyncEnabled)
        {
            ImGui.TextColored(SuccessMessageColor, UiStrings.Get(UiStrings.Key.StatusBarLiveSyncActive, this.displayLanguage));
        }
        else
        {
            ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.StatusBarLiveSyncInactive, this.displayLanguage));
        }
    }

    private void ApplyLiveSyncResult(LiveSyncEventKind kind, string? detail)
    {
        switch (kind)
        {
            case LiveSyncEventKind.PushSucceeded:
                this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.LiveSyncPushSucceeded, this.displayLanguage));
                break;
            case LiveSyncEventKind.PushFailed:
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.LiveSyncPushFailed, this.displayLanguage, detail ?? "?"));
                break;
            case LiveSyncEventKind.FetchFailed:
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.LiveSyncFetchFailed, this.displayLanguage, detail ?? "?"));
                break;
            case LiveSyncEventKind.DeleteSucceeded:
                this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.LiveSyncDeleteSucceeded, this.displayLanguage));
                break;
            case LiveSyncEventKind.DeleteFailed:
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.LiveSyncDeleteFailed, this.displayLanguage, detail ?? "?"));
                break;
            case LiveSyncEventKind.BrowseFailed:
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.LiveSyncBrowseFailed, this.displayLanguage, detail ?? "?"));
                break;
            case LiveSyncEventKind.GroupBrowseFailed:
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.GroupBrowseFailed, this.displayLanguage, detail ?? "?"));
                break;
            case LiveSyncEventKind.DevTestProfilesPublished:
                this.SetSuccessMessage(UiStrings.Format(UiStrings.Key.DevTestProfilesPublished, this.displayLanguage, detail ?? "?"));
                break;
            case LiveSyncEventKind.DevTestProfilesFailed:
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.DevTestProfilesFailed, this.displayLanguage, detail ?? "?"));
                break;
            case LiveSyncEventKind.GroupPublishSucceeded:
                this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.GroupPublishSucceededMessage, this.displayLanguage));
                break;
            case LiveSyncEventKind.GroupPublishFailed:
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.GroupPublishFailedMessage, this.displayLanguage, detail ?? "?"));
                break;
            case LiveSyncEventKind.GroupUnpublishSucceeded:
                this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.GroupUnpublishSucceededMessage, this.displayLanguage));
                break;
            case LiveSyncEventKind.GroupUnpublishFailed:
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.GroupUnpublishFailedMessage, this.displayLanguage, detail ?? "?"));
                break;
        }
    }

    public void Dispose() => this.chatGui.ChatMessage -= this.OnChatMessage;
}
