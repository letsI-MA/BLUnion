using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;

namespace BLUnion.UI;

public sealed partial class MainWindow
{
    // Muss mit NOTE_MAX_LENGTH im Worker übereinstimmen (siehe worker/src/index.ts) - der Worker
    // kappt "note" serverseitig ohnehin auf diese Länge, das InputText-Limit hier spiegelt das nur
    // clientseitig (siehe DrawGroupPublishSection-Zeichenzähler unten).
    private const int GroupNoteMaxLength = 60;

    // Feste ScrollY-Höhe der Ziel-Spell-Auswahl-/Filterlisten (siehe DrawSpellCheckboxFilterList) -
    // kleiner als ScrollingTableHeight (MainWindow.cs), weil diese Liste innerhalb einer bereits
    // aufgeklappten Sektion mit weiterem Inhalt darüber/darunter sitzt. Gemeinsam genutzt von
    // Gruppen-/Solo-Zielspell-Auswahl beim Veröffentlichen UND vom Browse-Zielspell-Filter.
    private const float TargetSpellListHeight = 180f;

    // ImGui-Popup-ID für DrawGroupTargetSpellDetailPopup - als Konstante statt an beiden
    // Aufrufstellen (ImGui.OpenPopup/ImGui.BeginPopup) den String zu wiederholen.
    private const string GroupTargetSpellDetailPopupId = "GroupTargetSpellDetailPopup";

    private void DrawGroupFinderTab()
    {
        this.DrawLastMessage();

        if (!this.configuration.LiveSyncEnabled)
        {
            this.DrawHintText(UiStrings.Get(UiStrings.Key.GroupFinderInactiveHint, this.displayLanguage));

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
                // Vorher zwei CollapsingHeader übereinander (Solo-Sichtbarkeit + Gruppen-
                // Veröffentlichung) - fachlich zwei getrennte Anwendungsfälle, die sich nur durch
                // Auf-/Zuklappen unterschieden. Jetzt eine klare Moduswahl: es wird immer nur GENAU
                // einer der beiden Abschnitte gezeichnet.
                if (ImGui.RadioButton(
                        UiStrings.Get(UiStrings.Key.GroupPublishModeSolo, this.displayLanguage),
                        this.groupPublishMode == GroupPublishMode.Solo))
                    this.groupPublishMode = GroupPublishMode.Solo;

                ImGui.SameLine();

                if (ImGui.RadioButton(
                        UiStrings.Get(UiStrings.Key.GroupPublishModeGroup, this.displayLanguage),
                        this.groupPublishMode == GroupPublishMode.Group))
                    this.groupPublishMode = GroupPublishMode.Group;

                ImGui.Separator();

                if (this.groupPublishMode == GroupPublishMode.Solo)
                {
                    this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.GroupFinderMyEntryHeader, this.displayLanguage));
                    ImGui.Separator();
                    this.DrawMyEntrySection();
                }
                else
                {
                    this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.GroupPublishHeader, this.displayLanguage));
                    ImGui.Separator();
                    this.DrawGroupPublishSection();
                }

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

        ImGui.SetNextItemWidth(40f);
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

        ImGui.Separator();
        DrawSectionGap();

        this.DrawMyEntryTargetSpellSection();

        ImGui.Separator();
        DrawSectionGap();

        // HasEditTokenForLocalCharacter() statt LastKnownOwnProfile != null: derselbe Zustand, den
        // auch DeleteOwnProfileAsync für "existiert bereits ein Eintrag" nutzt - ein Klick auf den
        // Button ist dann inhaltlich ein Update (editToken vorhanden), nicht ein Neuanlegen.
        var soloPublishLabel = this.liveSyncService.HasEditTokenForLocalCharacter()
            ? UiStrings.Get(UiStrings.Key.GroupFinderUpdateButton, this.displayLanguage)
            : UiStrings.Get(UiStrings.Key.GroupFinderPublishButton, this.displayLanguage);

        if (ImGui.Button(soloPublishLabel))
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

            if (confirmedProfile.DiscordChannelUrl is { } soloDiscordUrl)
                this.DrawDiscordChannelHint(soloDiscordUrl, confirmedProfile.DiscordChannelName, "SoloDiscordLink");
        }
    }

    // Gemeinsam für Solo-Profil (DrawMyEntrySection) und Gruppe (DrawGroupPublishSection) - idSuffix
    // macht den ImGui-Button in beiden Formularen eindeutig (siehe DrawSpellCheckboxFilterList-Doc
    // für dasselbe Muster). channelName ist optional (siehe Env.DISCORD_CHANNEL_NAME_NA-Doc im
    // Worker) - ohne ihn zeigt der Hinweis nur, DASS ein Discord-Kanal verfügbar ist, nicht welcher.
    private void DrawDiscordChannelHint(string discordUrl, string? channelName, string idSuffix)
    {
        var hintText = channelName is { } name
            ? UiStrings.Format(UiStrings.Key.GroupFinderDiscordChannelHintFormat, this.displayLanguage, name)
            : UiStrings.Get(UiStrings.Key.GroupFinderDiscordChannelHintGeneric, this.displayLanguage);

        ImGui.TextColored(SuccessMessageColor, hintText);
        ImGui.SameLine();
        if (ImGui.Button($"{UiStrings.Get(UiStrings.Key.OpenDiscordChannelButton, this.displayLanguage)}##{idSuffix}"))
            this.OpenUrlInBrowser(discordUrl);
    }

    private void DrawOtherPlayersSection()
    {
        var dataCenter = this.liveSyncService.LastKnownOwnProfile?.DataCenter;
        if (dataCenter is null)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.GroupFinderDeterminingDataCenter, this.displayLanguage));
            return;
        }

        this.DrawSectionHeader(UiStrings.Format(UiStrings.Key.GroupFinderOthersHeader, this.displayLanguage, dataCenter));
        ImGui.SameLine();

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.GroupFinderRefreshButton, this.displayLanguage)))
        {
            this.liveSyncService.TriggerBrowse();
            this.liveSyncService.TriggerGroupBrowse();

            this.lastGroupFinderAutoRefreshAt = DateTimeOffset.UtcNow;
        }

        ImGui.Separator();

        this.DrawBrowseTargetSpellFilterSection("BrowseFilterPlayers");

        var localPlayerName = this.partyService.GetLocalPlayerName();
        var allResults = this.liveSyncService.LastBrowseResults;

        // Ohne Filterauswahl bleibt die bestehende Sortierung (eigener Eintrag zuerst) unverändert.
        // Mit Filterauswahl wird zusätzlich primär nach ANZAHL der Übereinstimmungen mit
        // browseFilterSpellIds absteigend sortiert (mehr Treffer zuerst), die bisherige Sortierung
        // dient dabei nur noch als sekundäres Kriterium bei Gleichstand (siehe Aufgabenstellung).
        IReadOnlyList<GroupFinderEntry> entries = this.browseFilterSpellIds.Count > 0
            ? allResults
                .Select(entry => (Entry: entry, MatchCount: entry.TargetSpellIds.Count(this.browseFilterSpellIds.Contains)))
                .Where(match => match.MatchCount > 0)
                .OrderByDescending(match => match.MatchCount)
                .ThenByDescending(match => string.Equals(match.Entry.CharacterName, localPlayerName, StringComparison.Ordinal))
                .Select(match => match.Entry)
                .ToList()
            : allResults
                .OrderByDescending(entry => string.Equals(entry.CharacterName, localPlayerName, StringComparison.Ordinal))
                .ToList();

        if (entries.Count == 0)
        {
            var emptyStateKey = allResults.Count > 0 && this.browseFilterSpellIds.Count > 0
                ? UiStrings.Key.BrowseTargetSpellFilterNoMatches
                : UiStrings.Key.GroupFinderNoEntries;
            this.DrawEmptyState(UiStrings.Get(emptyStateKey, this.displayLanguage));
            return;
        }

        var totalSpellCount = this.spellDataService.Spells.Count;

        // Bewusst NICHT über DrawCard strukturiert (geprüft): DrawCard ist auf variable Höhe im
        // Vollbreite-Fluss ausgelegt (hängt am Ende DrawSectionGap an) und würde in dieser als
        // Grid-Zelle angelegten Karte (siehe DrawCardGrid) zu Overflow/Scrollbalken führen;
        // außerdem läuft DrawCards Titel immer über DrawSectionHeader mit fester Akzentfarbe, was
        // mit dem hier eigenen Entry-Highlighting (isOwnEntry -> komplett grün) kollidieren würde.
        //
        // Höhe wie bei den Gruppen-Karten NICHT mehr fest, sondern über den heightSelector-Overload
        // von DrawCardGrid individuell je Eintrag berechnet (siehe ComputePlayerBrowseCardHeight) -
        // eine feste Höhe müsste sonst für den ungünstigsten Fall (Notiz gesetzt, fremder statt
        // eigener Eintrag mit Button) dimensioniert sein, obwohl die meisten Einträge weniger
        // Inhalt haben.
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
        }, entry => this.ComputePlayerBrowseCardHeight(entry, localPlayerName));
    }

    // Individuelle Kartenhöhe je Spieler-Eintrag für den heightSelector-Overload von DrawCardGrid
    // (siehe MainWindow.Shared.cs) - gleiches Vorgehen wie ComputeGroupBrowseCardHeight weiter
    // unten: grobe Zeilen-Heuristik statt pixelgenauer Textvermessung, mit
    // ImGui.GetTextLineHeightWithSpacing()/ImGui.GetFrameHeightWithSpacing() statt hart codierter
    // Pixelwerte, damit es bei anderen Schriftgrößen/UI-Skalierungen konsistent mitskaliert.
    //
    // Basis (immer gezeichnet): Namenskopf + Fortschritts-Zeile (GroupFinderProgressFormat) +
    // Tags-Zeile + Gesucht-Zeile = 4 Text-Zeilen. Optional oben drauf: +1 Text-Zeile, wenn die
    // Notiz nicht leer ist. Der "Zur Vergleich hinzufügen"-Button ist die einzige Action hier
    // (siehe DrawOtherPlayersSection) und wird NUR für fremde Einträge gezeichnet, nie für den
    // eigenen (isOwnEntry) - entsprechend wird auch nur dort eine Button-Zeile reserviert.
    private float ComputePlayerBrowseCardHeight(GroupFinderEntry entry, string? localPlayerName)
    {
        var baseTextLines = 4;
        if (!string.IsNullOrEmpty(entry.Note))
            baseTextLines++;

        var isOwnEntry = string.Equals(entry.CharacterName, localPlayerName, StringComparison.Ordinal);
        var buttonRowCount = isOwnEntry ? 0 : 1;

        var textHeight = baseTextLines * ImGui.GetTextLineHeightWithSpacing();
        var buttonsHeight = buttonRowCount * ImGui.GetFrameHeightWithSpacing();
        var childPadding = ImGui.GetStyle().WindowPadding.Y * 2;

        return textHeight + buttonsHeight + childPadding;
    }

    private void DrawGroupPublishSection()
    {
        // Reine visuelle Gliederung (siehe Aufgabenstellung) - die nackten RadioButtons zur
        // Mitgliederquelle erklären sich sonst ohne Kontext nicht von selbst, anders als die
        // Sichtbarkeit/Tags/Notiz/Anzahl-Felder darunter (die schon über ihre eigenen Checkbox-/
        // Feld-Labels selbsterklärend sind) und der Zielspell-Bereich (hat bereits einen eigenen
        // DrawSectionHeader, siehe DrawGroupPublishTargetSpellSection) - daher bewusst kein neuer
        // UiStrings-Key hier, sondern Wiederverwendung von ColumnPlayer ("Spieler").
        this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.ColumnPlayer, this.displayLanguage));

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
        DrawSectionGap();

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
        ImGui.InputText(UiStrings.Get(UiStrings.Key.GroupPublishNoteLabel, this.displayLanguage), ref this.groupPublishNoteBuffer, GroupNoteMaxLength);
        this.DrawHintText(UiStrings.Format(
            UiStrings.Key.CharCountFormat, this.displayLanguage, this.groupPublishNoteBuffer.Length, GroupNoteMaxLength));

        ImGui.SetNextItemWidth(40f);
        ImGui.InputText(
            UiStrings.Get(UiStrings.Key.GroupPublishWantedPlayerCountLabel, this.displayLanguage),
            ref this.groupPublishWantedPlayerCountBuffer, 2, ImGuiInputTextFlags.CharsDecimal);

        ImGui.Separator();
        DrawSectionGap();

        this.DrawGroupPublishTargetSpellSection();

        ImGui.Separator();
        DrawSectionGap();

        var selectedCount = this.groupPublishSelectedMembers.Count;
        var canPublish = selectedCount is >= 1 and <= 8;

        // Siehe HasEditTokenForLocalCharacter()-Doc oben in DrawMyEntrySection - dasselbe Prinzip,
        // hier über den Gruppen-Zustand, den auch DeletePublishedGroup nutzt.
        var groupPublishLabel = this.liveSyncService.HasPublishedGroup()
            ? UiStrings.Get(UiStrings.Key.GroupUpdateButton, this.displayLanguage)
            : UiStrings.Get(UiStrings.Key.GroupPublishButton, this.displayLanguage);

        ImGui.BeginDisabled(!canPublish);
        if (ImGui.Button(groupPublishLabel))
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
                members, this.groupPublishVisible, this.groupPublishTags, this.groupPublishNoteBuffer, wantedPlayerCount,
                this.groupPublishTargetSpellIds);
        }

        ImGui.EndDisabled();

        if (this.liveSyncService.HasPublishedGroup())
        {
            ImGui.SameLine();
            if (ImGui.Button(UiStrings.Get(UiStrings.Key.GroupUnpublishButton, this.displayLanguage)))
                this.liveSyncService.DeletePublishedGroup();

            if (this.liveSyncService.LastKnownPublishedGroupDiscordChannelUrl is { } groupDiscordUrl)
            {
                this.DrawDiscordChannelHint(
                    groupDiscordUrl, this.liveSyncService.LastKnownPublishedGroupDiscordChannelName, "GroupDiscordLink");
            }
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

    // Spell-Auswahl für targetSpellIds (siehe GroupFinderGroupEntry.TargetSpellIds) - dieselbe
    // Filterlogik wie DrawSpellbookTab/DrawComparisonTab (SpellFilter.Matches + "Totems
    // ausblenden"), zusätzlich ein Umschalter zwischen "nur eigene fehlende Spells" (Standard -
    // das ist der typische Anwendungsfall: die Gruppe will gemeinsam die Spells farmen, die dem
    // Veröffentlichenden selbst noch fehlen) und "alle Spells" (z.B. um für andere Mitglieder
    // mitzuplanen, auch wenn der Veröffentlichende selbst die Spells schon kennt).
    private void DrawGroupPublishTargetSpellSection()
    {
        this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.GroupPublishTargetSpellHeader, this.displayLanguage));

        if (ImGui.RadioButton(
                UiStrings.Get(UiStrings.Key.GroupPublishTargetSpellScopeOnlyMissing, this.displayLanguage),
                this.groupPublishTargetSpellScope == GroupPublishTargetSpellScope.OnlyMissing))
            this.groupPublishTargetSpellScope = GroupPublishTargetSpellScope.OnlyMissing;

        ImGui.SameLine();

        if (ImGui.RadioButton(
                UiStrings.Get(UiStrings.Key.GroupPublishTargetSpellScopeAll, this.displayLanguage),
                this.groupPublishTargetSpellScope == GroupPublishTargetSpellScope.All))
            this.groupPublishTargetSpellScope = GroupPublishTargetSpellScope.All;

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint(
            "##GroupPublishTargetSpellFilter",
            UiStrings.Get(UiStrings.Key.SpellFilterHint, this.displayLanguage),
            ref this.groupPublishTargetSpellFilterText, 128);
        ImGui.Checkbox(
            UiStrings.Get(UiStrings.Key.HideTotemsToggle, this.displayLanguage), ref this.groupPublishTargetSpellHideTotems);

        var learnedSpellIds = this.localSpellUnlockService.GetLearnedSpellIds();

        var rows = this.spellDataService.Spells.Values
            .OrderBy(s => s.SpellbookOrder)
            .Where(s => this.groupPublishTargetSpellScope != GroupPublishTargetSpellScope.OnlyMissing || !learnedSpellIds.Contains(s.Id))
            .Where(s => !this.groupPublishTargetSpellHideTotems || !this.spellDataService.IsOnlyLearnableViaTotem(s.Id))
            .Where(s => SpellFilter.Matches(this.GetSpellName(s), s.SpellbookOrder, this.groupPublishTargetSpellFilterText))
            .ToList();

        ImGui.TextUnformatted(UiStrings.Format(
            UiStrings.Key.GroupPublishTargetSpellCountFormat, this.displayLanguage, this.groupPublishTargetSpellIds.Count));

        if (rows.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.SpellbookNoResults, this.displayLanguage));
            return;
        }

        ImGui.BeginChild("GroupPublishTargetSpellList", new System.Numerics.Vector2(0, TargetSpellListHeight), true);
        foreach (var spell in rows)
        {
            var selected = this.groupPublishTargetSpellIds.Contains(spell.Id);

            if (ImGui.Checkbox($"##GroupPublishTargetSpell{spell.Id}", ref selected))
            {
                if (selected)
                    this.groupPublishTargetSpellIds.Add(spell.Id);
                else
                    this.groupPublishTargetSpellIds.Remove(spell.Id);
            }

            ImGui.SameLine();
            this.DrawSpellIcon(spell.IconId);
            ImGui.SameLine();
            ImGui.TextUnformatted($"#{spell.SpellbookOrder:D3}  {this.GetSpellName(spell)}");
        }

        ImGui.EndChild();
    }

    // Ziel-Spell-Auswahl beim Veröffentlichen des eigenen Solo-Profils (siehe StoredProfile.
    // targetSpellIds im Worker/GroupFinderEntry.TargetSpellIds) - fachlich identisch zu
    // DrawGroupPublishTargetSpellSection oben (gleicher Scope-Umschalter "nur eigene fehlende
    // Spells" vs. "alle Spells", siehe dortige Doc), nutzt aber die eigenen groupFinderTargetSpell*-
    // Felder (siehe deren Doc in MainWindow.cs) statt der Gruppen-Publish-Felder, und ruft bei jeder
    // Auswahländerung sofort SetGroupFinderTargetSpellIds auf - analog zum bestehenden
    // Speicher-Zeitpunkt von Tags/Notiz/Anzahl weiter oben in DrawMyEntrySection (die Auswahl wird
    // also nicht erst beim "Veröffentlichen"-Klick übernommen, sondern läuft wie die übrigen
    // Solo-Felder direkt in liveSyncService mit).
    private void DrawMyEntryTargetSpellSection()
    {
        this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.GroupPublishTargetSpellHeader, this.displayLanguage));

        if (ImGui.RadioButton(
                $"{UiStrings.Get(UiStrings.Key.GroupPublishTargetSpellScopeOnlyMissing, this.displayLanguage)}##GroupFinderTargetSpellScopeOnlyMissing",
                this.groupFinderTargetSpellScope == GroupPublishTargetSpellScope.OnlyMissing))
            this.groupFinderTargetSpellScope = GroupPublishTargetSpellScope.OnlyMissing;

        ImGui.SameLine();

        if (ImGui.RadioButton(
                $"{UiStrings.Get(UiStrings.Key.GroupPublishTargetSpellScopeAll, this.displayLanguage)}##GroupFinderTargetSpellScopeAll",
                this.groupFinderTargetSpellScope == GroupPublishTargetSpellScope.All))
            this.groupFinderTargetSpellScope = GroupPublishTargetSpellScope.All;

        var learnedSpellIds = this.localSpellUnlockService.GetLearnedSpellIds();
        var candidateSpells = this.spellDataService.Spells.Values
            .Where(s => this.groupFinderTargetSpellScope != GroupPublishTargetSpellScope.OnlyMissing || !learnedSpellIds.Contains(s.Id));

        ImGui.TextUnformatted(UiStrings.Format(
            UiStrings.Key.GroupPublishTargetSpellCountFormat, this.displayLanguage, this.groupFinderTargetSpellIds.Count));

        if (this.DrawSpellCheckboxFilterList(
                "GroupFinderTargetSpell",
                ref this.groupFinderTargetSpellFilterText,
                ref this.groupFinderTargetSpellHideTotems,
                this.groupFinderTargetSpellIds,
                candidateSpells))
            this.liveSyncService.SetGroupFinderTargetSpellIds(this.groupFinderTargetSpellIds);
    }

    // Zeichnet Suchfeld + "Totems ausblenden"-Toggle + scrollbare Mehrfachauswahl-Checkbox-Liste für
    // eine Menge auswählbarer Spells (candidateSpells - bereits nach Scope/Sichtbarkeit gefiltert
    // vom Aufrufer, siehe z.B. DrawMyEntryTargetSpellSection) - gemeinsamer Kern von
    // DrawMyEntryTargetSpellSection (Solo-Zielspells) und DrawBrowseTargetSpellFilterSection
    // (Browse-Filter). idSuffix macht die ImGui-Widget-IDs je Aufrufer eindeutig; filterText/
    // hideTotems/selectedSpellIds bleiben dagegen bewusst eigene Instanzen JE AUFRUFER (siehe
    // jeweilige Feld-Docs in MainWindow.cs), damit ein Wechsel in einer Sektion die anderen nicht
    // beeinflusst. DrawGroupPublishTargetSpellSection oben nutzt diese Methode bewusst NICHT
    // (bleibt unverändert) - nur die beiden neu hinzugekommenen Sektionen tun das.
    //
    // Rückgabewert: ob sich selectedSpellIds in diesem Frame geändert hat - für Aufrufer, die bei
    // jeder Änderung sofort einen Folgeeffekt auslösen müssen (siehe SetGroupFinderTargetSpellIds
    // oben); Aufrufer, die die Auswahl erst bei einem separaten "Veröffentlichen"-Klick auslesen,
    // können den Rückgabewert ignorieren.
    private bool DrawSpellCheckboxFilterList(
        string idSuffix,
        ref string filterText,
        ref bool hideTotems,
        HashSet<uint> selectedSpellIds,
        IEnumerable<Spell> candidateSpells)
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint(
            $"##{idSuffix}Filter",
            UiStrings.Get(UiStrings.Key.SpellFilterHint, this.displayLanguage),
            ref filterText, 128);
        ImGui.Checkbox(
            $"{UiStrings.Get(UiStrings.Key.HideTotemsToggle, this.displayLanguage)}##{idSuffix}HideTotems", ref hideTotems);

        // ref-Parameter dürfen nicht direkt in Lambdas erfasst werden (CS1628) - lokale Kopien.
        var hideTotemsValue = hideTotems;
        var filterTextValue = filterText;

        var rows = candidateSpells
            .OrderBy(s => s.SpellbookOrder)
            .Where(s => !hideTotemsValue || !this.spellDataService.IsOnlyLearnableViaTotem(s.Id))
            .Where(s => SpellFilter.Matches(this.GetSpellName(s), s.SpellbookOrder, filterTextValue))
            .ToList();

        if (rows.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.SpellbookNoResults, this.displayLanguage));
            return false;
        }

        var changed = false;

        ImGui.BeginChild($"{idSuffix}List", new System.Numerics.Vector2(0, TargetSpellListHeight), true);
        foreach (var spell in rows)
        {
            var selected = selectedSpellIds.Contains(spell.Id);

            if (ImGui.Checkbox($"##{idSuffix}Spell{spell.Id}", ref selected))
            {
                if (selected)
                    selectedSpellIds.Add(spell.Id);
                else
                    selectedSpellIds.Remove(spell.Id);

                changed = true;
            }

            ImGui.SameLine();
            this.DrawSpellIcon(spell.IconId);
            ImGui.SameLine();
            ImGui.TextUnformatted($"#{spell.SpellbookOrder:D3}  {this.GetSpellName(spell)}");
        }

        ImGui.EndChild();

        return changed;
    }

    // Gemeinsamer Ziel-Spell-Filter für BEIDE Browse-Listen (Spieler UND Gruppen, siehe
    // DrawOtherPlayersSection/DrawGroupBrowseSection) - EIN geteilter Filter-/Auswahl-Zustand
    // (browseFilterSpellIds/-FilterText/-HideTotems, siehe deren Doc in MainWindow.cs), weil ein
    // Nutzer, der nach bestimmten Ziel-Spells sucht, typischerweise in BEIDEN Listen danach sucht.
    // idSuffix unterscheidet NUR die beiden CollapsingHeader-Instanzen selbst (je eine pro Liste,
    // unabhängig auf-/zuklappbar) - der eigentliche Filterzustand bleibt trotzdem identisch, weil
    // beide Aufrufe dieselben Felder lesen/schreiben; eine Auswahländerung in der einen Liste zeigt
    // sich dadurch sofort auch in der anderen.
    private void DrawBrowseTargetSpellFilterSection(string idSuffix)
    {
        if (!ImGui.CollapsingHeader($"{UiStrings.Get(UiStrings.Key.BrowseTargetSpellFilterHeader, this.displayLanguage)}##{idSuffix}"))
            return;

        ImGui.TextUnformatted(UiStrings.Format(
            UiStrings.Key.GroupPublishTargetSpellCountFormat, this.displayLanguage, this.browseFilterSpellIds.Count));

        this.DrawSpellCheckboxFilterList(
            idSuffix,
            ref this.browseFilterSpellFilterText,
            ref this.browseFilterSpellHideTotems,
            this.browseFilterSpellIds,
            this.spellDataService.Spells.Values);

        DrawSectionGap();
    }

    private void DrawGroupBrowseSection()
    {
        this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.GroupFinderGroupsHeader, this.displayLanguage));
        ImGui.Separator();

        this.DrawBrowseTargetSpellFilterSection("BrowseFilterGroups");

        var allGroups = this.liveSyncService.LastGroupBrowseResults;

        // Gleiches Filter-/Sortierprinzip wie DrawOtherPlayersSection (siehe dortiger Kommentar) -
        // OrderByDescending ist stabil, ohne Filterauswahl bleibt die bisherige (unveränderte)
        // Reihenfolge von LastGroupBrowseResults also erhalten, mit Filterauswahl dient sie nur noch
        // als sekundäres Kriterium bei gleicher Trefferanzahl.
        IReadOnlyList<GroupFinderGroupEntry> groups = this.browseFilterSpellIds.Count > 0
            ? allGroups
                .Select(group => (Group: group, MatchCount: group.TargetSpellIds.Count(this.browseFilterSpellIds.Contains)))
                .Where(match => match.MatchCount > 0)
                .OrderByDescending(match => match.MatchCount)
                .Select(match => match.Group)
                .ToList()
            : allGroups;

        if (groups.Count == 0)
        {
            var emptyStateKey = allGroups.Count > 0 && this.browseFilterSpellIds.Count > 0
                ? UiStrings.Key.BrowseTargetSpellFilterNoMatches
                : UiStrings.Key.GroupFinderNoGroups;
            this.DrawEmptyState(UiStrings.Get(emptyStateKey, this.displayLanguage));
            return;
        }

        var allSpellIds = this.spellDataService.Spells.Keys;

        // Nicht über DrawCard strukturiert - siehe Begründung in DrawOtherPlayersSection
        // (dieselbe Grid-Zellen-Logik via DrawCardGrid, zusätzlich pinnt AlignCursorToCardBottom
        // hier die Action-Buttons abhängig von variabel langem Inhalt darüber ans Kartenende, was
        // sich nicht sauber in DrawCards festen Content->Spacing->Action-Ablauf einfügt).
        //
        // cardWidth gegenüber dem DrawCardGrid-Standard (220f) leicht erhöht (260f) - verkürzt die
        // Notiz-Zeilenumbrüche (bei max. 60 Zeichen von ca. 3 auf 2 Zeilen), was wiederum Höhe
        // spart. Die HÖHE selbst ist NICHT mehr fest (siehe vorheriger Fixwert 280f), sondern nutzt
        // den heightSelector-Overload von DrawCardGrid: ComputeGroupBrowseCardHeight berechnet sie
        // je Gruppe individuell aus deren tatsächlichem Inhalt (Notiz gesetzt? Zielspells vorhanden?
        // siehe dortige Doc) - eine einzelne feste Höhe müsste sonst immer für den ungünstigsten Fall
        // aller Gruppen dimensioniert sein, auch wenn die meisten deutlich weniger Inhalt haben.
        this.DrawCardGrid(
            "GroupBrowseGroup", groups, group => this.DrawGroupBrowseEntry(group, allSpellIds),
            this.ComputeGroupBrowseCardHeight, cardWidth: 260f);

        this.DrawGroupTargetSpellDetailPopup();
    }

    // Individuelle Kartenhöhe je Gruppe für den heightSelector-Overload von DrawCardGrid (siehe
    // MainWindow.Shared.cs) - bewusst eine grobe Zeilen-Heuristik statt einer pixelgenauen
    // Vorausberechnung über ImGui.CalcTextSize (würde u.a. Zeilenumbrüche innerhalb einer einzelnen
    // ImGui.TextWrapped-Zeile mitberücksichtigen müssen): zählt stattdessen die in DrawGroupBrowseEntry
    // gezeichneten LOGISCHEN Zeilen/Elemente und multipliziert mit ImGui.GetTextLineHeightWithSpacing()
    // bzw. ImGui.GetFrameHeightWithSpacing() (statt hart codierter Pixelwerte), damit die Höhe bei
    // anderen Schriftgrößen/UI-Skalierungen konsistent mitskaliert.
    //
    // Basis (immer gezeichnet): Mitgliederkopf + Tags-Zeile + Gesucht-Zeile + die zwei
    // Beitrags-Vorschau-Zeilen = 5 Text-Zeilen, plus IMMER eine Button-Zeile für den (ggf.
    // deaktivierten, aber immer gezeichneten) "Zur Vergleich hinzufügen"-Button.
    // Optional oben drauf: +1 Text-Zeile, wenn die Notiz nicht leer ist, UND +1 Button-Zeile, wenn
    // die Gruppe Zielspells hat (dann kommt der "Ziel-Spells anzeigen"-Button als zweite Button-
    // Zeile hinzu, siehe Teil A/DrawGroupBrowseEntry).
    private float ComputeGroupBrowseCardHeight(GroupFinderGroupEntry group)
    {
        var baseTextLines = 5;
        if (!string.IsNullOrEmpty(group.Note))
            baseTextLines++;

        var buttonRowCount = group.TargetSpellIds.Count > 0 ? 2 : 1;

        var textHeight = baseTextLines * ImGui.GetTextLineHeightWithSpacing();
        var buttonsHeight = buttonRowCount * ImGui.GetFrameHeightWithSpacing();
        var childPadding = ImGui.GetStyle().WindowPadding.Y * 2;

        return textHeight + buttonsHeight + childPadding;
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

        // Beide Buttons stehen untereinander statt nebeneinander (siehe Aufgabenstellung Teil A) -
        // die kombinierte Breite beider Labels (v.a. in Französisch/Japanisch, siehe UiStrings)
        // sprengt ohnehin die Kartenbreite; volle Kartenbreite je Button (Vector2(-1, 0)) vermeidet
        // dadurch das bisherige Abschneiden/Quetschen. AlignCursorToCardBottom reserviert dafür
        // entsprechend 1 oder 2 Button-Zeilen (siehe dortiger Parameter buttonRowCount).
        AlignCursorToCardBottom(group.TargetSpellIds.Count > 0 ? 2 : 1);

        // Nur anzeigen, wenn die Gruppe überhaupt Ziel-Spells hat - ein Popup ohne Inhalt zu öffnen
        // brächte nichts (siehe DrawGroupTargetSpellDetailPopup).
        if (group.TargetSpellIds.Count > 0)
        {
            if (ImGui.Button(
                    $"{UiStrings.Get(UiStrings.Key.GroupFinderShowTargetSpellsButton, this.displayLanguage)}##GroupBrowseTargetSpells{group.GroupId}",
                    new System.Numerics.Vector2(-1, 0)))
            {
                this.groupTargetSpellDetailPopupEntry = group;
                ImGui.OpenPopup(GroupTargetSpellDetailPopupId);
            }
        }

        ImGui.BeginDisabled(availableMembers.Count == 0);
        if (ImGui.Button(
                $"{UiStrings.Get(UiStrings.Key.GroupFinderAddGroupToComparisonButton, this.displayLanguage)}##GroupBrowseAdd{group.GroupId}",
                new System.Numerics.Vector2(-1, 0)))
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

    // Zeigt targetSpellIds der zuletzt per "Ziel-Spells anzeigen"-Button ausgewählten Gruppe
    // (siehe groupTargetSpellDetailPopupEntry-Doc in MainWindow.cs) markiert danach, ob der lokale
    // Spieler den jeweiligen Spell schon gelernt hat (reiner Abgleich gegen die eigene Lernliste,
    // daher direkt inline statt über einen eigenen Service - siehe Audit-Refactoring). ImGui.
    // BeginPopup() MUSS JEDEN Frame aufgerufen werden (liefert nur dann true, wenn zuvor
    // ImGui.OpenPopup() mit derselben ID aufgerufen wurde), nicht nur wenn ein Klick stattfand -
    // Standard-ImGui-Popup-Muster.
    private void DrawGroupTargetSpellDetailPopup()
    {
        if (!ImGui.BeginPopup(GroupTargetSpellDetailPopupId))
            return;

        if (this.groupTargetSpellDetailPopupEntry is { } group)
        {
            this.DrawSectionHeader(UiStrings.Format(
                UiStrings.Key.GroupTargetSpellPopupHeader, this.displayLanguage, group.TargetSpellIds.Count));
            ImGui.Separator();

            var learnedSpellIds = this.localSpellUnlockService.GetLearnedSpellIds();
            var statuses = group.TargetSpellIds
                .Select(spellId => (SpellId: spellId, IsLearned: learnedSpellIds.Contains(spellId)))
                .ToList();

            var rows = statuses
                .Select(status =>
                {
                    var hasSpell = this.spellDataService.Spells.TryGetValue(status.SpellId, out var spell);
                    return new
                    {
                        Status = status,
                        Name = hasSpell ? this.GetSpellName(spell!) : UiStrings.Format(UiStrings.Key.SpellFallback, this.displayLanguage, status.SpellId),
                        SpellbookOrder = hasSpell ? spell!.SpellbookOrder : int.MaxValue,
                        IconId = hasSpell ? spell!.IconId : 0u,
                    };
                })
                .OrderBy(r => r.SpellbookOrder)
                .ToList();

            foreach (var row in rows)
            {
                var orderText = row.SpellbookOrder == int.MaxValue ? "—" : $"#{row.SpellbookOrder:D3}";
                var prefix = row.Status.IsLearned ? "✓" : "–";
                var color = row.Status.IsLearned ? SuccessMessageColor : NotLearnedColor;

                this.DrawSpellIcon(row.IconId);
                ImGui.SameLine();

                ImGui.PushStyleColor(ImGuiCol.Text, color);
                var clicked = ImGui.Selectable($"{prefix} {orderText}  {row.Name}##GroupTargetSpellPopupRow{row.Status.SpellId}");
                ImGui.PopStyleColor();

                if (clicked)
                {
                    this.JumpToSpellInSpellbook(row.Status.SpellId);
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.EndPopup();
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
}
