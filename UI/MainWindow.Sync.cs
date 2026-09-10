using System.Diagnostics;
using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using EcChat = ECommons.Automation.Chat;

namespace BLUnion.UI;

public sealed partial class MainWindow
{
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

        this.DrawHintText(UiStrings.Get(UiStrings.Key.AutoImportAsLeaderHint, this.displayLanguage));

        ImGui.Separator();
        this.DrawSectionHeader(UiStrings.Get(UiStrings.Key.CurrentlyLoadedPlayersHeader, this.displayLanguage));

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

                line += this.FormatRelativeTimestamp(status.Timestamp);

                ImGui.TextUnformatted(line);

                ImGui.SameLine();

                if (ImGui.Button($"{UiStrings.Get(UiStrings.Key.RemoveButton, this.displayLanguage)}##{status.CharacterName}"))
                    playerToRemove = status.CharacterName;
            }

            if (playerToRemove is not null)
                this.syncProvider.RemovePlayer(playerToRemove);
        }

        ImGui.Separator();
#if DEBUG
        // Dev-Only: "Alice/Bob/Charles laden" + "Testprofile im Gruppenfinder veröffentlichen" -
        // per #if DEBUG komplett aus Release-Builds entfernt (siehe Aufgabenstellung), nicht nur
        // versteckt. DrawDevFixtureButton, DevTestFixtures und LiveSyncService.PublishDevTestProfiles
        // sind entsprechend ebenfalls nur in Debug-Builds vorhanden.
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
#endif

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

#if DEBUG
    // Dev-Only: löst auch die grüne "Dev-Tool: ... geladen"-Erfolgsmeldung aus (UiStrings.Key.
    // DevFixtureLoaded) - da dieser gesamte Aufrufpfad nur in Debug-Builds existiert, kann sie in
    // einem Release-Build nie erscheinen.
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
#endif

    // PlayerSpellStatus.Timestamp wird bei JEDER (Neu-)Erzeugung eines Eintrags frisch gesetzt
    // (Default = DateTimeOffset.UtcNow, siehe Models/PlayerSpellStatus.cs) - egal ob durch
    // "Bestimmen & Exportieren" (eigener Status), Code-Import oder Gruppenfinder-"Zum Vergleich
    // hinzufügen" (siehe LocalSpellUnlockService.GetLocalPlayerStatus, ManualCodeSyncProvider.
    // Decode*Format, DrawOtherPlayersSection/DrawGroupBrowseEntry). Kein eigenes LastUpdated-Feld
    // nötig - Timestamp erfüllt exakt diese Rolle bereits.
    private string FormatRelativeTimestamp(DateTimeOffset timestamp)
    {
        var minutesAgo = (int)Math.Max(0, (DateTimeOffset.UtcNow - timestamp).TotalMinutes);

        return minutesAgo < 1
            ? UiStrings.Get(UiStrings.Key.PlayerLastUpdatedJustNow, this.displayLanguage)
            : UiStrings.Format(UiStrings.Key.PlayerLastUpdatedMinutesAgoFormat, this.displayLanguage, minutesAgo);
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
}
