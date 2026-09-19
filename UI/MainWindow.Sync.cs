using System.Diagnostics;
using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using EcChat = ECommons.Automation.Chat;

namespace BLUnion.UI;

public sealed partial class MainWindow
{
    private void DrawSyncSection()
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
            this.ExportAndShareOwnStatus();

        ImGui.Separator();
        this.DrawImportCodeInput();

        ImGui.Separator();

        var autoImportSyncCodes = this.configuration.AutoImportSyncCodesFromPartyChat;
        if (ImGui.Checkbox(UiStrings.Get(UiStrings.Key.AutoImportSyncCodesToggle, this.displayLanguage), ref autoImportSyncCodes))
        {
            this.configuration.AutoImportSyncCodesFromPartyChat = autoImportSyncCodes;
            this.configuration.Save();

            if (autoImportSyncCodes)
                this.chatGui.ChatMessage += this.OnChatMessage;
            else
                this.chatGui.ChatMessage -= this.OnChatMessage;
        }

        this.DrawHintText(UiStrings.Get(UiStrings.Key.AutoImportSyncCodesHint, this.displayLanguage));

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

            // Ohne World bleibt die Fixture in der Sync-Liste-Mitgliederauswahl beim
            // Gruppen-Publish dauerhaft ausgegraut ("(?)", siehe DrawGroupPublishSyncListMemberList
            // in MainWindow.GroupFinder.cs) - dieselbe World wie PublishDevTestProfilesAsync
            // (localWorld, siehe LiveSyncService) sorgt dafür, dass lokale Sync-Liste-Kopie und
            // server-seitig veröffentlichtes Profil dieselbe Identität (world+characterName) teilen.
            // Bleibt localWorld null/leer (kein eingeloggter Charakter erkannt), bleibt die Fixture
            // unverändert - ein Randfall, der beim normalen Testen nicht auftritt.
            var localWorld = this.partyService.GetLocalPlayerWorld();
            if (!string.IsNullOrEmpty(localWorld))
                fixture = fixture with { World = localWorld };

            this.syncProvider.PublishLocalStatus(fixture);
            this.SetSuccessMessage(UiStrings.Format(
                UiStrings.Key.DevFixtureLoaded, this.displayLanguage, fixture.CharacterName, fixture.LearnedSpellIds.Count));
        }
    }
#endif

    // Ausgelagert aus DrawSyncSection (siehe Aufgabenstellung "Export-Button auch auf Home
    // verfügbar machen") - wird sowohl vom bestehenden Button in DrawSyncSection als auch vom neuen
    // Button in DrawSyncQuickActionsCard (MainWindow.Home.cs) aufgerufen, damit die Logik an genau
    // einer Stelle steht.
    private void ExportAndShareOwnStatus()
    {
        try
        {
            var code = this.GenerateOwnStatusCode();

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

    // Reine Erzeugung ohne Clipboard/Chat-Versand (siehe ExportAndShareOwnStatus für Button-Pfad,
    // TryAutoPublishOwnStatus für den Auto-Lauf): veröffentlicht den eigenen Status in
    // syncProvider.known und liefert den BLU:-Code. Wirft bei Fehlern (z.B. Name > 255 UTF-8-Bytes,
    // mehr als 128 Spells) statt null zu liefern - der Button-Pfad zeigt die Meldung über sein
    // try/catch, der Auto-Lauf loggt sie.
    private string GenerateOwnStatusCode()
    {
        var localPlayerName = this.partyService.GetLocalPlayerName()
            ?? UiStrings.Get(UiStrings.Key.LocalPlayerFallbackName, this.displayLanguage);

        var status = this.localSpellUnlockService.GetLocalPlayerStatus(localPlayerName);
        this.syncProvider.PublishLocalStatus(status);
        return this.syncProvider.ExportToCode(status);
    }

    // Wird jeden Frame aus UiBuilder.Draw aufgerufen (siehe Plugin.cs) - NICHT aus MainWindow.Draw,
    // das nur bei geöffnetem Fenster läuft. Veröffentlicht den eigenen Status einmal pro Plugin-Start
    // in syncProvider.known, damit der eigene Charakter ohne Button-Klick dort steht. Bewusst NUR die
    // Erzeugung: kein Clipboard, kein TryAutoShareToPartyChat (autoShareToPartyChat/lastAutoShareAt
    // bleiben unberührt) - das WANN des Teilens gehört ausschließlich dem Button-Pfad.
    // Wartet AutoPublishOwnStatusDelay nach dem ersten Frame mit Charakter, damit IUnlockState
    // gefüllt ist; der Code-String wird verworfen (er ist deterministisch und nicht gespeichert).
    public void TryAutoPublishOwnStatus()
    {
        if (this.ownStatusAutoPublishDone)
            return;

        try
        {
            // GetLocalPlayerName() ist genau dann null, wenn objectTable.LocalPlayer null ist.
            if (string.IsNullOrEmpty(this.partyService.GetLocalPlayerName()))
            {
                this.localPlayerFirstSeenAt = null;
                return;
            }

            var now = DateTimeOffset.UtcNow;
            this.localPlayerFirstSeenAt ??= now;
            if (now - this.localPlayerFirstSeenAt.Value < AutoPublishOwnStatusDelay)
                return;

            // Latch VOR der Erzeugung: bei einem Fehler soll nicht jeden Frame neu versucht/geloggt werden.
            this.ownStatusAutoPublishDone = true;
            _ = this.GenerateOwnStatusCode();
            this.log.Debug("Eigener Status wurde beim Plugin-Start automatisch veröffentlicht.");
        }
        catch (Exception ex)
        {
            this.ownStatusAutoPublishDone = true;
            this.log.Warning(ex, "Automatisches Veröffentlichen des eigenen Status beim Plugin-Start fehlgeschlagen.");
        }
    }

    // Ausgelagert aus DrawSyncSection, siehe ExportAndShareOwnStatus-Doc - nutzt bewusst dasselbe
    // importCodeBuffer-Feld, damit ein auf Home angefangener Import nicht verloren geht, wenn man
    // zwischendurch zu Settings wechselt (oder umgekehrt).
    private void DrawImportCodeInput()
    {
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
    }

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
