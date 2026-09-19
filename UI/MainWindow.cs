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
// Tab-Dateien: MainWindow.Home.cs, MainWindow.Party.cs, MainWindow.Comparison.cs,
// MainWindow.LearningPlan.cs, MainWindow.Loadouts.cs, MainWindow.Sync.cs, MainWindow.Spellbook.cs,
// MainWindow.GroupFinder.cs, MainWindow.Settings.cs - je genau eine Datei pro Draw*Tab()-Methode (plus deren private
// Detail-Helper, die NUR von dieser einen Tab-Methode aus erreichbar sind). Tab-übergreifend
// genutzte kleine Draw-/Format-Helper (DrawSpellIcon, DrawSectionHeader, DrawHintText,
// DrawCardGrid, DrawLastMessage/SetSuccessMessage/SetErrorMessage, GetSpellName/GetMonsterName
// etc.) liegen in MainWindow.Shared.cs.
public sealed partial class MainWindow : Window, IDisposable
{
    private readonly PartyService partyService;
    private readonly SpellDataService spellDataService;
    private readonly ComparisonService comparisonService;
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

    private static readonly System.Numerics.Vector4 CategoryAccentHomeColor = new(0.55f, 0.55f, 0.6f, 1);

    private static readonly System.Numerics.Vector4 CategoryAccentPartyColor = new(0.3f, 0.55f, 0.95f, 1);

    private static readonly System.Numerics.Vector4 CategoryAccentGroupsColor = new(0.75f, 0.45f, 0.95f, 1);

    private static readonly System.Numerics.Vector4 CategoryAccentSpellbookColor = new(0.95f, 0.65f, 0.15f, 1);

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

    // Wartezeit nach dem ersten Frame mit Charakter, bevor TryAutoPublishOwnStatus den eigenen
    // Status erzeugt - damit IUnlockState gefüllt ist (siehe MainWindow.Sync.cs).
    private static readonly TimeSpan AutoPublishOwnStatusDelay = TimeSpan.FromSeconds(5);

    private const string WebCompanionUrl = "https://letsi-ma.github.io/BLUnion/";

    private string importCodeBuffer = string.Empty;
    private string comparisonFilterText = string.Empty;
    private string learningPlanFilterText = string.Empty;
    private string spellbookFilterText = string.Empty;
    private string? lastError;

    private bool lastMessageIsError;

    // Eigenes Feld statt lastMessageIsError wiederzuverwenden (siehe DrawStatusBar) - lastError wird
    // auch für sync-fremde Fehler gesetzt (z.B. ImportFailed, GenericError beim Browser-Öffnen), das
    // Status-Symbol soll aber ausschließlich den letzten LiveSync-Vorgang widerspiegeln. Nur in
    // ApplyLiveSyncResult gesetzt: true bei jedem *Failed-Kind, false bei jedem *Succeeded/
    // *Published-Kind.
    private bool lastSyncHadError;

    private bool autoShareToPartyChat = true;

    private DateTimeOffset? lastAutoShareAt;

    // Einmal-Latch für TryAutoPublishOwnStatus (nur Speicher, nicht persistiert) - unabhängig von
    // autoShareToPartyChat/lastAutoShareAt oben, die nur das Teilen per Button betreffen.
    private bool ownStatusAutoPublishDone;

    private DateTimeOffset? localPlayerFirstSeenAt;

    private bool excludeTotems;

    private DisplayLanguage displayLanguage;

    private bool groupFinderTabWasActive;

    private static readonly TimeSpan GroupFinderAutoRefreshInterval = TimeSpan.FromSeconds(15);

    private DateTimeOffset? lastGroupFinderAutoRefreshAt;

    private bool groupFinderVisibilityInitialized;

    private string? pendingActiveCategoryTabId;

    // Analog zu pendingActiveCategoryTabId, aber für einen Sub-Tab INNERHALB des per
    // pendingActiveCategoryTabId angesprungenen Top-Level-Tabs (siehe z.B. DrawHomeTab: "Track"-
    // Button springt zu Party -> Learning Plan in einem Klick) - gleiches Reset-Verhalten (auf null
    // nach jedem Frame, siehe Ende von Draw()).
    private string? pendingActiveSubTabId;

    private enum DashboardCategory
    {
        None,
        Home,
        Party,
        Spellbook,
        Groups,
        Settings,
    }

    private DashboardCategory currentActiveCategory = DashboardCategory.None;

    // Trennt die beiden fachlich unterschiedlichen Anwendungsfälle im Publish-Sub-Tab (siehe
    // DrawGroupFinderTab) - vorher zwei CollapsingHeader übereinander, jetzt eine klare Moduswahl
    // per RadioButton statt Akkordeon. Solo als Default entspricht dem bisherigen DefaultOpen von
    // DrawMyEntrySection.
    private enum GroupPublishMode
    {
        Solo,
        Group,
    }

    private GroupPublishMode groupPublishMode = GroupPublishMode.Solo;

    private HashSet<AvailabilityTag> groupFinderTags = new();
    private string groupFinderNoteBuffer = string.Empty;
    private string groupFinderWantedPlayerCountBuffer = "0";

    // Ziel-Spell-Auswahl beim Veröffentlichen des eigenen Solo-Profils (siehe
    // DrawMyEntryTargetSpellSection in MainWindow.GroupFinder.cs) - fachlich analog zu
    // groupPublishTargetSpellIds/-FilterText/-HideTotems/-Scope (Gruppen-Publish), aber bewusst
    // eine EIGENE Instanz: ein Wechsel hier soll den Gruppen-Publish-Filter nicht mitbeeinflussen
    // und umgekehrt, da beide Auswahlen fachlich unabhängig sind (eigene Ziel-Spells vs. die einer
    // veröffentlichten Gruppe).
    private readonly HashSet<uint> groupFinderTargetSpellIds = new();
    private string groupFinderTargetSpellFilterText = string.Empty;
    private bool groupFinderTargetSpellHideTotems;
    private GroupPublishTargetSpellScope groupFinderTargetSpellScope = GroupPublishTargetSpellScope.OnlyMissing;

    private enum GroupMemberSource
    {
        Party,
        SyncList,
    }

    private GroupMemberSource groupMemberSource = GroupMemberSource.Party;

    private readonly HashSet<string> groupPublishSelectedMembers = new();

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

    // Ziel-Spell-Filter für BEIDE Browse-Listen (Spieler UND Gruppen, siehe
    // DrawBrowseTargetSpellFilterSection in MainWindow.GroupFinder.cs) - bewusst EIN gemeinsamer
    // Zustand statt je Liste ein eigener: ein Nutzer, der nach bestimmten Ziel-Spells sucht, sucht
    // damit typischerweise sowohl unter den Spieler- als auch den Gruppen-Einträgen danach. Anders
    // als beim Veröffentlichen (siehe groupPublishTargetSpellIds/groupFinderTargetSpellIds oben)
    // gibt es hier bewusst KEINEN Scope ("nur fehlende"/"alle") - der Filter dient nur der Auswahl,
    // wonach gesucht wird, nicht der Eingrenzung einer zu veröffentlichenden Liste.
    private readonly HashSet<uint> browseFilterSpellIds = new();
    private string browseFilterSpellFilterText = string.Empty;
    private bool browseFilterSpellHideTotems;

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

        // Behebt einen bestehenden Bug: vorher wurde der Handler NUR bei einer tatsächlichen
        // Checkbox-Interaktion während der Session registriert (siehe DrawSyncTab), nie automatisch
        // beim Programmstart, selbst wenn die (jetzt persistierte) Einstellung true war.
        if (this.configuration.AutoImportSyncCodesFromPartyChat)
            this.chatGui.ChatMessage += this.OnChatMessage;
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

            var homeTabActiveThisFrame = ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabHome, this.displayLanguage) + "###TabHome");
            if (homeTabActiveThisFrame)
            {
                this.DrawStatusBar();
                this.DrawHomeTab();
                ImGui.EndTabItem();
            }

            var partyCategoryFlags = this.pendingActiveCategoryTabId == "TabCategoryParty"
                ? ImGuiTabItemFlags.SetSelected
                : ImGuiTabItemFlags.None;
            var partyCategoryActiveThisFrame = ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabCategoryParty, this.displayLanguage) + "###TabCategoryParty", partyCategoryFlags);
            if (partyCategoryActiveThisFrame)
            {
                this.DrawStatusBar();

                if (ImGui.BeginTabBar("PartySubTabs"))
                {
                    if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabPartyOverview, this.displayLanguage) + "###TabPartyOverview"))
                    {
                        this.DrawPartyOverviewTab();
                        ImGui.EndTabItem();
                    }

                    var comparisonSubTabFlags = this.pendingActiveSubTabId == "TabSpellComparison"
                        ? ImGuiTabItemFlags.SetSelected
                        : ImGuiTabItemFlags.None;
                    if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabSpellComparison, this.displayLanguage) + "###TabSpellComparison", comparisonSubTabFlags))
                    {
                        this.DrawComparisonTab();
                        ImGui.EndTabItem();
                    }

                    var learningPlanSubTabFlags = this.pendingActiveSubTabId == "TabLearningPlan"
                        ? ImGuiTabItemFlags.SetSelected
                        : ImGuiTabItemFlags.None;
                    if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabLearningPlan, this.displayLanguage) + "###TabLearningPlan", learningPlanSubTabFlags))
                    {
                        this.DrawLearningPlanTab();
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }

                ImGui.EndTabItem();
            }

            var spellbookCategoryFlags = this.pendingActiveCategoryTabId == "TabCategorySpellbook"
                ? ImGuiTabItemFlags.SetSelected
                : ImGuiTabItemFlags.None;
            var spellbookCategoryActiveThisFrame = ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabCategorySpellbook, this.displayLanguage) + "###TabCategorySpellbook", spellbookCategoryFlags);
            if (spellbookCategoryActiveThisFrame)
            {
                this.DrawStatusBar();

                if (ImGui.BeginTabBar("SpellbookSubTabs"))
                {
                    var spellbookSubTabFlags = this.pendingActiveSubTabId == "TabSpellbook"
                        ? ImGuiTabItemFlags.SetSelected
                        : ImGuiTabItemFlags.None;
                    if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabSpellbook, this.displayLanguage) + "###TabSpellbook", spellbookSubTabFlags))
                    {
                        this.DrawSpellbookTab();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabLoadouts, this.displayLanguage) + "###TabLoadouts"))
                    {
                        this.DrawLoadoutsTab();
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }

                ImGui.EndTabItem();
            }

            var groupsTabFlags = this.pendingActiveCategoryTabId == "TabGroupFinder"
                ? ImGuiTabItemFlags.SetSelected
                : ImGuiTabItemFlags.None;
            var groupsTabActiveThisFrame = ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabGroupFinder, this.displayLanguage) + "###TabGroupFinder", groupsTabFlags);
            if (groupsTabActiveThisFrame)
            {
                this.DrawStatusBar();

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

            this.groupFinderTabWasActive = groupsTabActiveThisFrame;

            var settingsTabFlags = this.pendingActiveCategoryTabId == "TabSettings"
                ? ImGuiTabItemFlags.SetSelected
                : ImGuiTabItemFlags.None;
            var settingsTabActiveThisFrame = ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabSettings, this.displayLanguage) + "###TabSettings", settingsTabFlags);
            if (settingsTabActiveThisFrame)
            {
                this.DrawStatusBar();
                this.DrawSettingsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();

            if (homeTabActiveThisFrame)
                this.currentActiveCategory = DashboardCategory.Home;
            else if (partyCategoryActiveThisFrame)
                this.currentActiveCategory = DashboardCategory.Party;
            else if (spellbookCategoryActiveThisFrame)
                this.currentActiveCategory = DashboardCategory.Spellbook;
            else if (groupsTabActiveThisFrame)
                this.currentActiveCategory = DashboardCategory.Groups;
            else if (settingsTabActiveThisFrame)
                this.currentActiveCategory = DashboardCategory.Settings;
        }

        this.pendingActiveCategoryTabId = null;
        this.pendingActiveSubTabId = null;
    }

    private void DrawCategoryAccentStripe()
    {
        var color = this.currentActiveCategory switch
        {
            DashboardCategory.Home => CategoryAccentHomeColor,
            DashboardCategory.Party => CategoryAccentPartyColor,
            DashboardCategory.Spellbook => CategoryAccentSpellbookColor,
            DashboardCategory.Groups => CategoryAccentGroupsColor,
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
            // Grauer "↻" = gerade ein Push/Fetch aktiv (LiveSyncService.IsSyncing), schlägt den
            // Fehler-Zustand NICHT (kein Sonderfall nötig, siehe Aufgabenstellung) - grüner Punkt =
            // zuletzt erfolgreich, rotes Warndreieck = letzter Sync-Vorgang fehlgeschlagen (siehe
            // lastSyncHadError/ApplyLiveSyncResult). Kein Symbol, wenn Live-Sync deaktiviert ist
            // (siehe else-Zweig unten).
            if (this.liveSyncService.IsSyncing)
            {
                ImGui.TextColored(HintTextColor, "↻");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(UiStrings.Get(UiStrings.Key.StatusBarSyncingTooltip, this.displayLanguage));
            }
            else if (this.lastSyncHadError)
            {
                ImGui.TextColored(ErrorMessageColor, "⚠");
            }
            else
            {
                ImGui.TextColored(SuccessMessageColor, "●");
            }

            ImGui.SameLine();
            ImGui.TextColored(SuccessMessageColor, UiStrings.Get(UiStrings.Key.StatusBarLiveSyncActive, this.displayLanguage));
        }
        else
        {
            ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.StatusBarLiveSyncInactive, this.displayLanguage));
        }
    }

    private void ApplyLiveSyncResult(LiveSyncEventKind kind, string? detail)
    {
        // Exhaustiv über alle LiveSyncEventKind-Werte (nicht nur die klassischen Push/Fetch/Delete-
        // Fälle) - GroupPublish/GroupUnpublish/GroupBrowse/DevTestProfiles laufen über denselben
        // LiveSyncService-Backend-Zugriff, ein Fehlschlag dort ist für die Sync-Status-Anzeige
        // genauso relevant.
        this.lastSyncHadError = kind switch
        {
            LiveSyncEventKind.PushFailed
                or LiveSyncEventKind.FetchFailed
                or LiveSyncEventKind.DeleteFailed
                or LiveSyncEventKind.BrowseFailed
                or LiveSyncEventKind.GroupBrowseFailed
                or LiveSyncEventKind.DevTestProfilesFailed
                or LiveSyncEventKind.GroupPublishFailed
                or LiveSyncEventKind.GroupUnpublishFailed => true,
            LiveSyncEventKind.PushSucceeded
                or LiveSyncEventKind.DeleteSucceeded
                or LiveSyncEventKind.DevTestProfilesPublished
                or LiveSyncEventKind.GroupPublishSucceeded
                or LiveSyncEventKind.GroupUnpublishSucceeded => false,
            _ => this.lastSyncHadError,
        };

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
                this.ResetGroupFinderFormBuffers();
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
