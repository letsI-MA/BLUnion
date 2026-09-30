using BLUnion.Models;

namespace BLUnion.Services;

public static class UiStrings
{
    public enum Key
    {
        WindowTitle,
        TabSpellComparison,
        TabLearningPlan,
        TabSettings,
        TabCategoryParty,
        TabCategorySpellbook,
        TabHome,
        TabPartyOverview,
        HomeEmptyStateText,
        HomePartyProgressFormat,
        HomeNextBestTargetHeader,
        HomeNextBestTargetFormat,
        HomeTrackButton,
        HomeSyncCardTitle,
        DashboardGoToPartyButton,
        DashboardGoToGroupsButton,
        DashboardGoToSettingsButton,
        DashboardGoToSpellbookButton,
        PartyOverviewHeader,
        PartyOverviewMissingCountFormat,
        PartyOverviewNoMissingSpells,
        ColumnPlayer,
        ColumnProgress,
        StatusBarSeparator,
        StatusBarProgressFormat,
        StatusBarLiveSyncActive,
        StatusBarSyncingTooltip,
        StatusBarLiveSyncInactive,
        NoBlueMagesInParty,
        PartyMemberEntry,
        NoPlayerDataLoaded,
        CommonlyMissingHeader,
        AllSpellsKnownByAll,
        ComparisonUrgencyLegend,
        SpellFilterHint,
        ColumnNumber,
        ColumnSpell,
        ColumnMissingFor,
        ColumnSources,
        TooltipMissingFor,
        TooltipSourceLine,
        SourceLineAfterLocationFormat,
        SourcesHeader,
        UnknownLocation,
        SourceCountSummary,
        SpellFallback,
        MonsterFallback,
        SyncIntro,
        DetermineAndExportButton,
        LocalPlayerFallbackName,
        ClipboardCopiedMessage,
        GenericError,
        ImportCodeLabel,
        ImportButton,
        ImportFailed,
        CurrentlyLoadedPlayersHeader,
        NoPlayerDataLoadedShort,
        PlayerSpellCount,
        YouSuffix,
        PlayerLastUpdatedJustNow,
        PlayerLastUpdatedMinutesAgoFormat,
        RemoveButton,
        DevToolHeader,
        DevLoadAliceButton,
        DevLoadBobButton,
        DevLoadCharlesButton,
        DevFixtureLoaded,
        DisplayLanguageHeader,
        DisplayLanguageHint,
        HideTotemsToggle,
        WebCompanionIntro,
        OpenInBrowserButton,
        CopyLinkButton,
        BrowserOpenedMessage,
        LinkCopiedMessage,
        ClipboardCopiedAndSharedMessage,
        AutoShareToPartyChatToggle,
        AutoShareToPartyChatHint,
        AutoImportSyncCodesToggle,
        AutoImportSyncCodesHint,
        AutoImportedMessage,
        LiveSyncEnabledToggle,
        LiveSyncEnabledHint,
        LiveSyncDeleteProfileButton,
        SettingsAdvancedSyncHeader,
        LiveSyncPushSucceeded,
        LiveSyncPushFailed,
        LiveSyncFetchFailed,
        LiveSyncDeleteSucceeded,
        LiveSyncDeleteFailed,
        LiveSyncBrowseFailed,
        DevPublishTestProfilesButton,
        DevTestProfilesPublished,
        DevTestProfilesFailed,

        TabGroupFinder,
        GroupFinderPublishSubTab,
        GroupFinderBrowseSubTab,
        GroupFinderInactiveHint,
        GroupFinderGoToSettingsButton,
        GroupFinderGoToSettingsMessage,
        GroupFinderMyEntryHeader,
        GroupFinderUnpublishButton,
        GroupFinderOwnVisibleConfirmation,
        GroupFinderTagMorning,
        GroupFinderTagAfternoon,
        GroupFinderTagEvening,
        GroupFinderTagWeekend,
        GroupFinderTagFlexible,
        GroupFinderNoteLabel,
        GroupFinderWantedPlayerCountLabel,
        GroupFinderWantedPlayerCountAny,
        GroupFinderPublishButton,
        GroupFinderUpdateButton,
        GroupFinderPublishedMessage,
        GroupFinderDiscordChannelHintFormat,
        GroupFinderDiscordChannelHintGeneric,
        OpenDiscordChannelButton,
        GroupFinderOthersHeader,
        GroupFinderDeterminingDataCenter,
        GroupFinderRefreshButton,
        GroupFinderAddToComparisonButton,
        GroupFinderAddedToComparisonMessage,
        GroupFinderNoEntries,
        GroupFinderProgressFormat,
        GroupFinderWantedPlayerCountEntryFormat,

        GroupPublishHeader,
        GroupPublishModeSolo,
        GroupPublishModeGroup,
        GroupPublishSourceParty,
        GroupPublishSourceSyncList,
        GroupFinderUnknownWorldHint,
        GroupPublishNoteLabel,
        CharCountFormat,
        GroupPublishWantedPlayerCountLabel,
        GroupPublishTargetSpellHeader,
        GroupPublishTargetSpellScopeOnlyMissing,
        GroupPublishTargetSpellScopeAll,
        GroupPublishTargetSpellCountFormat,
        GroupPublishButton,
        GroupUpdateButton,
        GroupUnpublishButton,
        GroupPublishSucceededMessage,
        GroupPublishFailedMessage,
        GroupUnpublishSucceededMessage,
        GroupUnpublishFailedMessage,

        GroupFinderGroupsHeader,
        GroupFinderNoGroups,
        GroupFinderGroupMemberProfileUnavailableHint,
        GroupFinderGroupNoAvailableProfiles,
        GroupFinderYouWouldContribute,
        GroupFinderYouWouldStillMiss,
        GroupFinderAddGroupToComparisonButton,
        GroupFinderGroupAddedToComparisonMessage,
        GroupFinderShowTargetSpellsButton,
        GroupTargetSpellPopupHeader,
        GroupBrowseFailed,
        BrowseTargetSpellFilterHeader,
        BrowseTargetSpellFilterNoMatches,

        TabSpellbook,
        SpellbookFilterAll,
        SpellbookFilterLearned,
        SpellbookFilterMissing,
        SpellbookNoResults,
        SpellbookDescriptionGermanOnlyHint,
        SpellbookNeededByPartyFormat,
        SpellbookGoToComparisonButton,
        ColumnStars,
        ColumnLearned,

        TabLoadouts,
        LoadoutContentTypeMaskedCarnivale,
        LoadoutContentTypeTank,
        LoadoutContentTypeHealer,
        LoadoutContentTypeOffHealer,
        LoadoutContentTypeDamageDealer,
        LoadoutsNoneForType,
        LoadoutSourceLabel,
        LoadoutProgressFormat,
        LoadoutOpenSourceButton,
        DisplayLanguageAutomatic,
        SourceMethodDuty,
        SourceMethodOpenWorld,
        SourceMethodLevequest,
        SourceMethodDungeonTrash,
        SourceMethodMaskedCarnivale,
        SourceMethodTotem,
        SourceMethodTotemSpellCount,
        SourceMethodTotemLevel,
        SourceMethodMaskedCarnivaleProgress,
        SourceMethodStartingSpell,
        GroupPublishRequiresOwnProfileHint,
        GroupPublishCreatorAlwaysMemberHint,
        GroupPublishCreatorRejectedMessage,
        GroupPublishMissingMembersMessage,
        GroupFinderNamesNotVerifiedHint,
        OpenDiscordServerButton,
        LiveSyncPushNameTaken,
        LearningPlanSummaryFormat,
        LearningPlanFilterAll,
        LearningPlanFilterDuties,
        LearningPlanFilterOpenWorld,
        LearningPlanLocationsHeader,
        LearningPlanLocationEntrySuffixFormat,
        LearningPlanNoLocations,
        LearningPlanBeneficiariesLabel,
        LearningPlanRequiredLevelHint,
        LearningPlanCarnivaleHeader,
        LearningPlanCarnivaleProgressFormat,
        LearningPlanTotemsHeader,
        LearningPlanTotemSpellCountFormat,
        LearningPlanTotemLevelFormat,
        LearningPlanTotemReady,
        DutyFinderOpenTooltip,
        DutyFinderOpenVariantTooltip,
        DutyFinderNotUnlockedTooltip,
        DutyFinderVariantNormal,
        DutyFinderVariantFallback,
        MapMarkTooltip,
        MapLinkToChatTooltip,
        TeleportToTooltip,
        AetheryteNotUnlockedTooltip,
        TeleportFailedMessage,
        TeleportOnLocationClickToggle,
        TeleportOnLocationClickHint,
        OnboardingWindowTitle,
        OnboardingIntro,
        OnboardingLiveSyncDataText,
        OnboardingManualText,
        OnboardingEnableLiveSyncButton,
        OnboardingManualButton,
        OnboardingChangeLaterHint,
        SharePresenceToggle,
        SharePresenceHint,
        SharePresenceRequiresLiveSyncHint,
        PresenceOnline,
        PresenceLastSeenMinutesFormat,
        PresenceLastSeenHoursFormat,
        PresenceLastSeenDaysFormat,
        CopyTellButton,
        TellCopiedMessage,
    }

    private static readonly Dictionary<Key, Dictionary<DisplayLanguage, string>> Strings = new()
    {
        [Key.WindowTitle] = new()
        {
            [DisplayLanguage.German] = "BLUnion",
            [DisplayLanguage.English] = "BLUnion",
            [DisplayLanguage.French] = "BLUnion",
            [DisplayLanguage.Japanese] = "BLUnion",
        },
        [Key.TabSpellComparison] = new()
        {
            [DisplayLanguage.German] = "Spell Comparison",
            [DisplayLanguage.English] = "Spell Comparison",
            [DisplayLanguage.French] = "Comparaison des sorts",
            [DisplayLanguage.Japanese] = "スペル比較",
        },
        [Key.TabLearningPlan] = new()
        {
            [DisplayLanguage.German] = "Lernplan",
            [DisplayLanguage.English] = "Learning Plan",
            [DisplayLanguage.French] = "Plan d'apprentissage",
            [DisplayLanguage.Japanese] = "習得プラン",
        },
        [Key.TabSettings] = new()
        {
            [DisplayLanguage.German] = "Settings",
            [DisplayLanguage.English] = "Settings",
            [DisplayLanguage.French] = "Paramètres",
            [DisplayLanguage.Japanese] = "設定",
        },
        [Key.TabCategoryParty] = new()
        {
            [DisplayLanguage.German] = "Party",
            [DisplayLanguage.English] = "Party",
            [DisplayLanguage.French] = "Groupe",
            [DisplayLanguage.Japanese] = "パーティ",
        },
        [Key.TabCategorySpellbook] = new()
        {
            [DisplayLanguage.German] = "Spellbook",
            [DisplayLanguage.English] = "Spellbook",
            [DisplayLanguage.French] = "Grimoire",
            [DisplayLanguage.Japanese] = "スペルブック",
        },
        [Key.TabHome] = new()
        {
            [DisplayLanguage.German] = "Übersicht",
            [DisplayLanguage.English] = "Overview",
            [DisplayLanguage.French] = "Aperçu",
            [DisplayLanguage.Japanese] = "概要",
        },
        [Key.TabPartyOverview] = new()
        {
            [DisplayLanguage.German] = "Übersicht",
            [DisplayLanguage.English] = "Overview",
            [DisplayLanguage.French] = "Aperçu",
            [DisplayLanguage.Japanese] = "概要",
        },
        [Key.HomeEmptyStateText] = new()
        {
            [DisplayLanguage.German] =
                "Noch keine Party-Daten geladen. Nutze den Gruppenfinder, um Mitspieler zu finden, " +
                "oder schau dir in der Zwischenzeit schon mal dein Spellbook an.",
            [DisplayLanguage.English] =
                "No party data loaded yet. Use the Group Finder to find party members, or check out " +
                "your spellbook in the meantime.",
            [DisplayLanguage.French] =
                "Aucune donnée de groupe chargée pour l'instant. Utilise la recherche de groupe pour " +
                "trouver des coéquipiers, ou consulte déjà ton grimoire en attendant.",
            [DisplayLanguage.Japanese] =
                "まだパーティデータが読み込まれていません。グループファインダーでパーティメンバーを" +
                "探すか、その間にスペルブックを確認してみましょう。",
        },
        [Key.HomePartyProgressFormat] = new()
        {
            [DisplayLanguage.German] = "Party-Fortschritt: im Schnitt {0}% gelernt ({1} Spieler)",
            [DisplayLanguage.English] = "Party progress: {0}% learned on average ({1} players)",
            [DisplayLanguage.French] = "Progression du groupe : {0} % appris en moyenne ({1} joueurs)",
            [DisplayLanguage.Japanese] = "パーティの進捗: 平均{0}%習得済み ({1}人)",
        },
        [Key.HomeNextBestTargetHeader] = new()
        {
            [DisplayLanguage.German] = "Nächstes sinnvolles Ziel",
            [DisplayLanguage.English] = "Next best target",
            [DisplayLanguage.French] = "Prochain objectif",
            [DisplayLanguage.Japanese] = "次に狙うべき目標",
        },
        [Key.HomeNextBestTargetFormat] = new()
        {
            [DisplayLanguage.German] = "{0} — fehlt {1} Spieler(n), lernbar bei {2}",
            [DisplayLanguage.English] = "{0} — missing for {1} player(s), learnable at {2}",
            [DisplayLanguage.French] = "{0} — manque à {1} joueur(s), apprenable auprès de {2}",
            [DisplayLanguage.Japanese] = "{0} — {1}人が未習得、{2}で習得可能",
        },
        [Key.HomeTrackButton] = new()
        {
            [DisplayLanguage.German] = "Verfolgen",
            [DisplayLanguage.English] = "Track",
            [DisplayLanguage.French] = "Suivre",
            [DisplayLanguage.Japanese] = "追跡",
        },
        [Key.HomeSyncCardTitle] = new()
        {
            [DisplayLanguage.German] = "Sync (Code-Austausch)",
            [DisplayLanguage.English] = "Sync (code exchange)",
            [DisplayLanguage.French] = "Synchro (échange de code)",
            [DisplayLanguage.Japanese] = "同期 (コード交換)",
        },
        [Key.DashboardGoToPartyButton] = new()
        {
            [DisplayLanguage.German] = "Zu Party",
            [DisplayLanguage.English] = "Go to Party",
            [DisplayLanguage.French] = "Aller au Groupe",
            [DisplayLanguage.Japanese] = "パーティへ",
        },
        [Key.DashboardGoToGroupsButton] = new()
        {
            [DisplayLanguage.German] = "Zum Gruppenfinder",
            [DisplayLanguage.English] = "Go to Group Finder",
            [DisplayLanguage.French] = "Aller à la recherche de groupe",
            [DisplayLanguage.Japanese] = "グループファインダーへ",
        },
        [Key.DashboardGoToSettingsButton] = new()
        {
            [DisplayLanguage.German] = "Zu Settings",
            [DisplayLanguage.English] = "Go to Settings",
            [DisplayLanguage.French] = "Aller aux Paramètres",
            [DisplayLanguage.Japanese] = "設定へ",
        },
        [Key.DashboardGoToSpellbookButton] = new()
        {
            [DisplayLanguage.German] = "Zum Spellbook",
            [DisplayLanguage.English] = "Go to Spellbook",
            [DisplayLanguage.French] = "Aller au Grimoire",
            [DisplayLanguage.Japanese] = "スペルブックへ",
        },
        [Key.PartyOverviewHeader] = new()
        {
            [DisplayLanguage.German] = "Party-Mitglieder",
            [DisplayLanguage.English] = "Party members",
            [DisplayLanguage.French] = "Membres du groupe",
            [DisplayLanguage.Japanese] = "パーティメンバー",
        },
        [Key.PartyOverviewMissingCountFormat] = new()
        {
            [DisplayLanguage.German] = "{0} fehlend",
            [DisplayLanguage.English] = "{0} missing",
            [DisplayLanguage.French] = "{0} manquant(s)",
            [DisplayLanguage.Japanese] = "未習得 {0} 個",
        },
        [Key.PartyOverviewNoMissingSpells] = new()
        {
            [DisplayLanguage.German] = "Alle Spells gelernt.",
            [DisplayLanguage.English] = "All spells learned.",
            [DisplayLanguage.French] = "Tous les sorts appris.",
            [DisplayLanguage.Japanese] = "すべてのスペルを習得済みです。",
        },
        [Key.ColumnPlayer] = new()
        {
            [DisplayLanguage.German] = "Spieler",
            [DisplayLanguage.English] = "Player",
            [DisplayLanguage.French] = "Joueur",
            [DisplayLanguage.Japanese] = "プレイヤー",
        },
        [Key.ColumnProgress] = new()
        {
            [DisplayLanguage.German] = "Fortschritt",
            [DisplayLanguage.English] = "Progress",
            [DisplayLanguage.French] = "Progression",
            [DisplayLanguage.Japanese] = "進捗",
        },
        [Key.StatusBarSeparator] = new()
        {
            [DisplayLanguage.German] = " · ",
            [DisplayLanguage.English] = " · ",
            [DisplayLanguage.French] = " · ",
            [DisplayLanguage.Japanese] = " · ",
        },
        [Key.StatusBarProgressFormat] = new()
        {
            [DisplayLanguage.German] = "{0}/{1} Spells",
            [DisplayLanguage.English] = "{0}/{1} spells",
            [DisplayLanguage.French] = "{0}/{1} sorts",
            [DisplayLanguage.Japanese] = "スペル {0}/{1}",
        },
        [Key.StatusBarLiveSyncActive] = new()
        {
            [DisplayLanguage.German] = "Live-Sync aktiv",
            [DisplayLanguage.English] = "Live sync active",
            [DisplayLanguage.French] = "Synchro en direct active",
            [DisplayLanguage.Japanese] = "ライブ同期：有効",
        },
        [Key.StatusBarSyncingTooltip] = new()
        {
            [DisplayLanguage.German] = "Sync läuft...",
            [DisplayLanguage.English] = "Syncing...",
            [DisplayLanguage.French] = "Synchronisation en cours...",
            [DisplayLanguage.Japanese] = "同期中...",
        },
        [Key.StatusBarLiveSyncInactive] = new()
        {
            [DisplayLanguage.German] = "Live-Sync inaktiv",
            [DisplayLanguage.English] = "Live sync inactive",
            [DisplayLanguage.French] = "Synchro en direct inactive",
            [DisplayLanguage.Japanese] = "ライブ同期：無効",
        },
        [Key.NoBlueMagesInParty] = new()
        {
            [DisplayLanguage.German] = "Keine Blue Mages in der aktuellen Party gefunden.",
            [DisplayLanguage.English] = "No Blue Mages found in the current party.",
            [DisplayLanguage.French] = "Aucun Mage bleu trouvé dans le groupe actuel.",
            [DisplayLanguage.Japanese] = "現在のパーティに青魔道士が見つかりません。",
        },
        [Key.PartyMemberEntry] = new()
        {
            [DisplayLanguage.German] = "{0}  (Level {1})",
            [DisplayLanguage.English] = "{0}  (Level {1})",
            [DisplayLanguage.French] = "{0}  (Niveau {1})",
            [DisplayLanguage.Japanese] = "{0}  (レベル{1})",
        },
        [Key.NoPlayerDataLoaded] = new()
        {
            [DisplayLanguage.German] =
                "Noch keine Spielerdaten geladen. Gehe zum '{0}'-Tab, um deinen eigenen Status zu " +
                "ermitteln und Codes von Mitspielern zu importieren.",
            [DisplayLanguage.English] =
                "No player data loaded yet. Go to the '{0}' tab to determine your own status and " +
                "import codes from party members.",
            [DisplayLanguage.French] =
                "Aucune donnée de joueur chargée pour l'instant. Va dans l'onglet « {0} » pour " +
                "déterminer ton propre statut et importer les codes de tes coéquipiers.",
            [DisplayLanguage.Japanese] =
                "まだプレイヤーデータが読み込まれていません。「{0}」タブで自分の状況を確認し、" +
                "パーティメンバーのコードをインポートしてください。",
        },
        [Key.CommonlyMissingHeader] = new()
        {
            [DisplayLanguage.German] = "Gemeinsam fehlend:",
            [DisplayLanguage.English] = "Commonly missing:",
            [DisplayLanguage.French] = "Sorts manquants en commun :",
            [DisplayLanguage.Japanese] = "共通して未習得:",
        },
        [Key.AllSpellsKnownByAll] = new()
        {
            [DisplayLanguage.German] = "Alle bekannten Spells sind bei allen geladenen Spielern vorhanden.",
            [DisplayLanguage.English] = "All known spells are already learned by every loaded player.",
            [DisplayLanguage.French] = "Tous les sorts connus sont déjà appris par tous les joueurs chargés.",
            [DisplayLanguage.Japanese] = "既知のスペルはすべて、読み込んだプレイヤー全員が習得済みです。",
        },
        [Key.ComparisonUrgencyLegend] = new()
        {
            [DisplayLanguage.German] = "Zeilenfarbe: Rot = fehlt der gesamten Party, Gelb = fehlt der Mehrheit.",
            [DisplayLanguage.English] = "Row color: red = missing for the whole party, yellow = missing for the majority.",
            [DisplayLanguage.French] = "Couleur de ligne : rouge = manque à tout le groupe, jaune = manque à la majorité.",
            [DisplayLanguage.Japanese] = "行の色：赤＝パーティ全員が未修得、黄＝過半数が未修得。",
        },
        [Key.SpellFilterHint] = new()
        {
            [DisplayLanguage.German] = "Filter: Name oder Nr. (z.B. 58, #058, a)...",
            [DisplayLanguage.English] = "Filter: name or # (e.g. 58, #058, a)...",
            [DisplayLanguage.French] = "Filtre : nom ou n° (p. ex. 58, #058, a)...",
            [DisplayLanguage.Japanese] = "フィルター: 名前または番号 (例: 58、#058、a)...",
        },
        [Key.ColumnNumber] = new()
        {
            [DisplayLanguage.German] = "#",
            [DisplayLanguage.English] = "#",
            [DisplayLanguage.French] = "#",
            [DisplayLanguage.Japanese] = "#",
        },
        [Key.ColumnSpell] = new()
        {
            [DisplayLanguage.German] = "Spell",
            [DisplayLanguage.English] = "Spell",
            [DisplayLanguage.French] = "Sort",
            [DisplayLanguage.Japanese] = "スペル",
        },
        [Key.ColumnMissingFor] = new()
        {
            [DisplayLanguage.German] = "Fehlt bei",
            [DisplayLanguage.English] = "Missing for",
            [DisplayLanguage.French] = "Manque à",
            [DisplayLanguage.Japanese] = "未習得者",
        },
        [Key.ColumnSources] = new()
        {
            [DisplayLanguage.German] = "Quelle(n)",
            [DisplayLanguage.English] = "Source(s)",
            [DisplayLanguage.French] = "Source(s)",
            [DisplayLanguage.Japanese] = "入手先",
        },
        [Key.TooltipMissingFor] = new()
        {
            [DisplayLanguage.German] = "Fehlt bei: {0}",
            [DisplayLanguage.English] = "Missing for: {0}",
            [DisplayLanguage.French] = "Manque à : {0}",
            [DisplayLanguage.Japanese] = "未習得者: {0}",
        },
        [Key.TooltipSourceLine] = new()
        {
            // {0} = Ort, {1} = Monster, {2} = Lernmethode. Muss "{0} " + SourceLineAfterLocationFormat
            // ergeben (Spellbook-Detail zeichnet den Ort klickbar und den Rest getrennt).
            [DisplayLanguage.German] = "{0} - {1} ({2})",
            [DisplayLanguage.English] = "{0} - {1} ({2})",
            [DisplayLanguage.French] = "{0} - {1} ({2})",
            [DisplayLanguage.Japanese] = "{0} - {1}({2})",
        },
        [Key.SourceLineAfterLocationFormat] = new()
        {
            // Rest der Quellzeile hinter dem (klickbaren) Ort: {0} = Monster, {1} = Lernmethode.
            [DisplayLanguage.German] = "- {0} ({1})",
            [DisplayLanguage.English] = "- {0} ({1})",
            [DisplayLanguage.French] = "- {0} ({1})",
            [DisplayLanguage.Japanese] = "- {0}({1})",
        },
        [Key.SourcesHeader] = new()
        {
            [DisplayLanguage.German] = "Quellen:",
            [DisplayLanguage.English] = "Sources:",
            [DisplayLanguage.French] = "Sources :",
            [DisplayLanguage.Japanese] = "入手先:",
        },
        [Key.UnknownLocation] = new()
        {
            [DisplayLanguage.German] = "unbekannt",
            [DisplayLanguage.English] = "unknown",
            [DisplayLanguage.French] = "inconnu",
            [DisplayLanguage.Japanese] = "不明",
        },
        [Key.SourceCountSummary] = new()
        {
            [DisplayLanguage.German] = "{0} Quellen",
            [DisplayLanguage.English] = "{0} sources",
            [DisplayLanguage.French] = "{0} sources",
            [DisplayLanguage.Japanese] = "入手先 {0} 件",
        },
        [Key.SpellFallback] = new()
        {
            [DisplayLanguage.German] = "Spell #{0}",
            [DisplayLanguage.English] = "Spell #{0}",
            [DisplayLanguage.French] = "Sort n° {0}",
            [DisplayLanguage.Japanese] = "スペル #{0}",
        },
        [Key.MonsterFallback] = new()
        {
            [DisplayLanguage.German] = "Monster #{0}",
            [DisplayLanguage.English] = "Monster #{0}",
            [DisplayLanguage.French] = "Monstre n° {0}",
            [DisplayLanguage.Japanese] = "モンスター #{0}",
        },
        [Key.SyncIntro] = new()
        {
            [DisplayLanguage.German] =
                "Sync-Option A (MVP): Exportiere deinen eigenen Status als Code und teile ihn z.B. " +
                "über Discord. Mitspieler importieren ihn hier.",
            [DisplayLanguage.English] =
                "Sync option A (MVP): export your own status as a code and share it, e.g. via " +
                "Discord. Party members import it here.",
            [DisplayLanguage.French] =
                "Option de synchro A (MVP) : exporte ton propre statut sous forme de code et " +
                "partage-le, p. ex. via Discord. Les coéquipiers l'importent ici.",
            [DisplayLanguage.Japanese] =
                "同期方式A (MVP): 自分の状況をコードとしてエクスポートし、Discordなどで共有します。" +
                "パーティメンバーはここでインポートします。",
        },
        [Key.DetermineAndExportButton] = new()
        {
            [DisplayLanguage.German] = "Eigenen Status ermitteln + exportieren",
            [DisplayLanguage.English] = "Determine + export own status",
            [DisplayLanguage.French] = "Déterminer + exporter mon statut",
            [DisplayLanguage.Japanese] = "自分の状況を確認してエクスポート",
        },
        [Key.LocalPlayerFallbackName] = new()
        {
            [DisplayLanguage.German] = "Du",
            [DisplayLanguage.English] = "You",
            [DisplayLanguage.French] = "Toi",
            [DisplayLanguage.Japanese] = "自分",
        },
        [Key.ClipboardCopiedMessage] = new()
        {
            [DisplayLanguage.German] = "Code in Zwischenablage kopiert.",
            [DisplayLanguage.English] = "Code copied to clipboard.",
            [DisplayLanguage.French] = "Code copié dans le presse-papiers.",
            [DisplayLanguage.Japanese] = "コードをクリップボードにコピーしました。",
        },
        [Key.GenericError] = new()
        {
            [DisplayLanguage.German] = "Fehler: {0}",
            [DisplayLanguage.English] = "Error: {0}",
            [DisplayLanguage.French] = "Erreur : {0}",
            [DisplayLanguage.Japanese] = "エラー: {0}",
        },
        [Key.ImportCodeLabel] = new()
        {
            [DisplayLanguage.German] = "Code eines Mitspielers",
            [DisplayLanguage.English] = "Party member's code",
            [DisplayLanguage.French] = "Code d'un coéquipier",
            [DisplayLanguage.Japanese] = "パーティメンバーのコード",
        },
        [Key.ImportButton] = new()
        {
            [DisplayLanguage.German] = "Importieren",
            [DisplayLanguage.English] = "Import",
            [DisplayLanguage.French] = "Importer",
            [DisplayLanguage.Japanese] = "インポート",
        },
        [Key.ImportFailed] = new()
        {
            [DisplayLanguage.German] = "Import fehlgeschlagen: {0}",
            [DisplayLanguage.English] = "Import failed: {0}",
            [DisplayLanguage.French] = "Échec de l'import : {0}",
            [DisplayLanguage.Japanese] = "インポートに失敗しました: {0}",
        },
        [Key.CurrentlyLoadedPlayersHeader] = new()
        {
            [DisplayLanguage.German] = "Aktuell geladene Spieler:",
            [DisplayLanguage.English] = "Currently loaded players:",
            [DisplayLanguage.French] = "Joueurs actuellement chargés :",
            [DisplayLanguage.Japanese] = "現在読み込まれているプレイヤー:",
        },
        [Key.NoPlayerDataLoadedShort] = new()
        {
            [DisplayLanguage.German] = "Noch keine Spielerdaten geladen.",
            [DisplayLanguage.English] = "No player data loaded yet.",
            [DisplayLanguage.French] = "Aucune donnée de joueur chargée pour l'instant.",
            [DisplayLanguage.Japanese] = "まだプレイヤーデータが読み込まれていません。",
        },
        [Key.PlayerSpellCount] = new()
        {
            [DisplayLanguage.German] = "{0} ({1} Spells)",
            [DisplayLanguage.English] = "{0} ({1} spells)",
            [DisplayLanguage.French] = "{0} ({1} sorts)",
            [DisplayLanguage.Japanese] = "{0} ({1}スペル)",
        },
        [Key.YouSuffix] = new()
        {
            [DisplayLanguage.German] = " — Du",
            [DisplayLanguage.English] = " — You",
            [DisplayLanguage.French] = " — Toi",
            [DisplayLanguage.Japanese] = " — 自分",
        },
        [Key.PlayerLastUpdatedJustNow] = new()
        {
            [DisplayLanguage.German] = " — gerade aktualisiert",
            [DisplayLanguage.English] = " — just updated",
            [DisplayLanguage.French] = " — mis à jour à l'instant",
            [DisplayLanguage.Japanese] = " — たった今更新",
        },
        [Key.PlayerLastUpdatedMinutesAgoFormat] = new()
        {
            [DisplayLanguage.German] = " — vor {0} Min. aktualisiert",
            [DisplayLanguage.English] = " — updated {0} min ago",
            [DisplayLanguage.French] = " — mis à jour il y a {0} min",
            [DisplayLanguage.Japanese] = " — {0}分前に更新",
        },
        [Key.RemoveButton] = new()
        {
            [DisplayLanguage.German] = "Entfernen",
            [DisplayLanguage.English] = "Remove",
            [DisplayLanguage.French] = "Retirer",
            [DisplayLanguage.Japanese] = "削除",
        },
        [Key.DevToolHeader] = new()
        {
            [DisplayLanguage.German] = "Dev-Tool (keine echte Party-Funktion):",
            [DisplayLanguage.English] = "Dev tool (not a real party feature):",
            [DisplayLanguage.French] = "Outil dev (pas une vraie fonction de groupe) :",
            [DisplayLanguage.Japanese] = "開発用ツール (実際のパーティ機能ではありません):",
        },
        [Key.DevLoadAliceButton] = new()
        {
            [DisplayLanguage.German] = "Dev: Alice laden",
            [DisplayLanguage.English] = "Dev: Load Alice",
            [DisplayLanguage.French] = "Dev : charger Alice",
            [DisplayLanguage.Japanese] = "開発: Aliceを読み込む",
        },
        [Key.DevLoadBobButton] = new()
        {
            [DisplayLanguage.German] = "Dev: Bob laden",
            [DisplayLanguage.English] = "Dev: Load Bob",
            [DisplayLanguage.French] = "Dev : charger Bob",
            [DisplayLanguage.Japanese] = "開発: Bobを読み込む",
        },
        [Key.DevLoadCharlesButton] = new()
        {
            [DisplayLanguage.German] = "Dev: Charles laden",
            [DisplayLanguage.English] = "Dev: Load Charles",
            [DisplayLanguage.French] = "Dev : charger Charles",
            [DisplayLanguage.Japanese] = "開発: Charlesを読み込む",
        },
        [Key.DevFixtureLoaded] = new()
        {
            [DisplayLanguage.German] = "Dev-Tool: '{0}' mit {1} Spells geladen.",
            [DisplayLanguage.English] = "Dev tool: '{0}' loaded with {1} spells.",
            [DisplayLanguage.French] = "Outil dev : « {0} » chargé avec {1} sorts.",
            [DisplayLanguage.Japanese] = "開発用ツール: 「{0}」を{1}スペルで読み込みました。",
        },
        [Key.DisplayLanguageHeader] = new()
        {
            [DisplayLanguage.German] = "Anzeigesprache:",
            [DisplayLanguage.English] = "Display language:",
            [DisplayLanguage.French] = "Langue d'affichage :",
            [DisplayLanguage.Japanese] = "表示言語:",
        },
        [Key.DisplayLanguageHint] = new()
        {
            [DisplayLanguage.German] =
                "Gilt für die komplette Oberfläche (inkl. Spell-Namen) und wird gespeichert. " +
                "\"Automatisch\" folgt der Sprache deines Clients.",
            [DisplayLanguage.English] =
                "Applies to the entire interface (including spell names) and is saved. " +
                "\"Automatic\" follows your client language.",
            [DisplayLanguage.French] =
                "S'applique à toute l'interface (y compris les noms de sorts) et est enregistré. " +
                "« Automatique » suit la langue de ton client.",
            [DisplayLanguage.Japanese] =
                "画面全体(スペル名を含む)に適用され、設定は保存されます。" +
                "「自動」はクライアント言語に従います。",
        },
        [Key.HideTotemsToggle] = new()
        {
            [DisplayLanguage.German] = "Totems ausblenden",
            [DisplayLanguage.English] = "Hide totems",
            [DisplayLanguage.French] = "Masquer les totems",
            [DisplayLanguage.Japanese] = "トーテムを非表示",
        },
        [Key.WebCompanionIntro] = new()
        {
            [DisplayLanguage.German] =
                "Es gibt auch eine Browser-Version, mit der du deinen Spellstatus als Code " +
                "exportieren oder einen Code importieren kannst - ganz ohne dass FFXIV läuft. " +
                "Praktisch, um Freunden ohne installiertes Plugin die Teilnahme zu ermöglichen.",
            [DisplayLanguage.English] =
                "There's also a browser version where you can export your spell status as a code " +
                "or import one - without FFXIV even running. Handy for letting friends without the " +
                "plugin installed join in.",
            [DisplayLanguage.French] =
                "Il existe aussi une version navigateur qui permet d'exporter ton statut de sorts " +
                "sous forme de code ou d'en importer un - sans même que FFXIV soit lancé. Pratique " +
                "pour permettre à des amis sans le plugin installé d'y participer.",
            [DisplayLanguage.Japanese] =
                "FFXIVを起動していなくても、自分のスペル状況をコードとしてエクスポートしたり、コード" +
                "をインポートしたりできるブラウザ版もあります。プラグインをインストールしていない" +
                "友達も参加しやすくなります。",
        },
        [Key.OpenInBrowserButton] = new()
        {
            [DisplayLanguage.German] = "Im Browser öffnen",
            [DisplayLanguage.English] = "Open in browser",
            [DisplayLanguage.French] = "Ouvrir dans le navigateur",
            [DisplayLanguage.Japanese] = "ブラウザで開く",
        },
        [Key.CopyLinkButton] = new()
        {
            [DisplayLanguage.German] = "Link kopieren",
            [DisplayLanguage.English] = "Copy link",
            [DisplayLanguage.French] = "Copier le lien",
            [DisplayLanguage.Japanese] = "リンクをコピー",
        },
        [Key.BrowserOpenedMessage] = new()
        {
            [DisplayLanguage.German] = "Im Browser geöffnet.",
            [DisplayLanguage.English] = "Opened in browser.",
            [DisplayLanguage.French] = "Ouvert dans le navigateur.",
            [DisplayLanguage.Japanese] = "ブラウザで開きました。",
        },
        [Key.LinkCopiedMessage] = new()
        {
            [DisplayLanguage.German] = "Link in Zwischenablage kopiert.",
            [DisplayLanguage.English] = "Link copied to clipboard.",
            [DisplayLanguage.French] = "Lien copié dans le presse-papiers.",
            [DisplayLanguage.Japanese] = "リンクをクリップボードにコピーしました。",
        },
        [Key.ClipboardCopiedAndSharedMessage] = new()
        {
            [DisplayLanguage.German] = "Status in Zwischenablage kopiert und in den Party-Chat gepostet.",
            [DisplayLanguage.English] = "Status copied to clipboard and posted to party chat.",
            [DisplayLanguage.French] = "Statut copié dans le presse-papiers et publié dans le chat de groupe.",
            [DisplayLanguage.Japanese] = "状況をクリップボードにコピーし、パーティチャットに投稿しました。",
        },
        [Key.AutoShareToPartyChatToggle] = new()
        {
            [DisplayLanguage.German] = "Code beim Export automatisch in den Party-Chat (/p) posten",
            [DisplayLanguage.English] = "Automatically post the code to party chat (/p) on export",
            [DisplayLanguage.French] = "Publier automatiquement le code dans le chat de groupe (/p) lors de l'export",
            [DisplayLanguage.Japanese] = "エクスポート時にコードを自動的にパーティチャット (/p) に投稿する",
        },
        [Key.AutoShareToPartyChatHint] = new()
        {
            [DisplayLanguage.German] =
                "Passiert nur, wenn du aktuell in einer Party bist, und höchstens alle 10 Sekunden - " +
                "so gibt es keinen Chat-Spam, falls du mehrmals hintereinander auf 'exportieren' klickst.",
            [DisplayLanguage.English] =
                "Only happens while you're currently in a party, and at most once every 10 seconds - " +
                "so clicking 'export' repeatedly won't spam the chat.",
            [DisplayLanguage.French] =
                "Ne se produit que si tu es actuellement dans un groupe, et au maximum toutes les " +
                "10 secondes - ainsi, cliquer plusieurs fois sur « exporter » ne spamme pas le chat.",
            [DisplayLanguage.Japanese] =
                "現在パーティに参加している場合のみ実行され、最短でも10秒間隔です。「エクスポート」を" +
                "連続でクリックしてもチャットがスパムされません。",
        },
        [Key.AutoImportSyncCodesToggle] = new()
        {
            [DisplayLanguage.German] = "Eingehende Sync-Codes aus dem Party-Chat automatisch übernehmen",
            [DisplayLanguage.English] = "Automatically import incoming sync codes from party chat",
            [DisplayLanguage.French] = "Importer automatiquement les codes de synchro reçus dans le chat de groupe",
            [DisplayLanguage.Japanese] = "パーティチャットで受信した同期コードを自動的に取り込む",
        },
        [Key.AutoImportSyncCodesHint] = new()
        {
            [DisplayLanguage.German] =
                "Durchsucht alle Chat-Kanäle nach Codes im Format \"BLU:...\" - deine eigenen Codes " +
                "werden dabei nicht erneut importiert.",
            [DisplayLanguage.English] =
                "Scans all chat channels for codes in the \"BLU:...\" format - your own codes won't be " +
                "re-imported.",
            [DisplayLanguage.French] =
                "Analyse tous les canaux de discussion à la recherche de codes au format « BLU:... » - " +
                "tes propres codes ne sont pas réimportés.",
            [DisplayLanguage.Japanese] =
                "すべてのチャットチャンネルを「BLU:...」形式のコードについて検索します。自分自身の" +
                "コードは再インポートされません。",
        },
        [Key.AutoImportedMessage] = new()
        {
            [DisplayLanguage.German] = "Automatisch importiert: {0}",
            [DisplayLanguage.English] = "Automatically imported: {0}",
            [DisplayLanguage.French] = "Importé automatiquement : {0}",
            [DisplayLanguage.Japanese] = "自動的にインポートしました: {0}",
        },
        [Key.LiveSyncEnabledToggle] = new()
        {
            [DisplayLanguage.German] = "Live-Sync aktivieren",
            [DisplayLanguage.English] = "Enable Live-Sync",
            [DisplayLanguage.French] = "Activer la synchro en direct",
            [DisplayLanguage.Japanese] = "ライブ同期を有効にする",
        },
        [Key.LiveSyncEnabledHint] = new()
        {
            [DisplayLanguage.German] =
                "Legt deinen Spell-Status auf einem externen Server ab und ruft automatisch die " +
                "Profile anderer Blue Mages in deiner Party ab - kein manueller Code-Austausch " +
                "mehr nötig. Dein Status ist dort öffentlich per Charaktername + World abrufbar " +
                "(ohne Login), aber nicht durchsuchbar.",
            [DisplayLanguage.English] =
                "Stores your spell status on an external server and automatically fetches the " +
                "profiles of other Blue Mages in your party - no more manual code exchange. Your " +
                "status is publicly readable there via character name + world (no login), but not " +
                "browsable/searchable.",
            [DisplayLanguage.French] =
                "Enregistre ton statut de sorts sur un serveur externe et récupère automatiquement " +
                "les profils des autres Mages bleus de ton groupe - plus besoin d'échanger des " +
                "codes manuellement. Ton statut y est lisible publiquement via nom de personnage + " +
                "monde (sans connexion), mais non consultable/recherchable.",
            [DisplayLanguage.Japanese] =
                "自分のスペル状況を外部サーバーに保存し、パーティ内の他の青魔道士のプロフィールを" +
                "自動的に取得します。手動でのコード交換は不要になります。自分の状況はキャラクター名" +
                "とワールドで(ログインなしに)誰でも閲覧可能ですが、一覧検索はできません。",
        },
        [Key.LiveSyncDeleteProfileButton] = new()
        {
            [DisplayLanguage.German] = "Mein Profil löschen",
            [DisplayLanguage.English] = "Delete my profile",
            [DisplayLanguage.French] = "Supprimer mon profil",
            [DisplayLanguage.Japanese] = "自分のプロフィールを削除",
        },
        [Key.SettingsAdvancedSyncHeader] = new()
        {
            [DisplayLanguage.German] = "Erweitert: Sync",
            [DisplayLanguage.English] = "Advanced: Sync",
            [DisplayLanguage.French] = "Avancé : Synchro",
            [DisplayLanguage.Japanese] = "詳細設定: 同期",
        },
        [Key.LiveSyncPushSucceeded] = new()
        {
            [DisplayLanguage.German] = "Live-Sync: eigenes Profil aktualisiert.",
            [DisplayLanguage.English] = "Live-Sync: own profile updated.",
            [DisplayLanguage.French] = "Synchro en direct : profil mis à jour.",
            [DisplayLanguage.Japanese] = "ライブ同期: 自分のプロフィールを更新しました。",
        },
        [Key.LiveSyncPushFailed] = new()
        {
            [DisplayLanguage.German] = "Live-Sync: Aktualisieren des eigenen Profils fehlgeschlagen ({0})",
            [DisplayLanguage.English] = "Live-Sync: failed to update own profile ({0})",
            [DisplayLanguage.French] = "Synchro en direct : échec de la mise à jour du profil ({0})",
            [DisplayLanguage.Japanese] = "ライブ同期: 自分のプロフィールの更新に失敗しました ({0})",
        },
        [Key.LiveSyncFetchFailed] = new()
        {
            [DisplayLanguage.German] = "Live-Sync: Abrufen mindestens eines Party-Profils fehlgeschlagen ({0})",
            [DisplayLanguage.English] = "Live-Sync: failed to fetch at least one party profile ({0})",
            [DisplayLanguage.French] = "Synchro en direct : échec de la récupération d'au moins un profil du groupe ({0})",
            [DisplayLanguage.Japanese] = "ライブ同期: 少なくとも1件のパーティプロフィールの取得に失敗しました ({0})",
        },
        [Key.LiveSyncDeleteSucceeded] = new()
        {
            [DisplayLanguage.German] = "Live-Sync: eigenes Profil gelöscht.",
            [DisplayLanguage.English] = "Live-Sync: own profile deleted.",
            [DisplayLanguage.French] = "Synchro en direct : profil supprimé.",
            [DisplayLanguage.Japanese] = "ライブ同期: 自分のプロフィールを削除しました。",
        },
        [Key.LiveSyncDeleteFailed] = new()
        {
            [DisplayLanguage.German] = "Live-Sync: Löschen des eigenen Profils fehlgeschlagen ({0})",
            [DisplayLanguage.English] = "Live-Sync: failed to delete own profile ({0})",
            [DisplayLanguage.French] = "Synchro en direct : échec de la suppression du profil ({0})",
            [DisplayLanguage.Japanese] = "ライブ同期: 自分のプロフィールの削除に失敗しました ({0})",
        },
        [Key.LiveSyncBrowseFailed] = new()
        {
            [DisplayLanguage.German] = "Gruppenfinder: Abrufen anderer Spieler fehlgeschlagen ({0})",
            [DisplayLanguage.English] = "Group finder: failed to fetch other players ({0})",
            [DisplayLanguage.French] = "Recherche de groupe : échec de la récupération des autres joueurs ({0})",
            [DisplayLanguage.Japanese] = "グループファインダー: 他プレイヤーの取得に失敗しました ({0})",
        },
        [Key.DevPublishTestProfilesButton] = new()
        {
            [DisplayLanguage.German] = "Dev: Testprofile im Gruppenfinder veröffentlichen",
            [DisplayLanguage.English] = "Dev: Publish test profiles to group finder",
            [DisplayLanguage.French] = "Dev : publier des profils de test dans la recherche de groupe",
            [DisplayLanguage.Japanese] = "開発: テストプロフィールをグループファインダーに公開",
        },
        [Key.DevTestProfilesPublished] = new()
        {
            [DisplayLanguage.German] = "Dev: {0} Testprofile im Gruppenfinder veröffentlicht.",
            [DisplayLanguage.English] = "Dev: published {0} test profiles to the group finder.",
            [DisplayLanguage.French] = "Dev : {0} profils de test publiés dans la recherche de groupe.",
            [DisplayLanguage.Japanese] = "開発: {0}件のテストプロフィールをグループファインダーに公開しました。",
        },
        [Key.DevTestProfilesFailed] = new()
        {
            [DisplayLanguage.German] = "Dev: Veröffentlichen der Testprofile fehlgeschlagen ({0})",
            [DisplayLanguage.English] = "Dev: failed to publish test profiles ({0})",
            [DisplayLanguage.French] = "Dev : échec de la publication des profils de test ({0})",
            [DisplayLanguage.Japanese] = "開発: テストプロフィールの公開に失敗しました ({0})",
        },

        [Key.TabGroupFinder] = new()
        {
            [DisplayLanguage.German] = "Gruppenfinder",
            [DisplayLanguage.English] = "Group Finder",
            [DisplayLanguage.French] = "Recherche de groupe",
            [DisplayLanguage.Japanese] = "グループファインダー",
        },
        [Key.GroupFinderPublishSubTab] = new()
        {
            [DisplayLanguage.German] = "Veröffentlichen",
            [DisplayLanguage.English] = "Publish",
            [DisplayLanguage.French] = "Publier",
            [DisplayLanguage.Japanese] = "公開",
        },
        [Key.GroupFinderBrowseSubTab] = new()
        {
            [DisplayLanguage.German] = "Durchsuchen",
            [DisplayLanguage.English] = "Browse",
            [DisplayLanguage.French] = "Parcourir",
            [DisplayLanguage.Japanese] = "検索",
        },
        [Key.GroupFinderInactiveHint] = new()
        {
            [DisplayLanguage.German] =
                "Der Gruppenfinder erweitert dein Live-Sync-Profil um Verfügbarkeit, Notiz und " +
                "gewünschte Mitspieleranzahl - es gibt keinen eigenen Gruppenfinder-Login. Aktiviere " +
                "zuerst Live-Sync im Settings-Tab, um hier sichtbar zu werden und andere Spieler zu finden.",
            [DisplayLanguage.English] =
                "The group finder extends your Live-Sync profile with availability, a note and a " +
                "desired player count - there's no separate group finder login. Enable Live-Sync in " +
                "the Settings tab first to become visible here and find other players.",
            [DisplayLanguage.French] =
                "La recherche de groupe complète ton profil de synchro en direct avec disponibilité, " +
                "note et nombre de coéquipiers souhaité - il n'y a pas de connexion séparée. Active " +
                "d'abord la synchro en direct dans l'onglet Paramètres pour devenir visible ici et " +
                "trouver d'autres joueurs.",
            [DisplayLanguage.Japanese] =
                "グループファインダーは、あなたのライブ同期プロフィールに空き時間・メモ・希望人数を追加" +
                "する機能です。専用のログインはありません。ここで表示され、他のプレイヤーを見つけるには" +
                "まず設定タブでライブ同期を有効にしてください。",
        },
        [Key.GroupFinderGoToSettingsButton] = new()
        {
            [DisplayLanguage.German] = "Wo finde ich Live-Sync?",
            [DisplayLanguage.English] = "Where do I find Live-Sync?",
            [DisplayLanguage.French] = "Où trouver la synchro en direct ?",
            [DisplayLanguage.Japanese] = "ライブ同期はどこ?",
        },
        [Key.GroupFinderGoToSettingsMessage] = new()
        {
            [DisplayLanguage.German] = "Live-Sync aktivierst du im Settings-Tab (Checkbox 'Live-Sync aktivieren').",
            [DisplayLanguage.English] = "You can enable Live-Sync in the Settings tab (checkbox 'Enable Live-Sync').",
            [DisplayLanguage.French] = "Tu actives la synchro en direct dans l'onglet Paramètres (case « Activer la synchro en direct »).",
            [DisplayLanguage.Japanese] = "ライブ同期は設定タブの「ライブ同期を有効にする」チェックボックスで有効化できます。",
        },
        [Key.GroupFinderMyEntryHeader] = new()
        {
            [DisplayLanguage.German] = "Mein Eintrag:",
            [DisplayLanguage.English] = "My entry:",
            [DisplayLanguage.French] = "Mon entrée :",
            [DisplayLanguage.Japanese] = "自分の登録内容:",
        },
        [Key.GroupFinderUnpublishButton] = new()
        {
            [DisplayLanguage.German] = "Eintrag wieder löschen",
            [DisplayLanguage.English] = "Delete entry",
            [DisplayLanguage.French] = "Supprimer l'entrée",
            [DisplayLanguage.Japanese] = "登録を削除",
        },
        [Key.GroupFinderOwnVisibleConfirmation] = new()
        {
            [DisplayLanguage.German] = "Dein Profil ist im Gruppenfinder sichtbar ({0}, {1}, gesucht: {2}).",
            [DisplayLanguage.English] = "Your profile is visible in the group finder ({0}, {1}, looking for: {2}).",
            [DisplayLanguage.French] = "Ton profil est visible dans la recherche de groupe ({0}, {1}, recherché : {2}).",
            [DisplayLanguage.Japanese] = "あなたのプロフィールはグループファインダーに表示されています ({0}、{1}、募集: {2})。",
        },
        [Key.GroupFinderTagMorning] = new()
        {
            [DisplayLanguage.German] = "Morgens",
            [DisplayLanguage.English] = "Morning",
            [DisplayLanguage.French] = "Matin",
            [DisplayLanguage.Japanese] = "朝",
        },
        [Key.GroupFinderTagAfternoon] = new()
        {
            [DisplayLanguage.German] = "Nachmittags",
            [DisplayLanguage.English] = "Afternoon",
            [DisplayLanguage.French] = "Après-midi",
            [DisplayLanguage.Japanese] = "昼",
        },
        [Key.GroupFinderTagEvening] = new()
        {
            [DisplayLanguage.German] = "Abends",
            [DisplayLanguage.English] = "Evening",
            [DisplayLanguage.French] = "Soir",
            [DisplayLanguage.Japanese] = "夜",
        },
        [Key.GroupFinderTagWeekend] = new()
        {
            [DisplayLanguage.German] = "Wochenende",
            [DisplayLanguage.English] = "Weekend",
            [DisplayLanguage.French] = "Week-end",
            [DisplayLanguage.Japanese] = "週末",
        },
        [Key.GroupFinderTagFlexible] = new()
        {
            [DisplayLanguage.German] = "Flexibel",
            [DisplayLanguage.English] = "Flexible",
            [DisplayLanguage.French] = "Flexible",
            [DisplayLanguage.Japanese] = "柔軟",
        },
        [Key.GroupFinderNoteLabel] = new()
        {
            [DisplayLanguage.German] = "Notiz (max. 60 Zeichen)",
            [DisplayLanguage.English] = "Note (max. 60 characters)",
            [DisplayLanguage.French] = "Note (60 caractères max.)",
            [DisplayLanguage.Japanese] = "メモ (最大60文字)",
        },
        [Key.GroupFinderWantedPlayerCountLabel] = new()
        {
            [DisplayLanguage.German] = "Gewünschte Mitspieleranzahl (0 = egal)",
            [DisplayLanguage.English] = "Desired player count (0 = any)",
            [DisplayLanguage.French] = "Nombre de coéquipiers souhaité (0 = peu importe)",
            [DisplayLanguage.Japanese] = "希望人数 (0 = 指定なし)",
        },
        [Key.GroupFinderWantedPlayerCountAny] = new()
        {
            [DisplayLanguage.German] = "egal wie viele",
            [DisplayLanguage.English] = "any number",
            [DisplayLanguage.French] = "peu importe",
            [DisplayLanguage.Japanese] = "人数指定なし",
        },
        [Key.GroupFinderPublishButton] = new()
        {
            [DisplayLanguage.German] = "Jetzt veröffentlichen",
            [DisplayLanguage.English] = "Publish now",
            [DisplayLanguage.French] = "Publier maintenant",
            [DisplayLanguage.Japanese] = "今すぐ公開",
        },
        [Key.GroupFinderUpdateButton] = new()
        {
            [DisplayLanguage.German] = "Aktualisieren",
            [DisplayLanguage.English] = "Update",
            [DisplayLanguage.French] = "Mettre à jour",
            [DisplayLanguage.Japanese] = "更新",
        },
        [Key.GroupFinderPublishedMessage] = new()
        {
            [DisplayLanguage.German] = "Gruppenfinder-Eintrag aktualisiert.",
            [DisplayLanguage.English] = "Group Finder entry updated.",
            [DisplayLanguage.French] = "Entrée du chercheur de groupe mise à jour.",
            [DisplayLanguage.Japanese] = "グループファインダーの登録内容を更新しました。",
        },
        [Key.GroupFinderDiscordChannelHintFormat] = new()
        {
            [DisplayLanguage.German] = "Sichtbar in der Gruppensuche · Discord: #{0}",
            [DisplayLanguage.English] = "Visible in the group finder · Discord: #{0}",
            [DisplayLanguage.French] = "Visible dans la recherche de groupe · Discord : #{0}",
            [DisplayLanguage.Japanese] = "グループファインダーに表示中 · Discord: #{0}",
        },
        [Key.GroupFinderDiscordChannelHintGeneric] = new()
        {
            [DisplayLanguage.German] = "Sichtbar in der Gruppensuche · Discord verfügbar",
            [DisplayLanguage.English] = "Visible in the group finder · Discord available",
            [DisplayLanguage.French] = "Visible dans la recherche de groupe · Discord disponible",
            [DisplayLanguage.Japanese] = "グループファインダーに表示中 · Discordあり",
        },
        [Key.OpenDiscordChannelButton] = new()
        {
            [DisplayLanguage.German] = "Discord-Kanal öffnen",
            [DisplayLanguage.English] = "Open Discord channel",
            [DisplayLanguage.French] = "Ouvrir le salon Discord",
            [DisplayLanguage.Japanese] = "Discordチャンネルを開く",
        },
        [Key.GroupFinderOthersHeader] = new()
        {
            [DisplayLanguage.German] = "Andere Spieler auf {0}:",
            [DisplayLanguage.English] = "Other players on {0}:",
            [DisplayLanguage.French] = "Autres joueurs sur {0} :",
            [DisplayLanguage.Japanese] = "{0}の他プレイヤー:",
        },
        [Key.GroupFinderDeterminingDataCenter] = new()
        {
            [DisplayLanguage.German] = "Data Center wird ermittelt...",
            [DisplayLanguage.English] = "Determining data center...",
            [DisplayLanguage.French] = "Détermination du data center...",
            [DisplayLanguage.Japanese] = "データセンターを確認中...",
        },
        [Key.GroupFinderRefreshButton] = new()
        {
            [DisplayLanguage.German] = "Aktualisieren",
            [DisplayLanguage.English] = "Refresh",
            [DisplayLanguage.French] = "Actualiser",
            [DisplayLanguage.Japanese] = "更新",
        },
        [Key.GroupFinderAddToComparisonButton] = new()
        {
            [DisplayLanguage.German] = "In Vergleich aufnehmen",
            [DisplayLanguage.English] = "Add to comparison",
            [DisplayLanguage.French] = "Ajouter à la comparaison",
            [DisplayLanguage.Japanese] = "比較に追加",
        },
        [Key.GroupFinderAddedToComparisonMessage] = new()
        {
            [DisplayLanguage.German] = "'{0}' wurde in den Spell-Vergleich aufgenommen.",
            [DisplayLanguage.English] = "'{0}' was added to the spell comparison.",
            [DisplayLanguage.French] = "« {0} » a été ajouté à la comparaison des sorts.",
            [DisplayLanguage.Japanese] = "「{0}」をスペル比較に追加しました。",
        },
        [Key.GroupFinderNoEntries] = new()
        {
            [DisplayLanguage.German] = "Aktuell keine anderen sichtbaren Spieler im Gruppenfinder auf diesem Data Center.",
            [DisplayLanguage.English] = "Currently no other visible players in the group finder on this data center.",
            [DisplayLanguage.French] = "Actuellement aucun autre joueur visible dans la recherche de groupe sur ce data center.",
            [DisplayLanguage.Japanese] = "現在このデータセンターのグループファインダーに他の表示中プレイヤーはいません。",
        },
        [Key.GroupFinderProgressFormat] = new()
        {
            [DisplayLanguage.German] = "{0}/{1} gelernt",
            [DisplayLanguage.English] = "{0}/{1} learned",
            [DisplayLanguage.French] = "{0}/{1} appris",
            [DisplayLanguage.Japanese] = "{0}/{1} 習得済み",
        },
        [Key.GroupFinderWantedPlayerCountEntryFormat] = new()
        {
            [DisplayLanguage.German] = "Gesucht: {0}",
            [DisplayLanguage.English] = "Looking for: {0}",
            [DisplayLanguage.French] = "Recherché : {0}",
            [DisplayLanguage.Japanese] = "募集: {0}",
        },

        [Key.GroupPublishHeader] = new()
        {
            [DisplayLanguage.German] = "Eigene Gruppe veröffentlichen:",
            [DisplayLanguage.English] = "Publish own group:",
            [DisplayLanguage.French] = "Publier mon groupe :",
            [DisplayLanguage.Japanese] = "自分のグループを公開:",
        },
        [Key.GroupPublishModeSolo] = new()
        {
            [DisplayLanguage.German] = "Solo",
            [DisplayLanguage.English] = "Solo",
            [DisplayLanguage.French] = "Solo",
            [DisplayLanguage.Japanese] = "ソロ",
        },
        [Key.GroupPublishModeGroup] = new()
        {
            [DisplayLanguage.German] = "Gruppe",
            [DisplayLanguage.English] = "Group",
            [DisplayLanguage.French] = "Groupe",
            [DisplayLanguage.Japanese] = "グループ",
        },
        [Key.GroupPublishSourceParty] = new()
        {
            [DisplayLanguage.German] = "Party",
            [DisplayLanguage.English] = "Party",
            [DisplayLanguage.French] = "Groupe",
            [DisplayLanguage.Japanese] = "パーティ",
        },
        [Key.GroupPublishSourceSyncList] = new()
        {
            [DisplayLanguage.German] = "Sync-Liste",
            [DisplayLanguage.English] = "Sync list",
            [DisplayLanguage.French] = "Liste de synchro",
            [DisplayLanguage.Japanese] = "同期リスト",
        },
        [Key.GroupFinderUnknownWorldHint] = new()
        {
            [DisplayLanguage.German] =
                "World unbekannt - nur über Party oder Gruppenfinder bezogene Mitglieder können " +
                "in eine Gruppe aufgenommen werden.",
            [DisplayLanguage.English] =
                "World unknown - only members obtained via party or group finder can be added to " +
                "a group.",
            [DisplayLanguage.French] =
                "Monde inconnu - seuls les membres obtenus via le groupe ou la recherche de groupe " +
                "peuvent être ajoutés à une groupe.",
            [DisplayLanguage.Japanese] =
                "ワールド不明 - パーティまたはグループファインダー経由で取得したメンバーのみ" +
                "グループに追加できます。",
        },
        [Key.GroupPublishNoteLabel] = new()
        {
            [DisplayLanguage.German] = "Notiz für die Gruppe (max. 60 Zeichen)",
            [DisplayLanguage.English] = "Note for the group (max. 60 characters)",
            [DisplayLanguage.French] = "Note pour le groupe (60 caractères max.)",
            [DisplayLanguage.Japanese] = "グループのメモ (最大60文字)",
        },
        // Sprachunabhängiges "aktuell/maximal"-Format (reine Zahlen/Satzzeichen) - trotzdem als
        // UiStrings-Key statt hartcodiert, damit der Zeichenzähler in DrawGroupPublishSection dem
        // bestehenden Stil folgt (siehe Aufgabenstellung).
        [Key.CharCountFormat] = new()
        {
            [DisplayLanguage.German] = "{0}/{1}",
            [DisplayLanguage.English] = "{0}/{1}",
            [DisplayLanguage.French] = "{0}/{1}",
            [DisplayLanguage.Japanese] = "{0}/{1}",
        },
        [Key.GroupPublishWantedPlayerCountLabel] = new()
        {
            [DisplayLanguage.German] = "Gewünschte Mitspieleranzahl für die Gruppe (0 = egal)",
            [DisplayLanguage.English] = "Desired player count for the group (0 = any)",
            [DisplayLanguage.French] = "Nombre de coéquipiers souhaité pour le groupe (0 = peu importe)",
            [DisplayLanguage.Japanese] = "グループの希望人数 (0 = 指定なし)",
        },
        [Key.GroupPublishTargetSpellHeader] = new()
        {
            [DisplayLanguage.German] = "Ziel-Spells",
            [DisplayLanguage.English] = "Target spells",
            [DisplayLanguage.French] = "Sorts cibles",
            [DisplayLanguage.Japanese] = "目標スペル",
        },
        [Key.GroupPublishTargetSpellScopeOnlyMissing] = new()
        {
            [DisplayLanguage.German] = "Nur eigene fehlende Spells anzeigen",
            [DisplayLanguage.English] = "Show only my missing spells",
            [DisplayLanguage.French] = "Afficher seulement mes sorts manquants",
            [DisplayLanguage.Japanese] = "自分の未修得スペルのみ表示",
        },
        [Key.GroupPublishTargetSpellScopeAll] = new()
        {
            [DisplayLanguage.German] = "Alle Spells anzeigen",
            [DisplayLanguage.English] = "Show all spells",
            [DisplayLanguage.French] = "Afficher tous les sorts",
            [DisplayLanguage.Japanese] = "すべてのスペルを表示",
        },
        [Key.GroupPublishTargetSpellCountFormat] = new()
        {
            [DisplayLanguage.German] = "{0} Ziel-Spells ausgewählt",
            [DisplayLanguage.English] = "{0} target spells selected",
            [DisplayLanguage.French] = "{0} sorts cibles sélectionnés",
            [DisplayLanguage.Japanese] = "{0}個の目標スペルを選択中",
        },
        [Key.GroupPublishButton] = new()
        {
            [DisplayLanguage.German] = "Gruppe veröffentlichen",
            [DisplayLanguage.English] = "Publish group",
            [DisplayLanguage.French] = "Publier le groupe",
            [DisplayLanguage.Japanese] = "グループを公開",
        },
        [Key.GroupUpdateButton] = new()
        {
            [DisplayLanguage.German] = "Gruppe aktualisieren",
            [DisplayLanguage.English] = "Update group",
            [DisplayLanguage.French] = "Mettre à jour le groupe",
            [DisplayLanguage.Japanese] = "グループを更新",
        },
        [Key.GroupUnpublishButton] = new()
        {
            [DisplayLanguage.German] = "Gruppe wieder löschen",
            [DisplayLanguage.English] = "Delete group",
            [DisplayLanguage.French] = "Supprimer le groupe",
            [DisplayLanguage.Japanese] = "グループを削除",
        },
        [Key.GroupPublishSucceededMessage] = new()
        {
            [DisplayLanguage.German] = "Gruppe veröffentlicht/aktualisiert.",
            [DisplayLanguage.English] = "Group published/updated.",
            [DisplayLanguage.French] = "Groupe publié/mis à jour.",
            [DisplayLanguage.Japanese] = "グループを公開/更新しました。",
        },
        [Key.GroupPublishFailedMessage] = new()
        {
            [DisplayLanguage.German] = "Veröffentlichen der Gruppe fehlgeschlagen ({0})",
            [DisplayLanguage.English] = "Failed to publish group ({0})",
            [DisplayLanguage.French] = "Échec de la publication du groupe ({0})",
            [DisplayLanguage.Japanese] = "グループの公開に失敗しました ({0})",
        },
        [Key.GroupUnpublishSucceededMessage] = new()
        {
            [DisplayLanguage.German] = "Gruppe gelöscht.",
            [DisplayLanguage.English] = "Group deleted.",
            [DisplayLanguage.French] = "Groupe supprimé.",
            [DisplayLanguage.Japanese] = "グループを削除しました。",
        },
        [Key.GroupUnpublishFailedMessage] = new()
        {
            [DisplayLanguage.German] = "Löschen der Gruppe fehlgeschlagen ({0})",
            [DisplayLanguage.English] = "Failed to delete group ({0})",
            [DisplayLanguage.French] = "Échec de la suppression du groupe ({0})",
            [DisplayLanguage.Japanese] = "グループの削除に失敗しました ({0})",
        },

        [Key.GroupFinderGroupsHeader] = new()
        {
            [DisplayLanguage.German] = "Gruppen:",
            [DisplayLanguage.English] = "Groups:",
            [DisplayLanguage.French] = "Groupes :",
            [DisplayLanguage.Japanese] = "グループ:",
        },
        [Key.GroupFinderNoGroups] = new()
        {
            [DisplayLanguage.German] = "Aktuell keine veröffentlichten Gruppen im Gruppenfinder auf diesem Data Center.",
            [DisplayLanguage.English] = "Currently no published groups in the group finder on this data center.",
            [DisplayLanguage.French] = "Actuellement aucun groupe publié dans la recherche de groupe sur ce data center.",
            [DisplayLanguage.Japanese] = "現在このデータセンターのグループファインダーに公開されているグループはありません。",
        },
        [Key.GroupFinderGroupMemberProfileUnavailableHint] = new()
        {
            [DisplayLanguage.German] = "Profil dieses Mitglieds nicht verfügbar (gelöscht oder abgelaufen) - wird beim Vergleich nicht berücksichtigt.",
            [DisplayLanguage.English] = "This member's profile is unavailable (deleted or expired) - not considered in the comparison.",
            [DisplayLanguage.French] = "Le profil de ce membre n'est pas disponible (supprimé ou expiré) - non pris en compte dans la comparaison.",
            [DisplayLanguage.Japanese] = "このメンバーのプロフィールは利用できません(削除または期限切れ) - 比較には含まれません。",
        },
        [Key.GroupFinderGroupNoAvailableProfiles] = new()
        {
            [DisplayLanguage.German] = "Kein Vergleich möglich - für keines der Mitglieder dieser Gruppe ist aktuell ein Profil verfügbar.",
            [DisplayLanguage.English] = "No comparison possible - none of this group's members currently have an available profile.",
            [DisplayLanguage.French] = "Comparaison impossible - aucun membre de ce groupe n'a actuellement de profil disponible.",
            [DisplayLanguage.Japanese] = "比較できません - このグループのメンバーの中に利用可能なプロフィールがありません。",
        },
        [Key.GroupFinderYouWouldContribute] = new()
        {
            [DisplayLanguage.German] = "Du würdest beitragen: {0} Spells",
            [DisplayLanguage.English] = "You would contribute: {0} spells",
            [DisplayLanguage.French] = "Tu apporterais : {0} sorts",
            [DisplayLanguage.Japanese] = "あなたが貢献できる: {0}スペル",
        },
        [Key.GroupFinderYouWouldStillMiss] = new()
        {
            [DisplayLanguage.German] = "Dir würde weiterhin fehlen: {0} Spells",
            [DisplayLanguage.English] = "You would still be missing: {0} spells",
            [DisplayLanguage.French] = "Il te manquerait encore : {0} sorts",
            [DisplayLanguage.Japanese] = "あなたにまだ不足している: {0}スペル",
        },
        [Key.GroupFinderAddGroupToComparisonButton] = new()
        {
            [DisplayLanguage.German] = "Gruppe zum Vergleich hinzufügen",
            [DisplayLanguage.English] = "Add group to comparison",
            [DisplayLanguage.French] = "Ajouter le groupe à la comparaison",
            [DisplayLanguage.Japanese] = "グループを比較に追加",
        },
        [Key.GroupFinderGroupAddedToComparisonMessage] = new()
        {
            [DisplayLanguage.German] = "{0} Gruppenmitglied(er) wurden in den Spell-Vergleich aufgenommen.",
            [DisplayLanguage.English] = "{0} group member(s) were added to the spell comparison.",
            [DisplayLanguage.French] = "{0} membre(s) du groupe ont été ajoutés à la comparaison des sorts.",
            [DisplayLanguage.Japanese] = "{0}人のグループメンバーをスペル比較に追加しました。",
        },
        [Key.GroupFinderShowTargetSpellsButton] = new()
        {
            [DisplayLanguage.German] = "Ziel-Spells anzeigen",
            [DisplayLanguage.English] = "Show target spells",
            [DisplayLanguage.French] = "Afficher les sorts cibles",
            [DisplayLanguage.Japanese] = "目標スペルを表示",
        },
        [Key.GroupTargetSpellPopupHeader] = new()
        {
            [DisplayLanguage.German] = "Ziel-Spells dieser Gruppe ({0})",
            [DisplayLanguage.English] = "This group's target spells ({0})",
            [DisplayLanguage.French] = "Sorts cibles de ce groupe ({0})",
            [DisplayLanguage.Japanese] = "このグループの目標スペル ({0})",
        },
        [Key.GroupBrowseFailed] = new()
        {
            [DisplayLanguage.German] = "Gruppenfinder: Abrufen anderer Gruppen fehlgeschlagen ({0})",
            [DisplayLanguage.English] = "Group finder: failed to fetch other groups ({0})",
            [DisplayLanguage.French] = "Recherche de groupe : échec de la récupération des autres groupes ({0})",
            [DisplayLanguage.Japanese] = "グループファインダー: 他グループの取得に失敗しました ({0})",
        },
        [Key.BrowseTargetSpellFilterHeader] = new()
        {
            [DisplayLanguage.German] = "Nach Ziel-Spells filtern",
            [DisplayLanguage.English] = "Filter by target spells",
            [DisplayLanguage.French] = "Filtrer par sorts cibles",
            [DisplayLanguage.Japanese] = "目標スペルで絞り込む",
        },
        [Key.BrowseTargetSpellFilterNoMatches] = new()
        {
            [DisplayLanguage.German] = "Keine Treffer für diese Ziel-Spell-Auswahl.",
            [DisplayLanguage.English] = "No matches for this target spell selection.",
            [DisplayLanguage.French] = "Aucun résultat pour cette sélection de sorts cibles.",
            [DisplayLanguage.Japanese] = "この目標スペルの選択に一致する結果はありません。",
        },

        [Key.TabSpellbook] = new()
        {
            [DisplayLanguage.German] = "Spellbook",
            [DisplayLanguage.English] = "Spellbook",
            [DisplayLanguage.French] = "Grimoire",
            [DisplayLanguage.Japanese] = "スペルブック",
        },
        [Key.SpellbookFilterAll] = new()
        {
            [DisplayLanguage.German] = "Alle",
            [DisplayLanguage.English] = "All",
            [DisplayLanguage.French] = "Tous",
            [DisplayLanguage.Japanese] = "すべて",
        },
        [Key.SpellbookFilterLearned] = new()
        {
            [DisplayLanguage.German] = "Gelernt",
            [DisplayLanguage.English] = "Learned",
            [DisplayLanguage.French] = "Appris",
            [DisplayLanguage.Japanese] = "習得済み",
        },
        [Key.SpellbookFilterMissing] = new()
        {
            [DisplayLanguage.German] = "Fehlend",
            [DisplayLanguage.English] = "Missing",
            [DisplayLanguage.French] = "Manquants",
            [DisplayLanguage.Japanese] = "未習得",
        },
        [Key.SpellbookNoResults] = new()
        {
            [DisplayLanguage.German] = "Keine Spells entsprechen dem aktuellen Filter.",
            [DisplayLanguage.English] = "No spells match the current filter.",
            [DisplayLanguage.French] = "Aucun sort ne correspond au filtre actuel.",
            [DisplayLanguage.Japanese] = "現在のフィルターに一致するスペルはありません。",
        },
        [Key.SpellbookDescriptionGermanOnlyHint] = new()
        {
            [DisplayLanguage.German] = "(nur auf Deutsch verfügbar)",
            [DisplayLanguage.English] = "(only available in German)",
            [DisplayLanguage.French] = "(disponible uniquement en allemand)",
            [DisplayLanguage.Japanese] = "(ドイツ語のみ利用可能)",
        },
        [Key.SpellbookNeededByPartyFormat] = new()
        {
            [DisplayLanguage.German] = "Wird von {0} Mitspieler(n) noch benötigt.",
            [DisplayLanguage.English] = "Still needed by {0} party member(s).",
            [DisplayLanguage.French] = "Encore nécessaire pour {0} coéquipier(s).",
            [DisplayLanguage.Japanese] = "あと{0}人のパーティメンバーが未習得です。",
        },
        [Key.SpellbookGoToComparisonButton] = new()
        {
            [DisplayLanguage.German] = "Zum Vergleich",
            [DisplayLanguage.English] = "Go to Comparison",
            [DisplayLanguage.French] = "Aller à la comparaison",
            [DisplayLanguage.Japanese] = "比較へ",
        },
        [Key.ColumnStars] = new()
        {
            [DisplayLanguage.German] = "Sterne",
            [DisplayLanguage.English] = "Stars",
            [DisplayLanguage.French] = "Étoiles",
            [DisplayLanguage.Japanese] = "★",
        },
        [Key.ColumnLearned] = new()
        {
            [DisplayLanguage.German] = "Gelernt",
            [DisplayLanguage.English] = "Learned",
            [DisplayLanguage.French] = "Appris",
            [DisplayLanguage.Japanese] = "習得",
        },

        [Key.TabLoadouts] = new()
        {
            [DisplayLanguage.German] = "Loadouts",
            [DisplayLanguage.English] = "Loadouts",
            [DisplayLanguage.French] = "Sets de sorts",
            [DisplayLanguage.Japanese] = "ロードアウト",
        },
        [Key.LoadoutContentTypeMaskedCarnivale] = new()
        {
            [DisplayLanguage.German] = "Maskenkarneval",
            [DisplayLanguage.English] = "Masked Carnivale",
            [DisplayLanguage.French] = "Carnaval masqué",
            [DisplayLanguage.Japanese] = "仮面舞踏会",
        },
        [Key.LoadoutContentTypeTank] = new()
        {
            [DisplayLanguage.German] = "Verteidiger",
            [DisplayLanguage.English] = "Tank",
            [DisplayLanguage.French] = "Tank",
            [DisplayLanguage.Japanese] = "タンク",
        },
        [Key.LoadoutContentTypeHealer] = new()
        {
            [DisplayLanguage.German] = "Heiler",
            [DisplayLanguage.English] = "Healer",
            [DisplayLanguage.French] = "Soigneur",
            [DisplayLanguage.Japanese] = "ヒーラー",
        },
        [Key.LoadoutContentTypeOffHealer] = new()
        {
            [DisplayLanguage.German] = "Off-Heiler",
            [DisplayLanguage.English] = "Off-Healer",
            [DisplayLanguage.French] = "Soigneur d'appoint",
            [DisplayLanguage.Japanese] = "サブヒーラー",
        },
        [Key.LoadoutContentTypeDamageDealer] = new()
        {
            [DisplayLanguage.German] = "Angreifer",
            [DisplayLanguage.English] = "Damage Dealer",
            [DisplayLanguage.French] = "DPS",
            [DisplayLanguage.Japanese] = "DPS",
        },
        [Key.LoadoutsNoneForType] = new()
        {
            [DisplayLanguage.German] = "Noch keine Loadouts für diesen Content-Typ hinterlegt.",
            [DisplayLanguage.English] = "No loadouts for this content type yet.",
            [DisplayLanguage.French] = "Aucun set de sorts pour ce type de contenu pour l'instant.",
            [DisplayLanguage.Japanese] = "このコンテンツタイプのロードアウトはまだ登録されていません。",
        },
        [Key.LoadoutSourceLabel] = new()
        {
            [DisplayLanguage.German] = "Quelle: {0}",
            [DisplayLanguage.English] = "Source: {0}",
            [DisplayLanguage.French] = "Source : {0}",
            [DisplayLanguage.Japanese] = "出典: {0}",
        },
        [Key.LoadoutProgressFormat] = new()
        {
            [DisplayLanguage.German] = "{0}/{1} bereits gelernt",
            [DisplayLanguage.English] = "{0}/{1} already learned",
            [DisplayLanguage.French] = "{0}/{1} déjà appris",
            [DisplayLanguage.Japanese] = "{0}/{1} 習得済み",
        },
        [Key.LoadoutOpenSourceButton] = new()
        {
            [DisplayLanguage.German] = "Quelle öffnen",
            [DisplayLanguage.English] = "Open source",
            [DisplayLanguage.French] = "Ouvrir la source",
            [DisplayLanguage.Japanese] = "出典を開く",
        },
        [Key.DisplayLanguageAutomatic] = new()
        {
            [DisplayLanguage.German] = "Automatisch (Client-Sprache)",
            [DisplayLanguage.English] = "Automatic (client language)",
            [DisplayLanguage.French] = "Automatique (langue du client)",
            [DisplayLanguage.Japanese] = "自動(クライアント言語)",
        },
        [Key.SourceMethodDuty] = new()
        {
            [DisplayLanguage.German] = "Duty",
            [DisplayLanguage.English] = "Duty",
            [DisplayLanguage.French] = "Mission",
            [DisplayLanguage.Japanese] = "コンテンツ",
        },
        [Key.SourceMethodOpenWorld] = new()
        {
            [DisplayLanguage.German] = "Open World",
            [DisplayLanguage.English] = "Open World",
            // TODO: Übersetzung prüfen
            [DisplayLanguage.French] = "Monde ouvert",
            [DisplayLanguage.Japanese] = "フィールド",
        },
        [Key.SourceMethodLevequest] = new()
        {
            [DisplayLanguage.German] = "Levequest",
            [DisplayLanguage.English] = "Levequest",
            [DisplayLanguage.French] = "Mandat",
            [DisplayLanguage.Japanese] = "ギルドリーヴ",
        },
        [Key.SourceMethodDungeonTrash] = new()
        {
            [DisplayLanguage.German] = "Dungeon-Trash",
            [DisplayLanguage.English] = "Dungeon trash",
            // TODO: Übersetzung prüfen
            [DisplayLanguage.French] = "Monstres de donjon",
            // TODO: Übersetzung prüfen
            [DisplayLanguage.Japanese] = "ダンジョンの雑魚",
        },
        [Key.SourceMethodMaskedCarnivale] = new()
        {
            [DisplayLanguage.German] = "Masked Carnivale",
            [DisplayLanguage.English] = "Masked Carnivale",
            [DisplayLanguage.French] = "La Grande Mascarade",
            [DisplayLanguage.Japanese] = "マスクカーニバル",
        },
        [Key.SourceMethodTotem] = new()
        {
            [DisplayLanguage.German] = "Totem",
            [DisplayLanguage.English] = "Totem",
            // TODO: Übersetzung prüfen
            [DisplayLanguage.French] = "Totem",
            // TODO: Übersetzung prüfen
            [DisplayLanguage.Japanese] = "トーテム",
        },
        [Key.SourceMethodTotemSpellCount] = new()
        {
            [DisplayLanguage.German] = "Totem (nach Spell-Anzahl)",
            [DisplayLanguage.English] = "Totem (by spell count)",
            // TODO: Übersetzung prüfen
            [DisplayLanguage.French] = "Totem (selon le nombre de sorts)",
            // TODO: Übersetzung prüfen
            [DisplayLanguage.Japanese] = "トーテム(習得数に応じて)",
        },
        [Key.SourceMethodTotemLevel] = new()
        {
            [DisplayLanguage.German] = "Totem (nach Level)",
            [DisplayLanguage.English] = "Totem (by level)",
            // TODO: Übersetzung prüfen
            [DisplayLanguage.French] = "Totem (selon le niveau)",
            // TODO: Übersetzung prüfen
            [DisplayLanguage.Japanese] = "トーテム(レベルに応じて)",
        },
        [Key.SourceMethodMaskedCarnivaleProgress] = new()
        {
            [DisplayLanguage.German] = "Masked Carnivale (Fortschritt)",
            [DisplayLanguage.English] = "Masked Carnivale (progress)",
            [DisplayLanguage.French] = "La Grande Mascarade (progression)",
            [DisplayLanguage.Japanese] = "マスクカーニバル(進行度)",
        },
        [Key.SourceMethodStartingSpell] = new()
        {
            [DisplayLanguage.German] = "Von Anfang an bekannt",
            [DisplayLanguage.English] = "Known from the start",
            [DisplayLanguage.French] = "Connu dès le départ",
            [DisplayLanguage.Japanese] = "初期習得",
        },
        [Key.GroupPublishRequiresOwnProfileHint] = new()
        {
            [DisplayLanguage.German] =
                "Gruppen können nur mit eigenem Live-Sync-Profil veröffentlicht werden. Aktiviere zuerst " +
                "Live-Sync (Einstellungen), damit dein eigenes Profil existiert.",
            [DisplayLanguage.English] =
                "Groups can only be published with your own Live Sync profile. Enable Live Sync first " +
                "(Settings) so that your own profile exists.",
            [DisplayLanguage.French] =
                "Les groupes ne peuvent être publiés qu'avec ton propre profil Live Sync. Active d'abord " +
                "Live Sync (Paramètres) pour que ton profil existe.",
            [DisplayLanguage.Japanese] =
                "グループを公開するには自分のLive Syncプロフィールが必要です。まず設定でLive Syncを有効にして、" +
                "自分のプロフィールを作成してください。",
        },
        [Key.GroupPublishCreatorAlwaysMemberHint] = new()
        {
            [DisplayLanguage.German] = "Als Ersteller bist du immer Mitglied der Gruppe.",
            [DisplayLanguage.English] = "As the creator, you are always a member of the group.",
            [DisplayLanguage.French] = "En tant que créateur, tu fais toujours partie du groupe.",
            [DisplayLanguage.Japanese] = "作成者であるあなたは常にグループのメンバーです。",
        },
        [Key.GroupPublishCreatorRejectedMessage] = new()
        {
            [DisplayLanguage.German] =
                "Gruppe nicht veröffentlicht: Dein eigenes Live-Sync-Profil wurde nicht gefunden oder passt " +
                "nicht. Aktiviere Live-Sync (bzw. veröffentliche dein Profil erneut) und versuche es noch einmal.",
            [DisplayLanguage.English] =
                "Group not published: your own Live Sync profile was not found or does not match. Enable " +
                "Live Sync (or publish your profile again) and try again.",
            [DisplayLanguage.French] =
                "Groupe non publié : ton propre profil Live Sync est introuvable ou ne correspond pas. Active " +
                "Live Sync (ou republie ton profil) puis réessaie.",
            [DisplayLanguage.Japanese] =
                "グループを公開できませんでした: 自分のLive Syncプロフィールが見つからないか一致しません。" +
                "Live Syncを有効にする(またはプロフィールを再公開する)と、もう一度お試しください。",
        },
        [Key.GroupPublishMissingMembersMessage] = new()
        {
            [DisplayLanguage.German] =
                "Gruppe nicht veröffentlicht - diese Mitglieder haben kein Live-Sync-Profil: {0}. Sie müssen " +
                "Live-Sync aktivieren oder ihren Eintrag auf der Website veröffentlichen.",
            [DisplayLanguage.English] =
                "Group not published - these members have no Live Sync profile: {0}. They need to enable " +
                "Live Sync or publish their entry on the website.",
            [DisplayLanguage.French] =
                "Groupe non publié - ces membres n'ont pas de profil Live Sync : {0}. Ils doivent activer " +
                "Live Sync ou publier leur entrée sur le site web.",
            [DisplayLanguage.Japanese] =
                "グループを公開できませんでした - 次のメンバーにLive Syncプロフィールがありません: {0}。" +
                "Live Syncを有効にするか、ウェブサイトで自分のエントリーを公開する必要があります。",
        },
        [Key.GroupFinderNamesNotVerifiedHint] = new()
        {
            [DisplayLanguage.German] = "Charakternamen werden nicht verifiziert. Missbrauch kannst du im Discord melden.",
            [DisplayLanguage.English] = "Character names are not verified. You can report abuse on Discord.",
            [DisplayLanguage.French] = "Les noms de personnage ne sont pas vérifiés. Tu peux signaler les abus sur Discord.",
            [DisplayLanguage.Japanese] = "キャラクター名は検証されていません。不正利用はDiscordで報告できます。",
        },
        [Key.OpenDiscordServerButton] = new()
        {
            [DisplayLanguage.German] = "Discord öffnen",
            [DisplayLanguage.English] = "Open Discord",
            [DisplayLanguage.French] = "Ouvrir Discord",
            [DisplayLanguage.Japanese] = "Discordを開く",
        },
        [Key.LiveSyncPushNameTaken] = new()
        {
            [DisplayLanguage.German] =
                "Für diesen Charakter existiert bereits ein Profil. Wenn das nicht deins ist, melde dich im Discord.",
            [DisplayLanguage.English] =
                "A profile already exists for this character. If it isn't yours, please let us know on Discord.",
            [DisplayLanguage.French] =
                "Un profil existe déjà pour ce personnage. S'il n'est pas à toi, signale-le sur Discord.",
            [DisplayLanguage.Japanese] =
                "このキャラクターのプロフィールは既に存在します。あなたのものでない場合は、Discordでお知らせください。",
        },
        [Key.LearningPlanSummaryFormat] = new()
        {
            [DisplayLanguage.German] = "{0} fehlende Spells - Nutzen gesamt: {1} (Spell x Spieler, die ihn brauchen)",
            [DisplayLanguage.English] = "{0} missing spells - total benefit: {1} (spell x players who need it)",
            [DisplayLanguage.French] = "{0} sorts manquants - bénéfice total : {1} (sort x joueurs qui en ont besoin)",
            [DisplayLanguage.Japanese] = "未習得スペル {0} 個 - 合計メリット: {1}(スペル × 必要なプレイヤー数)",
        },
        [Key.LearningPlanFilterAll] = new()
        {
            [DisplayLanguage.German] = "Alle",
            [DisplayLanguage.English] = "All",
            [DisplayLanguage.French] = "Tous",
            [DisplayLanguage.Japanese] = "すべて",
        },
        [Key.LearningPlanFilterDuties] = new()
        {
            [DisplayLanguage.German] = "Nur Duties",
            [DisplayLanguage.English] = "Duties only",
            [DisplayLanguage.French] = "Missions uniquement",
            [DisplayLanguage.Japanese] = "コンテンツのみ",
        },
        [Key.LearningPlanFilterOpenWorld] = new()
        {
            [DisplayLanguage.German] = "Nur Open World",
            [DisplayLanguage.English] = "Open world only",
            [DisplayLanguage.French] = "Monde ouvert uniquement",
            [DisplayLanguage.Japanese] = "フィールドのみ",
        },
        [Key.LearningPlanLocationsHeader] = new()
        {
            [DisplayLanguage.German] = "Nach Ort (sortiert nach Nutzen):",
            [DisplayLanguage.English] = "By location (sorted by benefit):",
            [DisplayLanguage.French] = "Par lieu (triés par bénéfice) :",
            [DisplayLanguage.Japanese] = "場所別(メリット順):",
        },
        [Key.LearningPlanLocationEntrySuffixFormat] = new()
        {
            // Hinter dem (klickbaren) Ortsnamen: {0} = Duty/Open World, {1} = Nutzen.
            [DisplayLanguage.German] = "({0}) - Nutzen {1}",
            [DisplayLanguage.English] = "({0}) - benefit {1}",
            [DisplayLanguage.French] = "({0}) - bénéfice {1}",
            [DisplayLanguage.Japanese] = "({0})- メリット {1}",
        },
        [Key.LearningPlanNoLocations] = new()
        {
            [DisplayLanguage.German] = "Keine Orte für diesen Filter.",
            [DisplayLanguage.English] = "No locations for this filter.",
            [DisplayLanguage.French] = "Aucun lieu pour ce filtre.",
            [DisplayLanguage.Japanese] = "このフィルターに該当する場所はありません。",
        },
        [Key.LearningPlanBeneficiariesLabel] = new()
        {
            [DisplayLanguage.German] = "Profitiert:",
            [DisplayLanguage.English] = "Benefits:",
            [DisplayLanguage.French] = "En profitent :",
            [DisplayLanguage.Japanese] = "恩恵を受けるプレイヤー:",
        },
        [Key.LearningPlanRequiredLevelHint] = new()
        {
            [DisplayLanguage.German] = "Erfordert Stufe {0} - dein Blaumagier ist Stufe {1}.",
            [DisplayLanguage.English] = "Requires level {0} - your Blue Mage is level {1}.",
            [DisplayLanguage.French] = "Niveau {0} requis - ton mage bleu est niveau {1}.",
            [DisplayLanguage.Japanese] = "必要レベル {0} - あなたの青魔道士はレベル {1} です。",
        },
        [Key.LearningPlanCarnivaleHeader] = new()
        {
            [DisplayLanguage.German] = "Masked Carnivale:",
            [DisplayLanguage.English] = "Masked Carnivale:",
            [DisplayLanguage.French] = "La Grande Mascarade :",
            [DisplayLanguage.Japanese] = "マスクカーニバル:",
        },
        [Key.LearningPlanCarnivaleProgressFormat] = new()
        {
            [DisplayLanguage.German] = "Belohnung für {0} abgeschlossene Stages",
            [DisplayLanguage.English] = "Reward for clearing {0} stages",
            [DisplayLanguage.French] = "Récompense pour {0} étapes terminées",
            [DisplayLanguage.Japanese] = "{0}ステージクリアの報酬",
        },
        [Key.LearningPlanTotemsHeader] = new()
        {
            [DisplayLanguage.German] = "Totems:",
            [DisplayLanguage.English] = "Totems:",
            [DisplayLanguage.French] = "Totems :",
            [DisplayLanguage.Japanese] = "トーテム:",
        },
        [Key.LearningPlanTotemSpellCountFormat] = new()
        {
            [DisplayLanguage.German] = "Bedingung: {0} gelernte Spells (du: {1})",
            [DisplayLanguage.English] = "Condition: {0} learned spells (you: {1})",
            [DisplayLanguage.French] = "Condition : {0} sorts appris (toi : {1})",
            [DisplayLanguage.Japanese] = "条件: 習得スペル {0} 個(あなた: {1})",
        },
        [Key.LearningPlanTotemLevelFormat] = new()
        {
            [DisplayLanguage.German] = "Bedingung: Blaumagier-Stufe {0} (du: {1})",
            [DisplayLanguage.English] = "Condition: Blue Mage level {0} (you: {1})",
            [DisplayLanguage.French] = "Condition : mage bleu niveau {0} (toi : {1})",
            [DisplayLanguage.Japanese] = "条件: 青魔道士レベル {0}(あなた: {1})",
        },
        [Key.LearningPlanTotemReady] = new()
        {
            // {0} = NPC-Name aus den Spieldaten (GameTextService.GetWaywardGaheelJaName)
            [DisplayLanguage.German] = "Bedingung erfüllt - bei {0} abholen.",
            [DisplayLanguage.English] = "Condition met - collect it from {0}.",
            [DisplayLanguage.French] = "Condition remplie - à récupérer auprès de {0}.",
            [DisplayLanguage.Japanese] = "条件達成 - {0}から受け取れます。",
        },
        [Key.DutyFinderOpenTooltip] = new()
        {
            [DisplayLanguage.German] = "Im Duty Finder öffnen",
            [DisplayLanguage.English] = "Open in Duty Finder",
            [DisplayLanguage.French] = "Ouvrir dans l'outil de mission",
            [DisplayLanguage.Japanese] = "コンテンツファインダーで開く",
        },
        [Key.DutyFinderOpenVariantTooltip] = new()
        {
            // {0} = Duty-Name aus den Spieldaten (GameTextService.GetDutyName)
            [DisplayLanguage.German] = "Im Duty Finder öffnen: {0} (nur Fenster, keine Anmeldung)",
            [DisplayLanguage.English] = "Open in Duty Finder: {0} (window only, does not register)",
            [DisplayLanguage.French] = "Ouvrir dans l'outil de mission : {0} (fenêtre uniquement, sans inscription)",
            [DisplayLanguage.Japanese] = "コンテンツファインダーで開く: {0}(ウィンドウのみ、申請はしません)",
        },
        [Key.DutyFinderNotUnlockedTooltip] = new()
        {
            [DisplayLanguage.German] = "Noch nicht freigeschaltet",
            [DisplayLanguage.English] = "Not unlocked yet",
            [DisplayLanguage.French] = "Pas encore débloqué",
            [DisplayLanguage.Japanese] = "未開放",
        },
        [Key.DutyFinderVariantNormal] = new()
        {
            [DisplayLanguage.German] = "Normal",
            [DisplayLanguage.English] = "Normal",
            [DisplayLanguage.French] = "Normal",
            [DisplayLanguage.Japanese] = "ノーマル",
        },
        [Key.DutyFinderVariantFallback] = new()
        {
            [DisplayLanguage.German] = "Variante {0}",
            [DisplayLanguage.English] = "Variant {0}",
            [DisplayLanguage.French] = "Variante {0}",
            [DisplayLanguage.Japanese] = "バリエーション {0}",
        },
        [Key.MapMarkTooltip] = new()
        {
            [DisplayLanguage.German] = "Auf Karte markieren",
            [DisplayLanguage.English] = "Mark on map",
            [DisplayLanguage.French] = "Marquer sur la carte",
            [DisplayLanguage.Japanese] = "マップにフラグを立てる",
        },
        [Key.MapLinkToChatTooltip] = new()
        {
            [DisplayLanguage.German] = "Link in Chat (nur in deinem Chatfenster, wird nicht gesendet)",
            [DisplayLanguage.English] = "Link in chat (only in your own chat window, nothing is sent)",
            [DisplayLanguage.French] = "Lien dans le chat (uniquement dans ta fenêtre de chat, rien n'est envoyé)",
            [DisplayLanguage.Japanese] = "チャットにリンク(自分のチャット欄のみ、送信はされません)",
        },
        [Key.TeleportToTooltip] = new()
        {
            // {0} = Aetheryt-Name aus den Spieldaten
            [DisplayLanguage.German] = "Teleport nach {0}",
            [DisplayLanguage.English] = "Teleport to {0}",
            [DisplayLanguage.French] = "Téléportation vers {0}",
            [DisplayLanguage.Japanese] = "{0}へテレポ",
        },
        [Key.AetheryteNotUnlockedTooltip] = new()
        {
            [DisplayLanguage.German] = "Aetheryt nicht freigeschaltet",
            [DisplayLanguage.English] = "Aetheryte not unlocked",
            [DisplayLanguage.French] = "Éthérite non débloquée",
            [DisplayLanguage.Japanese] = "エーテライト未交感",
        },
        [Key.TeleportFailedMessage] = new()
        {
            [DisplayLanguage.German] = "Teleport gerade nicht möglich.",
            [DisplayLanguage.English] = "Teleport not possible right now.",
            [DisplayLanguage.French] = "Téléportation impossible pour le moment.",
            [DisplayLanguage.Japanese] = "現在テレポできません。",
        },
        [Key.TeleportOnLocationClickToggle] = new()
        {
            [DisplayLanguage.German] = "Teleport per Klick auf Ortsnamen",
            [DisplayLanguage.English] = "Teleport by clicking a location name",
            [DisplayLanguage.French] = "Téléportation en cliquant sur un nom de lieu",
            [DisplayLanguage.Japanese] = "地名クリックでテレポ",
        },
        [Key.TeleportOnLocationClickHint] = new()
        {
            [DisplayLanguage.German] =
                "Ein Klick auf einen Open-World-Ort teleportiert einmal zum passenden Aetheryten (Gil-Kosten wie im " +
                "Teleport-Fenster) und setzt die Kartenflagge beim Monster. Aus: Ortsnamen sind normaler Text.",
            [DisplayLanguage.English] =
                "Clicking an open-world location teleports you once to the matching aetheryte (gil cost as in the " +
                "teleport window) and sets the map flag at the monster. Off: location names are plain text.",
            [DisplayLanguage.French] =
                "Un clic sur un lieu du monde ouvert te téléporte une fois vers l'éthérite correspondante (coût en gils " +
                "comme dans la fenêtre de téléportation) et place le drapeau sur la carte. Désactivé : noms en texte simple.",
            [DisplayLanguage.Japanese] =
                "フィールドの地名をクリックすると、対応するエーテライトへ1回テレポし(費用はテレポ画面と同じ)、" +
                "モンスターの位置にフラグを立てます。オフ: 地名は通常のテキストになります。",
        },
        [Key.OnboardingWindowTitle] = new()
        {
            [DisplayLanguage.German] = "BLUnion - Einrichtung",
            [DisplayLanguage.English] = "BLUnion - Setup",
            [DisplayLanguage.French] = "BLUnion - Configuration",
            [DisplayLanguage.Japanese] = "BLUnion - 初期設定",
        },
        [Key.OnboardingIntro] = new()
        {
            [DisplayLanguage.German] = "Wie soll BLUnion deinen Spell-Status mit anderen teilen?",
            [DisplayLanguage.English] = "How should BLUnion share your spell status with others?",
            [DisplayLanguage.French] = "Comment BLUnion doit-il partager ton statut de sorts avec les autres ?",
            [DisplayLanguage.Japanese] = "BLUnionで習得スペルの状況をどのように共有しますか?",
        },
        [Key.OnboardingLiveSyncDataText] = new()
        {
            [DisplayLanguage.German] = "Live-Sync speichert auf dem BLUnion-Server: Charaktername, World und gelernte Spells. Öffentlich sichtbar ist das nur, wenn du deinen Eintrag selbst im Group Finder listest.",
            [DisplayLanguage.English] = "Live Sync stores on the BLUnion server: character name, world, and learned spells. It is only publicly visible if you list your entry in the Group Finder yourself.",
            [DisplayLanguage.French] = "Live-Sync enregistre sur le serveur BLUnion : nom du personnage, monde et sorts appris. Ce n'est visible publiquement que si tu inscris toi-même ton entrée dans le Group Finder.",
            [DisplayLanguage.Japanese] = "ライブ同期ではBLUnionサーバーにキャラクター名、ワールド、習得済みスペルを保存します。グループファインダーに自分で掲載した場合のみ公開されます。",
        },
        [Key.OnboardingManualText] = new()
        {
            [DisplayLanguage.German] = "Nur manuell: Nichts verlässt deinen PC, du tauschst Codes selbst aus (Chat, Discord, ...).",
            [DisplayLanguage.English] = "Manual only: nothing leaves your PC, you exchange codes yourself (chat, Discord, ...).",
            [DisplayLanguage.French] = "Manuel uniquement : rien ne quitte ton PC, tu échanges les codes toi-même (chat, Discord, ...).",
            [DisplayLanguage.Japanese] = "手動のみ: データはPCの外に出ません。コードは自分で交換します(チャット、Discordなど)。",
        },
        [Key.OnboardingEnableLiveSyncButton] = new()
        {
            [DisplayLanguage.German] = "Live-Sync aktivieren (empfohlen)",
            [DisplayLanguage.English] = "Enable Live Sync (recommended)",
            [DisplayLanguage.French] = "Activer Live-Sync (recommandé)",
            [DisplayLanguage.Japanese] = "ライブ同期を有効にする(推奨)",
        },
        [Key.OnboardingManualButton] = new()
        {
            [DisplayLanguage.German] = "Nur manuell (Codes)",
            [DisplayLanguage.English] = "Manual only (codes)",
            [DisplayLanguage.French] = "Manuel uniquement (codes)",
            [DisplayLanguage.Japanese] = "手動のみ(コード)",
        },
        [Key.OnboardingChangeLaterHint] = new()
        {
            [DisplayLanguage.German] = "Alles lässt sich jederzeit in den Settings ändern.",
            [DisplayLanguage.English] = "You can change all of this at any time in the settings.",
            [DisplayLanguage.French] = "Tout peut être modifié à tout moment dans les paramètres.",
            [DisplayLanguage.Japanese] = "すべて設定からいつでも変更できます。",
        },
        [Key.SharePresenceToggle] = new()
        {
            [DisplayLanguage.German] = "Online-Status teilen",
            [DisplayLanguage.English] = "Share online status",
            [DisplayLanguage.French] = "Partager le statut en ligne",
            [DisplayLanguage.Japanese] = "オンライン状況を共有",
        },
        [Key.SharePresenceHint] = new()
        {
            [DisplayLanguage.German] = "Andere sehen im Group Finder, ob du gerade online bist bzw. grob wann zuletzt - nur solange dein Eintrag gelistet ist.",
            [DisplayLanguage.English] = "Others see in the Group Finder whether you are online right now or roughly when you last were - only while your entry is listed.",
            [DisplayLanguage.French] = "Les autres voient dans le Group Finder si tu es en ligne ou, approximativement, quand tu l'étais pour la dernière fois - uniquement tant que ton entrée est inscrite.",
            [DisplayLanguage.Japanese] = "グループファインダーで、あなたが現在オンラインか、または最後にオンラインだったおおよその時間が表示されます(掲載中のみ)。",
        },
        [Key.SharePresenceRequiresLiveSyncHint] = new()
        {
            [DisplayLanguage.German] = "Nur mit Live-Sync verfügbar.",
            [DisplayLanguage.English] = "Only available with Live Sync.",
            [DisplayLanguage.French] = "Disponible uniquement avec Live-Sync.",
            [DisplayLanguage.Japanese] = "ライブ同期が有効な場合のみ利用できます。",
        },
        [Key.PresenceOnline] = new()
        {
            [DisplayLanguage.German] = "● online",
            [DisplayLanguage.English] = "● online",
            [DisplayLanguage.French] = "● en ligne",
            [DisplayLanguage.Japanese] = "● オンライン",
        },
        [Key.PresenceLastSeenMinutesFormat] = new()
        {
            [DisplayLanguage.German] = "zuletzt online vor {0} Min.",
            [DisplayLanguage.English] = "last online {0} min ago",
            [DisplayLanguage.French] = "en ligne il y a {0} min",
            [DisplayLanguage.Japanese] = "最終オンライン: {0}分前",
        },
        [Key.PresenceLastSeenHoursFormat] = new()
        {
            [DisplayLanguage.German] = "zuletzt online vor {0} Std.",
            [DisplayLanguage.English] = "last online {0} h ago",
            [DisplayLanguage.French] = "en ligne il y a {0} h",
            [DisplayLanguage.Japanese] = "最終オンライン: {0}時間前",
        },
        [Key.PresenceLastSeenDaysFormat] = new()
        {
            [DisplayLanguage.German] = "zuletzt online vor {0} Tag(en)",
            [DisplayLanguage.English] = "last online {0} day(s) ago",
            [DisplayLanguage.French] = "en ligne il y a {0} jour(s)",
            [DisplayLanguage.Japanese] = "最終オンライン: {0}日前",
        },
        [Key.CopyTellButton] = new()
        {
            [DisplayLanguage.German] = "/tell kopieren",
            [DisplayLanguage.English] = "Copy /tell",
            [DisplayLanguage.French] = "Copier /tell",
            [DisplayLanguage.Japanese] = "/tellをコピー",
        },
        [Key.TellCopiedMessage] = new()
        {
            [DisplayLanguage.German] = "\"{0}\" in die Zwischenablage kopiert - im Chat einfügen.",
            [DisplayLanguage.English] = "Copied \"{0}\" to the clipboard - paste it into the chat.",
            [DisplayLanguage.French] = "\"{0}\" copié dans le presse-papiers - colle-le dans le chat.",
            [DisplayLanguage.Japanese] = "「{0}」をクリップボードにコピーしました。チャットに貼り付けてください。",
        },
    };

    static UiStrings()
    {
        foreach (var key in Enum.GetValues<Key>())
        {
            if (!Strings.TryGetValue(key, out var translations))
                throw new InvalidOperationException($"UiStrings: Kein Eintrag für Key \"{key}\".");

            foreach (var language in Enum.GetValues<DisplayLanguage>())
            {
                if (!translations.ContainsKey(language))
                    throw new InvalidOperationException($"UiStrings: Kein Eintrag für Key \"{key}\" in Sprache \"{language}\".");
            }
        }
    }

    public static string Get(Key key, DisplayLanguage language) => Strings[key][language];

    public static string Format(Key key, DisplayLanguage language, params object[] args) =>
        string.Format(Get(key, language), args);
}
