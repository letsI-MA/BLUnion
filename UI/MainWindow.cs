using System.Diagnostics;
using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.Chat;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using EcChat = ECommons.Automation.Chat;

namespace BLUnion.UI;

public sealed class MainWindow : Window, IDisposable
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

    private static readonly System.Numerics.Vector4 UrgencyAllMissingColor = new(0.8f, 0.2f, 0.15f, 0.55f);

    private static readonly System.Numerics.Vector4 UrgencyMajorityMissingColor = new(0.85f, 0.75f, 0.15f, 0.45f);

    private static readonly System.Numerics.Vector4 DevToolHeaderColor = new(0.3f, 0.75f, 1f, 1);

    private static readonly System.Numerics.Vector4 NotLearnedColor = new(0.6f, 0.6f, 0.6f, 1);

    private static readonly System.Numerics.Vector4 CategoryAccentDashboardColor = new(0.55f, 0.55f, 0.6f, 1);

    private static readonly System.Numerics.Vector4 CategoryAccentProgressColor = new(0.3f, 0.55f, 0.95f, 1);

    private static readonly System.Numerics.Vector4 CategoryAccentSyncGroupsColor = new(0.75f, 0.45f, 0.95f, 1);

    private static readonly System.Numerics.Vector4 CategoryAccentReferenceColor = new(0.95f, 0.65f, 0.15f, 1);

    private static readonly System.Numerics.Vector4 CategoryAccentSettingsColor = new(0.5f, 0.5f, 0.5f, 1);

    private const float TwoColumnLayoutMinWidth = 600f;

    private const float MasterDetailListWidth = 220f;

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
    }

    public override void Draw()
    {
        this.DrawCategoryAccentStripe();

        this.liveSyncService.Tick();
        if (this.liveSyncService.TryTakePendingResult(out var liveSyncEventKind, out var liveSyncDetail))
            this.ApplyLiveSyncResult(liveSyncEventKind, liveSyncDetail);

        this.WindowName = UiStrings.Get(UiStrings.Key.WindowTitle, this.displayLanguage) + "###BLUnion";

        if (ImGui.BeginTabBar("BLUnionTabs"))
        {
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

        const float stripeHeight = 3f;
        var cursorScreenPos = ImGui.GetCursorScreenPos();
        var windowWidth = ImGui.GetWindowWidth();
        ImGui.GetWindowDrawList().AddRectFilled(
            cursorScreenPos,
            cursorScreenPos + new System.Numerics.Vector2(windowWidth, stripeHeight),
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

    private void DrawComparisonTab()
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

        var missing = this.comparisonService.GetCommonlyMissingSpells(allSpellIds, partyStatus)
            .OrderByDescending(m => m.PlayersMissingIt.Count)
            .ThenBy(m => this.spellDataService.Spells.TryGetValue(m.SpellId, out var s) ? s.SpellbookOrder : int.MaxValue)
            .ToList();

        ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.CommonlyMissingHeader, this.displayLanguage));
        ImGui.Separator();

        if (missing.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.AllSpellsKnownByAll, this.displayLanguage));
            return;
        }

        var rows = missing.Select(entry =>
        {
            var hasSpell = this.spellDataService.Spells.TryGetValue(entry.SpellId, out var spell);
            return new
            {
                Entry = entry,
                Name = hasSpell ? this.GetSpellName(spell!) : UiStrings.Format(UiStrings.Key.SpellFallback, this.displayLanguage, entry.SpellId),
                SpellbookOrder = hasSpell ? spell!.SpellbookOrder : int.MaxValue,
                IconId = hasSpell ? spell!.IconId : 0u,
            };
        }).ToList();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint(
            "##ComparisonFilter", UiStrings.Get(UiStrings.Key.SpellFilterHint, this.displayLanguage), ref this.comparisonFilterText, 128);
        ImGui.Checkbox(UiStrings.Get(UiStrings.Key.HideTotemsToggle, this.displayLanguage), ref this.excludeTotems);
        ImGui.Separator();

        var filteredRows = rows
            .Where(r => SpellFilter.Matches(r.Name, r.SpellbookOrder, this.comparisonFilterText))
            .Where(r => !this.excludeTotems || !this.spellDataService.IsOnlyLearnableViaTotem(r.Entry.SpellId))
            .ToList();

        const ImGuiTableFlags tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("MissingSpellsTable", 5, tableFlags))
        {
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 28);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnNumber, this.displayLanguage), ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnSpell, this.displayLanguage));
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnMissingFor, this.displayLanguage), ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnSources, this.displayLanguage));
            ImGui.TableHeadersRow();

            foreach (var row in filteredRows)
            {
                var entry = row.Entry;
                var orderText = row.SpellbookOrder == int.MaxValue ? "—" : $"#{row.SpellbookOrder:D3}";
                var sources = this.spellDataService.GetSourcesForSpell(entry.SpellId, this.excludeTotems).ToList();

                ImGui.TableNextRow();
                this.HighlightRowByUrgency(entry.PlayersMissingIt.Count, partyStatus.Count);

                ImGui.TableSetColumnIndex(0);
                this.DrawSpellIcon(row.IconId);

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(orderText);

                ImGui.TableSetColumnIndex(2);
                ImGui.Selectable(row.Name, false, ImGuiSelectableFlags.SpanAllColumns);

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(UiStrings.Format(
                        UiStrings.Key.TooltipMissingFor, this.displayLanguage, string.Join(", ", entry.PlayersMissingIt)));

                    foreach (var (monster, location, method) in sources)
                    {
                        ImGui.TextUnformatted(UiStrings.Format(
                            UiStrings.Key.TooltipSourceLine, this.displayLanguage, this.GetMonsterName(monster), method.GetDisplayName(), this.FormatLocation(location)));
                    }

                    ImGui.EndTooltip();
                }

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(entry.PlayersMissingIt.Count.ToString());

                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted(this.FormatSourceSummary(sources));
            }

            ImGui.EndTable();
        }
    }

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

        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.LearnableAtMonstersHeader, this.displayLanguage));
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

    private void DrawSpellIcon(uint iconId)
    {
        var size = new System.Numerics.Vector2(24, 24);

        if (iconId != 0)
        {
            try
            {
                var texture = this.textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
                if (texture.TryGetWrap(out var wrap, out _))
                {
                    ImGui.Image(wrap.Handle, size);
                    return;
                }
            }
            catch
            {
            }
        }

        ImGui.Dummy(size);
    }

    private void HighlightRowByUrgency(int playersMissingCount, int totalPlayerCount)
    {
        if (totalPlayerCount == 0)
            return;

        if (playersMissingCount == totalPlayerCount)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(UrgencyAllMissingColor));
        }
        else if (playersMissingCount > totalPlayerCount / 2.0)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(UrgencyMajorityMissingColor));
        }
    }

    private string FormatLocation(Location? location)
    {
        if (location is null)
            return UiStrings.Get(UiStrings.Key.UnknownLocation, this.displayLanguage);

        var zoneName = location.GetZoneName(this.displayLanguage);

        if (location.Coordinates is not null)
            return $"{zoneName} ({location.Coordinates})";

        if (location.DutyName is not null)
            return location.DutyName;

        return zoneName;
    }

    private string FormatSourceSummary(IReadOnlyList<(Monster Monster, Location? Location, SourceMethod Method)> sources)
    {
        if (sources.Count == 0)
            return UiStrings.Get(UiStrings.Key.UnknownLocation, this.displayLanguage);

        if (sources.Count == 1)
        {
            var (monster, location, _) = sources[0];
            return $"{this.GetMonsterName(monster)} ({this.FormatLocation(location)})";
        }

        return UiStrings.Format(UiStrings.Key.SourceCountSummary, this.displayLanguage, sources.Count);
    }

    private void DrawSyncTab()
    {
        var members = this.partyService.GetBlueMagePartyMembers();

        if (members.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.NoBlueMagesInParty, this.displayLanguage));
        }
        else
        {
            foreach (var member in members)
                ImGui.TextUnformatted(UiStrings.Format(UiStrings.Key.PartyMemberEntry, this.displayLanguage, member.Name, member.Level));
        }

        ImGui.Separator();

        this.DrawLastMessage();

        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.SyncIntro, this.displayLanguage));

        ImGui.Separator();

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.DetermineAndExportButton, this.displayLanguage)))
        {
            try
            {
                var localPlayerName = this.partyService.GetLocalPlayerName()
                    ?? UiStrings.Get(UiStrings.Key.LocalPlayerFallbackName, this.displayLanguage);

                var status = this.localSpellUnlockService.GetLocalPlayerStatus(localPlayerName);
                this.syncProvider.PublishLocalStatus(status);
                var code = this.syncProvider.ExportToCode(status);

                ImGui.SetClipboardText(code);

                var sharedToPartyChat = this.TryAutoShareToPartyChat(code);
                this.SetSuccessMessage(sharedToPartyChat
                    ? UiStrings.Get(UiStrings.Key.ClipboardCopiedAndSharedMessage, this.displayLanguage)
                    : UiStrings.Get(UiStrings.Key.ClipboardCopiedMessage, this.displayLanguage));
            }
            catch (Exception ex)
            {
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.GenericError, this.displayLanguage, ex.Message));
            }
        }

        ImGui.Separator();
        ImGui.InputText(UiStrings.Get(UiStrings.Key.ImportCodeLabel, this.displayLanguage), ref this.importCodeBuffer, 4096);

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.ImportButton, this.displayLanguage)))
        {
            try
            {
                this.syncProvider.ImportCode(this.importCodeBuffer);
                this.ClearMessage();
                this.importCodeBuffer = string.Empty;
            }
            catch (Exception ex)
            {
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.ImportFailed, this.displayLanguage, ex.Message));
            }
        }

        ImGui.Separator();

        if (ImGui.Checkbox(UiStrings.Get(UiStrings.Key.AutoImportAsLeaderToggle, this.displayLanguage), ref this.autoImportAsPartyLeader))
        {
            if (this.autoImportAsPartyLeader)
                this.chatGui.ChatMessage += this.OnChatMessage;
            else
                this.chatGui.ChatMessage -= this.OnChatMessage;
        }

        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.AutoImportAsLeaderHint, this.displayLanguage));

        ImGui.Separator();
        ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.CurrentlyLoadedPlayersHeader, this.displayLanguage));

        var knownStatus = this.syncProvider.GetKnownPartyStatus();

        if (knownStatus.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.NoPlayerDataLoadedShort, this.displayLanguage));
        }
        else
        {
            string? playerToRemove = null;

            foreach (var status in knownStatus)
            {
                var line = UiStrings.Format(
                    UiStrings.Key.PlayerSpellCount, this.displayLanguage, status.CharacterName, status.LearnedSpellIds.Count);

                if (status.IsLocalPlayer)
                    line += UiStrings.Get(UiStrings.Key.YouSuffix, this.displayLanguage);

                ImGui.TextUnformatted(line);

                ImGui.SameLine();

                if (ImGui.Button($"{UiStrings.Get(UiStrings.Key.RemoveButton, this.displayLanguage)}##{status.CharacterName}"))
                    playerToRemove = status.CharacterName;
            }

            if (playerToRemove is not null)
                this.syncProvider.RemovePlayer(playerToRemove);
        }

        ImGui.Separator();
        ImGui.TextColored(
            DevToolHeaderColor,
            UiStrings.Get(UiStrings.Key.DevToolHeader, this.displayLanguage));

        this.DrawDevFixtureButton(UiStrings.Get(UiStrings.Key.DevLoadAliceButton, this.displayLanguage), DevTestFixtures.CreateAlice);
        ImGui.SameLine();
        this.DrawDevFixtureButton(UiStrings.Get(UiStrings.Key.DevLoadBobButton, this.displayLanguage), DevTestFixtures.CreateBob);
        ImGui.SameLine();
        this.DrawDevFixtureButton(UiStrings.Get(UiStrings.Key.DevLoadCharlesButton, this.displayLanguage), DevTestFixtures.CreateCharles);
        ImGui.SameLine();

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.DevPublishTestProfilesButton, this.displayLanguage)))
            this.liveSyncService.PublishDevTestProfiles();

        ImGui.Separator();

        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.WebCompanionIntro, this.displayLanguage));
        ImGui.Separator();

        ImGui.TextUnformatted(WebCompanionUrl);
        ImGui.Separator();

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.OpenInBrowserButton, this.displayLanguage)))
        {
            try
            {
                Process.Start(new ProcessStartInfo(WebCompanionUrl) { UseShellExecute = true });
                this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.BrowserOpenedMessage, this.displayLanguage));
            }
            catch (Exception ex)
            {
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.GenericError, this.displayLanguage, ex.Message));
            }
        }

        ImGui.SameLine();

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.CopyLinkButton, this.displayLanguage)))
        {
            ImGui.SetClipboardText(WebCompanionUrl);
            this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.LinkCopiedMessage, this.displayLanguage));
        }
    }

    private void DrawDevFixtureButton(string label, Func<SpellDataService, PlayerSpellStatus> createFixture)
    {
        if (ImGui.Button(label))
        {
            var fixture = createFixture(this.spellDataService);
            this.syncProvider.PublishLocalStatus(fixture);
            this.SetSuccessMessage(UiStrings.Format(
                UiStrings.Key.DevFixtureLoaded, this.displayLanguage, fixture.CharacterName, fixture.LearnedSpellIds.Count));
        }
    }

    private void DrawLastMessage()
    {
        if (this.lastError is null)
            return;

        var color = this.lastMessageIsError
            ? ErrorMessageColor
            : SuccessMessageColor;

        ImGui.TextColored(color, this.lastError);
        ImGui.Separator();
    }

    private void SetSuccessMessage(string message)
    {
        this.lastError = message;
        this.lastMessageIsError = false;
    }

    private void SetErrorMessage(string message)
    {
        this.lastError = message;
        this.lastMessageIsError = true;
    }

    private void ClearMessage() => this.lastError = null;

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

    private bool TryAutoShareToPartyChat(string code)
    {
        if (!this.autoShareToPartyChat || !this.partyService.IsInParty)
            return false;

        var now = DateTimeOffset.UtcNow;
        if (this.lastAutoShareAt is { } lastShare && now - lastShare < AutoShareCooldown)
            return false;

        EcChat.SendMessage("/p " + code);
        this.lastAutoShareAt = now;
        return true;
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        var senderName = message.Sender.TextValue;

        var localPlayerName = this.partyService.GetLocalPlayerName();
        if (localPlayerName is not null && string.Equals(senderName, localPlayerName, StringComparison.Ordinal))
            return;

        var text = message.Message.TextValue;
        var codeStart = text.IndexOf(ManualCodeSyncProvider.CurrentPrefix, StringComparison.Ordinal);
        if (codeStart < 0)
            return;

        var codeEnd = codeStart;
        while (codeEnd < text.Length && !char.IsWhiteSpace(text[codeEnd]))
            codeEnd++;

        var code = text[codeStart..codeEnd];

        try
        {
            this.syncProvider.ImportCode(code);

            this.SetSuccessMessage(UiStrings.Format(UiStrings.Key.AutoImportedMessage, this.displayLanguage, senderName));
        }
        catch (Exception ex)
        {
            this.log.Debug(ex, $"Automatischer Sync-Code-Import fehlgeschlagen (Absender \"{senderName}\").");
        }
    }

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

        if (ImGui.GetContentRegionAvail().X >= TwoColumnLayoutMinWidth)
        {
            this.DrawSpellbookMasterDetailLayout(rows, learnedSpellIds);
            return;
        }

        const ImGuiTableFlags tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("SpellbookTable", 6, tableFlags))
        {
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 28);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnNumber, this.displayLanguage), ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnSpell, this.displayLanguage));
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnStars, this.displayLanguage), ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnLearned, this.displayLanguage), ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnSources, this.displayLanguage));
            ImGui.TableHeadersRow();

            foreach (var spell in rows)
            {
                var isLearned = learnedSpellIds.Contains(spell.Id);

                var sources = this.spellDataService.GetSourcesForSpell(spell.Id, excludeTotems: false).ToList();

                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                this.DrawSpellIcon(spell.IconId);

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted($"#{spell.SpellbookOrder:D3}");

                ImGui.TableSetColumnIndex(2);
                ImGui.Selectable(this.GetSpellName(spell), false, ImGuiSelectableFlags.SpanAllColumns);

                if (ImGui.IsItemHovered() && (spell.Description is not null || sources.Count > 0))
                {
                    ImGui.BeginTooltip();

                    if (spell.Description is not null)
                    {
                        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35);
                        ImGui.TextUnformatted(spell.Description);

                        if (this.displayLanguage != DisplayLanguage.German)
                            ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.SpellbookDescriptionGermanOnlyHint, this.displayLanguage));

                        ImGui.PopTextWrapPos();

                        if (sources.Count > 0)
                            ImGui.Separator();
                    }

                    foreach (var (monster, location, method) in sources)
                    {
                        ImGui.TextUnformatted(UiStrings.Format(
                            UiStrings.Key.TooltipSourceLine, this.displayLanguage, this.GetMonsterName(monster), method.GetDisplayName(), this.FormatLocation(location)));
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

    private void DrawSpellbookMasterDetailLayout(List<Spell> rows, IReadOnlySet<uint> learnedSpellIds)
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
            this.DrawSpellbookDetailContent(this.selectedSpellbookSpell, learnedSpellIds);

        ImGui.EndChild();
    }

    private void DrawSpellbookDetailContent(Spell spell, IReadOnlySet<uint> learnedSpellIds)
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
                ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.SpellbookDescriptionGermanOnlyHint, this.displayLanguage));

            ImGui.PopTextWrapPos();
        }

        ImGui.Separator();

        foreach (var (monster, location, method) in sources)
        {
            ImGui.TextUnformatted(UiStrings.Format(
                UiStrings.Key.TooltipSourceLine, this.displayLanguage, this.GetMonsterName(monster), method.GetDisplayName(), this.FormatLocation(location)));
        }
    }

    private void DrawCardGrid<T>(
        string gridId,
        IReadOnlyList<T> items,
        Action<T> drawCardContent,
        float cardWidth = 220f,
        float cardHeight = 160f)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var columns = Math.Max(1, (int)((availableWidth + spacing) / (cardWidth + spacing)));

        for (var i = 0; i < items.Count; i++)
        {
            ImGui.BeginChild($"{gridId}Card{i}", new System.Numerics.Vector2(cardWidth, cardHeight), true);
            drawCardContent(items[i]);
            ImGui.EndChild();

            if ((i + 1) % columns != 0 && i < items.Count - 1)
                ImGui.SameLine();
        }
    }

    private static void AlignCursorToCardBottom()
    {
        var remainingHeight = ImGui.GetContentRegionAvail().Y;
        var buttonHeight = ImGui.GetFrameHeight();
        if (remainingHeight > buttonHeight)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + remainingHeight - buttonHeight);
    }

    private void DrawGroupFinderTab()
    {
        this.DrawLastMessage();

        if (!this.configuration.LiveSyncEnabled)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.GroupFinderInactiveHint, this.displayLanguage));

            if (ImGui.Button(UiStrings.Get(UiStrings.Key.GroupFinderGoToSettingsButton, this.displayLanguage)))
                this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.GroupFinderGoToSettingsMessage, this.displayLanguage));

            return;
        }

        if (!this.groupFinderVisibilityInitialized && this.liveSyncService.LastKnownOwnProfile is { } ownProfile)
        {
            this.groupFinderVisible = ownProfile.VisibleInGroupFinder;
            this.groupFinderTags = new HashSet<AvailabilityTag>(ownProfile.AvailabilityTags);
            this.groupFinderNoteBuffer = ownProfile.Note;
            this.groupFinderWantedPlayerCountBuffer = ownProfile.WantedPlayerCount.ToString();
            this.groupFinderVisibilityInitialized = true;
        }

        if (ImGui.BeginTabBar("GroupFinderSubTabs"))
        {
            if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.GroupFinderPublishSubTab, this.displayLanguage)))
            {
                if (ImGui.CollapsingHeader(
                        UiStrings.Get(UiStrings.Key.GroupFinderMyEntryHeader, this.displayLanguage),
                        ImGuiTreeNodeFlags.DefaultOpen))
                    this.DrawMyEntrySection();

                if (ImGui.CollapsingHeader(UiStrings.Get(UiStrings.Key.GroupPublishHeader, this.displayLanguage)))
                    this.DrawGroupPublishSection();

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.GroupFinderBrowseSubTab, this.displayLanguage)))
            {
                this.DrawGroupBrowseSection();
                ImGui.Separator();
                this.DrawOtherPlayersSection();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawMyEntrySection()
    {
        if (ImGui.Checkbox(UiStrings.Get(UiStrings.Key.GroupFinderVisibleToggle, this.displayLanguage), ref this.groupFinderVisible))
            this.liveSyncService.SetGroupFinderVisibility(this.groupFinderVisible);

        foreach (var tag in Enum.GetValues<AvailabilityTag>())
        {
            var selected = this.groupFinderTags.Contains(tag);
            if (ImGui.Checkbox($"{UiStrings.Get(GetAvailabilityTagLabelKey(tag), this.displayLanguage)}##GroupFinderTag{tag}", ref selected))
            {
                if (selected)
                    this.groupFinderTags.Add(tag);
                else
                    this.groupFinderTags.Remove(tag);

                this.liveSyncService.SetGroupFinderAvailabilityTags(this.groupFinderTags);
            }

            ImGui.SameLine();
        }

        ImGui.NewLine();

        ImGui.SetNextItemWidth(-1);
        var noteChanged = ImGui.InputText(UiStrings.Get(UiStrings.Key.GroupFinderNoteLabel, this.displayLanguage), ref this.groupFinderNoteBuffer, 60);

        ImGui.SetNextItemWidth(80);
        var wantedPlayerCountChanged = ImGui.InputText(
            UiStrings.Get(UiStrings.Key.GroupFinderWantedPlayerCountLabel, this.displayLanguage),
            ref this.groupFinderWantedPlayerCountBuffer, 2, ImGuiInputTextFlags.CharsDecimal);

        if (noteChanged || wantedPlayerCountChanged)
        {
            if (!int.TryParse(this.groupFinderWantedPlayerCountBuffer, out var wantedPlayerCount))
                wantedPlayerCount = 0;

            wantedPlayerCount = Math.Clamp(wantedPlayerCount, 0, 8);
            this.groupFinderWantedPlayerCountBuffer = wantedPlayerCount.ToString();

            this.liveSyncService.SetGroupFinderNoteAndWantedPlayerCount(this.groupFinderNoteBuffer, wantedPlayerCount);
        }

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.GroupFinderPublishButton, this.displayLanguage)))
        {
            this.liveSyncService.PushOwnProfile();
            this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.GroupFinderPublishedMessage, this.displayLanguage));
        }

        if (this.liveSyncService.LastKnownOwnProfile is { VisibleInGroupFinder: true } confirmedProfile)
        {
            var tagsText = confirmedProfile.AvailabilityTags.Count > 0
                ? string.Join(", ", confirmedProfile.AvailabilityTags.Select(tag => UiStrings.Get(GetAvailabilityTagLabelKey(tag), this.displayLanguage)))
                : "–";
            var noteText = string.IsNullOrEmpty(confirmedProfile.Note) ? "–" : $"\"{confirmedProfile.Note}\"";
            var wantedPlayerCountText = confirmedProfile.WantedPlayerCount == 0
                ? UiStrings.Get(UiStrings.Key.GroupFinderWantedPlayerCountAny, this.displayLanguage)
                : confirmedProfile.WantedPlayerCount.ToString();

            ImGui.TextColored(SuccessMessageColor, UiStrings.Format(
                UiStrings.Key.GroupFinderOwnVisibleConfirmation, this.displayLanguage, tagsText, noteText, wantedPlayerCountText));
        }
    }

    private void DrawOtherPlayersSection()
    {
        var dataCenter = this.liveSyncService.LastKnownOwnProfile?.DataCenter;
        if (dataCenter is null)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.GroupFinderDeterminingDataCenter, this.displayLanguage));
            return;
        }

        ImGui.TextUnformatted(UiStrings.Format(UiStrings.Key.GroupFinderOthersHeader, this.displayLanguage, dataCenter));
        ImGui.SameLine();

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.GroupFinderRefreshButton, this.displayLanguage)))
        {
            this.liveSyncService.TriggerBrowse();
            this.liveSyncService.TriggerGroupBrowse();

            this.lastGroupFinderAutoRefreshAt = DateTimeOffset.UtcNow;
        }

        ImGui.Separator();

        var localPlayerName = this.partyService.GetLocalPlayerName();
        var entries = this.liveSyncService.LastBrowseResults
            .OrderByDescending(entry => string.Equals(entry.CharacterName, localPlayerName, StringComparison.Ordinal))
            .ToList();

        if (entries.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.GroupFinderNoEntries, this.displayLanguage));
            return;
        }

        var totalSpellCount = this.spellDataService.Spells.Count;

        this.DrawCardGrid("GroupFinderEntry", entries, entry =>
        {
            var isOwnEntry = string.Equals(entry.CharacterName, localPlayerName, StringComparison.Ordinal);

            if (isOwnEntry)
                ImGui.PushStyleColor(ImGuiCol.Text, SuccessMessageColor);

            var nameText = $"{entry.CharacterName} ({entry.World})";
            if (isOwnEntry)
                nameText += UiStrings.Get(UiStrings.Key.YouSuffix, this.displayLanguage);

            ImGui.TextWrapped(nameText);
            ImGui.TextUnformatted(UiStrings.Format(
                UiStrings.Key.GroupFinderProgressFormat, this.displayLanguage, entry.LearnedSpellIds.Count, totalSpellCount));

            if (entry.AvailabilityTags.Count > 0)
            {
                var tagLabels = entry.AvailabilityTags.Select(tag => UiStrings.Get(GetAvailabilityTagLabelKey(tag), this.displayLanguage));
                ImGui.TextWrapped(string.Join(", ", tagLabels));
            }

            if (!string.IsNullOrEmpty(entry.Note))
                ImGui.TextWrapped(entry.Note);

            var wantedPlayerCountText = entry.WantedPlayerCount == 0
                ? UiStrings.Get(UiStrings.Key.GroupFinderWantedPlayerCountAny, this.displayLanguage)
                : entry.WantedPlayerCount.ToString();
            ImGui.TextUnformatted(UiStrings.Format(UiStrings.Key.GroupFinderWantedPlayerCountEntryFormat, this.displayLanguage, wantedPlayerCountText));

            if (isOwnEntry)
                ImGui.PopStyleColor();

            if (!isOwnEntry)
            {
                AlignCursorToCardBottom();
                if (ImGui.Button($"{UiStrings.Get(UiStrings.Key.GroupFinderAddToComparisonButton, this.displayLanguage)}##GroupFinderAdd{entry.CharacterName}"))
                {
                    var status = new PlayerSpellStatus
                    {
                        CharacterName = entry.CharacterName,
                        LearnedSpellIds = entry.LearnedSpellIds,
                        IsLocalPlayer = false,
                        World = entry.World,
                    };

                    this.syncProvider.PublishLocalStatus(status);
                    this.SetSuccessMessage(UiStrings.Format(UiStrings.Key.GroupFinderAddedToComparisonMessage, this.displayLanguage, entry.CharacterName));
                }
            }
        });
    }

    private void DrawGroupPublishSection()
    {
        if (ImGui.RadioButton(
                UiStrings.Get(UiStrings.Key.GroupPublishSourceParty, this.displayLanguage),
                this.groupMemberSource == GroupMemberSource.Party))
            this.groupMemberSource = GroupMemberSource.Party;

        ImGui.SameLine();

        if (ImGui.RadioButton(
                UiStrings.Get(UiStrings.Key.GroupPublishSourceSyncList, this.displayLanguage),
                this.groupMemberSource == GroupMemberSource.SyncList))
            this.groupMemberSource = GroupMemberSource.SyncList;

        if (this.groupMemberSource == GroupMemberSource.Party)
            this.DrawGroupPublishPartyMemberList();
        else
            this.DrawGroupPublishSyncListMemberList();

        ImGui.Separator();

        ImGui.Checkbox(UiStrings.Get(UiStrings.Key.GroupPublishVisibleToggle, this.displayLanguage), ref this.groupPublishVisible);

        foreach (var tag in Enum.GetValues<AvailabilityTag>())
        {
            var selected = this.groupPublishTags.Contains(tag);
            if (ImGui.Checkbox($"{UiStrings.Get(GetAvailabilityTagLabelKey(tag), this.displayLanguage)}##GroupPublishTag{tag}", ref selected))
            {
                if (selected)
                    this.groupPublishTags.Add(tag);
                else
                    this.groupPublishTags.Remove(tag);
            }

            ImGui.SameLine();
        }

        ImGui.NewLine();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputText(UiStrings.Get(UiStrings.Key.GroupPublishNoteLabel, this.displayLanguage), ref this.groupPublishNoteBuffer, 60);

        ImGui.SetNextItemWidth(80);
        ImGui.InputText(
            UiStrings.Get(UiStrings.Key.GroupPublishWantedPlayerCountLabel, this.displayLanguage),
            ref this.groupPublishWantedPlayerCountBuffer, 2, ImGuiInputTextFlags.CharsDecimal);

        var selectedCount = this.groupPublishSelectedMembers.Count;
        var canPublish = selectedCount is >= 1 and <= 8;

        ImGui.BeginDisabled(!canPublish);
        if (ImGui.Button(UiStrings.Get(UiStrings.Key.GroupPublishButton, this.displayLanguage)))
        {
            if (!int.TryParse(this.groupPublishWantedPlayerCountBuffer, out var wantedPlayerCount))
                wantedPlayerCount = 0;

            wantedPlayerCount = Math.Clamp(wantedPlayerCount, 0, 8);
            this.groupPublishWantedPlayerCountBuffer = wantedPlayerCount.ToString();

            var members = this.groupPublishSelectedMembers
                .Select(key =>
                {
                    var atIndex = key.IndexOf('@');
                    return (World: key[(atIndex + 1)..], CharacterName: key[..atIndex]);
                })
                .ToList();

            this.liveSyncService.PublishGroup(
                members, this.groupPublishVisible, this.groupPublishTags, this.groupPublishNoteBuffer, wantedPlayerCount);
        }

        ImGui.EndDisabled();

        if (this.liveSyncService.HasPublishedGroup())
        {
            ImGui.SameLine();
            if (ImGui.Button(UiStrings.Get(UiStrings.Key.GroupUnpublishButton, this.displayLanguage)))
                this.liveSyncService.DeletePublishedGroup();
        }
    }

    private void DrawGroupPublishPartyMemberList()
    {
        var partyMembers = this.partyService.GetBlueMagePartyMembers();

        if (partyMembers.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.NoBlueMagesInParty, this.displayLanguage));
            return;
        }

        foreach (var member in partyMembers)
            this.DrawGroupPublishMemberCheckbox(member.Name, member.World);
    }

    private void DrawGroupPublishSyncListMemberList()
    {
        var knownStatus = this.syncProvider.GetKnownPartyStatus();

        if (knownStatus.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.NoPlayerDataLoadedShort, this.displayLanguage));
            return;
        }

        foreach (var status in knownStatus)
        {
            if (status.World is null)
            {
                ImGui.BeginDisabled();
                var disabledSelected = false;
                ImGui.Checkbox($"{status.CharacterName}##GroupPublishMemberUnknownWorld{status.CharacterName}", ref disabledSelected);
                ImGui.EndDisabled();

                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(UiStrings.Get(UiStrings.Key.GroupFinderUnknownWorldHint, this.displayLanguage));

                continue;
            }

            this.DrawGroupPublishMemberCheckbox(status.CharacterName, status.World);
        }
    }

    private void DrawGroupPublishMemberCheckbox(string characterName, string world)
    {
        var key = $"{characterName}@{world}";
        var selected = this.groupPublishSelectedMembers.Contains(key);

        if (ImGui.Checkbox($"{characterName} ({world})##GroupPublishMember{key}", ref selected))
        {
            if (selected)
                this.groupPublishSelectedMembers.Add(key);
            else
                this.groupPublishSelectedMembers.Remove(key);
        }
    }

    private void DrawGroupBrowseSection()
    {
        ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.GroupFinderGroupsHeader, this.displayLanguage));
        ImGui.Separator();

        var groups = this.liveSyncService.LastGroupBrowseResults;
        if (groups.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.GroupFinderNoGroups, this.displayLanguage));
            return;
        }

        var allSpellIds = this.spellDataService.Spells.Keys;

        this.DrawCardGrid("GroupBrowseGroup", groups, group => this.DrawGroupBrowseEntry(group, allSpellIds), cardHeight: 220f);
    }

    private void DrawGroupBrowseEntry(GroupFinderGroupEntry group, IEnumerable<uint> allSpellIds)
    {
        this.DrawGroupMemberHeader(group.Members);

        if (group.AvailabilityTags.Count > 0)
        {
            var tagLabels = group.AvailabilityTags.Select(tag => UiStrings.Get(GetAvailabilityTagLabelKey(tag), this.displayLanguage));
            ImGui.TextWrapped(string.Join(", ", tagLabels));
        }

        if (!string.IsNullOrEmpty(group.Note))
            ImGui.TextWrapped(group.Note);

        var wantedPlayerCountText = group.WantedPlayerCount == 0
            ? UiStrings.Get(UiStrings.Key.GroupFinderWantedPlayerCountAny, this.displayLanguage)
            : group.WantedPlayerCount.ToString();

        ImGui.TextWrapped(UiStrings.Format(UiStrings.Key.GroupFinderWantedPlayerCountEntryFormat, this.displayLanguage, wantedPlayerCountText));

        var availableMembers = group.Members.Where(m => m.LearnedSpellIds is not null).ToList();

        if (availableMembers.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.GroupFinderGroupNoAvailableProfiles, this.displayLanguage));
        }
        else
        {
            var partyStatus = availableMembers
                .Select(m => new PlayerSpellStatus
                {
                    CharacterName = m.CharacterName,
                    LearnedSpellIds = m.LearnedSpellIds!,
                    IsLocalPlayer = false,
                    World = m.World,
                })
                .ToList();

            var commonlyMissingSpellIds = this.comparisonService.GetCommonlyMissingSpells(allSpellIds, partyStatus)
                .Where(m => m.PlayersMissingIt.Count == partyStatus.Count)
                .Select(m => m.SpellId)
                .ToHashSet();

            var ownLearnedIds = this.localSpellUnlockService.GetLearnedSpellIds();
            var contributableCount = commonlyMissingSpellIds.Count(ownLearnedIds.Contains);
            var stillMissingForYouCount = commonlyMissingSpellIds.Count - contributableCount;

            ImGui.TextWrapped(UiStrings.Format(UiStrings.Key.GroupFinderYouWouldContribute, this.displayLanguage, contributableCount));
            ImGui.TextWrapped(UiStrings.Format(UiStrings.Key.GroupFinderYouWouldStillMiss, this.displayLanguage, stillMissingForYouCount));
        }

        AlignCursorToCardBottom();
        ImGui.BeginDisabled(availableMembers.Count == 0);
        if (ImGui.Button($"{UiStrings.Get(UiStrings.Key.GroupFinderAddGroupToComparisonButton, this.displayLanguage)}##GroupBrowseAdd{group.GroupId}"))
        {
            foreach (var member in availableMembers)
            {
                var status = new PlayerSpellStatus
                {
                    CharacterName = member.CharacterName,
                    LearnedSpellIds = member.LearnedSpellIds!,
                    IsLocalPlayer = false,
                    World = member.World,
                };

                this.syncProvider.PublishLocalStatus(status);
            }

            this.SetSuccessMessage(UiStrings.Format(
                UiStrings.Key.GroupFinderGroupAddedToComparisonMessage, this.displayLanguage, availableMembers.Count));
        }

        ImGui.EndDisabled();
    }

    private void DrawGroupMemberHeader(IReadOnlyList<GroupFinderGroupMember> members)
    {
        if (members.Count == 0)
            return;

        var sameWorld = members.Select(m => m.World).Distinct().Count() <= 1;

        var memberTexts = members.Select(member =>
        {
            var nameText = sameWorld ? member.CharacterName : $"{member.CharacterName} ({member.World})";
            return member.LearnedSpellIds is null ? $"{nameText} (?)" : nameText;
        });

        var headerText = string.Join(", ", memberTexts);
        if (sameWorld)
            headerText += $" ({members[0].World})";

        ImGui.TextWrapped(headerText);

        if (members.Any(m => m.LearnedSpellIds is null) && ImGui.IsItemHovered())
            ImGui.SetTooltip(UiStrings.Get(UiStrings.Key.GroupFinderGroupMemberProfileUnavailableHint, this.displayLanguage));
    }

    private static UiStrings.Key GetAvailabilityTagLabelKey(AvailabilityTag tag) => tag switch
    {
        AvailabilityTag.Morning => UiStrings.Key.GroupFinderTagMorning,
        AvailabilityTag.Afternoon => UiStrings.Key.GroupFinderTagAfternoon,
        AvailabilityTag.Evening => UiStrings.Key.GroupFinderTagEvening,
        AvailabilityTag.Weekend => UiStrings.Key.GroupFinderTagWeekend,
        AvailabilityTag.Flexible => UiStrings.Key.GroupFinderTagFlexible,
        _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, null),
    };

    private void DrawSettingsTab()
    {
        this.DrawLastMessage();

        ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.DisplayLanguageHeader, this.displayLanguage));
        ImGui.Separator();

        foreach (var language in Enum.GetValues<DisplayLanguage>())
        {
            if (ImGui.RadioButton(GetNativeLanguageName(language), this.displayLanguage == language))
                this.displayLanguage = language;
        }

        ImGui.Separator();
        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.DisplayLanguageHint, this.displayLanguage));

        ImGui.Separator();
        ImGui.Checkbox(UiStrings.Get(UiStrings.Key.AutoShareToPartyChatToggle, this.displayLanguage), ref this.autoShareToPartyChat);
        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.AutoShareToPartyChatHint, this.displayLanguage));

        ImGui.Separator();

        var liveSyncEnabled = this.configuration.LiveSyncEnabled;
        if (ImGui.Checkbox(UiStrings.Get(UiStrings.Key.LiveSyncEnabledToggle, this.displayLanguage), ref liveSyncEnabled))
        {
            this.configuration.LiveSyncEnabled = liveSyncEnabled;
            this.configuration.Save();
        }

        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.LiveSyncEnabledHint, this.displayLanguage));

        var hasLiveSyncProfile = this.liveSyncService.HasEditTokenForLocalCharacter();
        ImGui.BeginDisabled(!hasLiveSyncProfile);
        if (ImGui.Button(UiStrings.Get(UiStrings.Key.LiveSyncDeleteProfileButton, this.displayLanguage)))
            this.liveSyncService.DeleteOwnProfile();
        ImGui.EndDisabled();
    }

    private static string GetNativeLanguageName(DisplayLanguage language) => language switch
    {
        DisplayLanguage.German => "Deutsch",
        DisplayLanguage.English => "English",
        DisplayLanguage.French => "Français",
        DisplayLanguage.Japanese => "日本語",
        _ => language.ToString(),
    };

    private string GetSpellName(Spell spell) => spell.GetName(this.displayLanguage);

    private string GetMonsterName(Monster monster) => monster.GetName(this.displayLanguage);

    public void Dispose() => this.chatGui.ChatMessage -= this.OnChatMessage;
}
