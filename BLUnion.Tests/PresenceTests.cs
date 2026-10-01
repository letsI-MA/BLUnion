using System.Net;
using System.Reflection;
using System.Text;
using BLUnion.Models;
using BLUnion.Services;
using BLUnion.Tests.Fakes;
using Xunit;

namespace BLUnion.Tests;

// Abfrage beim ersten Start (Onboarding), Online-Status-Signale (PresenceSignalPlanner), Anzeige
// (PresenceFormatter) und die Presence-Teile von LiveSyncService - alles ohne Dalamud-Runtime.
public class PresenceTests
{
    private static readonly PresenceCharacter Alice = new("Alice Test", "Gilgamesh");
    private static readonly PresenceCharacter Bob = new("Bob Test", "Gilgamesh");
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private static bool Always(PresenceCharacter _) => true;

    // ----- Onboarding -----

    [Fact]
    public void NewUser_IsAsked()
    {
        Assert.True(Onboarding.ShouldAsk(new Configuration()));
    }

    [Fact]
    public void ExistingUsers_WithLiveSyncOrToken_AreNeverAsked_AndGetMarkedAsDecided()
    {
        var active = new Configuration { LiveSyncEnabled = true };
        var withToken = new Configuration { LiveSyncEditTokens = { ["Alice Test@Gilgamesh"] = "token" } };

        foreach (var configuration in new[] { active, withToken })
        {
            Assert.False(Onboarding.ShouldAsk(configuration));
            Assert.True(Onboarding.MarkExistingUserAsDecided(configuration));
            Assert.True(configuration.OnboardingCompleted);

            // Auch nach Abschalten von Live-Sync bleibt es bei "entschieden".
            configuration.LiveSyncEnabled = false;
            configuration.LiveSyncEditTokens.Clear();
            Assert.False(Onboarding.ShouldAsk(configuration));
        }
    }

    [Fact]
    public void MarkExistingUserAsDecided_LeavesNewUsersAlone()
    {
        var configuration = new Configuration();

        Assert.False(Onboarding.MarkExistingUserAsDecided(configuration));
        Assert.False(configuration.OnboardingCompleted);
    }

    [Fact]
    public void ClosingWithoutChoice_AsksAgain_ChoosingDoesNot()
    {
        var configuration = new Configuration();
        Assert.True(Onboarding.ShouldAsk(configuration));
        Assert.True(Onboarding.ShouldAsk(configuration));

        Onboarding.ApplyChoice(configuration, enableLiveSync: false, sharePresence: false);
        Assert.False(Onboarding.ShouldAsk(configuration));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, false)]
    public void ApplyChoice_SharePresenceOnlyWithLiveSync(bool liveSync, bool sharePresence, bool expectedLiveSync, bool expectedSharePresence)
    {
        var configuration = new Configuration();

        Onboarding.ApplyChoice(configuration, liveSync, sharePresence);

        Assert.Equal(expectedLiveSync, configuration.LiveSyncEnabled);
        Assert.Equal(expectedSharePresence, configuration.SharePresence);
        Assert.True(configuration.OnboardingCompleted);
    }

    [Fact]
    public void Configuration_DefaultsAreOff()
    {
        var configuration = new Configuration();

        Assert.False(configuration.OnboardingCompleted);
        Assert.False(configuration.SharePresence);
    }

    // ----- Signal-Logik -----

    [Fact]
    public void Login_SendsOnline_OnlyOnce()
    {
        var planner = new PresenceSignalPlanner();

        Assert.Equal([new PresenceSignal(Alice, PresenceState.Online)], planner.Update(Start, Alice, Always));
        Assert.Empty(planner.Update(Start.AddMinutes(1), Alice, Always));
    }

    [Fact]
    public void Logout_SendsOffline()
    {
        var planner = new PresenceSignalPlanner();
        planner.Update(Start, Alice, Always);

        Assert.Equal([new PresenceSignal(Alice, PresenceState.Offline)], planner.Update(Start.AddMinutes(5), null, Always));
        Assert.Empty(planner.Update(Start.AddMinutes(6), null, Always));
    }

    [Fact]
    public void WhileLoggedIn_HeartbeatEvery60Minutes()
    {
        var planner = new PresenceSignalPlanner();
        planner.Update(Start, Alice, Always);

        Assert.Empty(planner.Update(Start.AddMinutes(59), Alice, Always));
        Assert.Equal([new PresenceSignal(Alice, PresenceState.Online)], planner.Update(Start.AddMinutes(60), Alice, Always));
        Assert.Empty(planner.Update(Start.AddMinutes(119), Alice, Always));
        Assert.Single(planner.Update(Start.AddMinutes(120), Alice, Always));
    }

    [Fact]
    public void CharacterChange_SendsOfflineForOld_ThenOnlineForNew()
    {
        var planner = new PresenceSignalPlanner();
        planner.Update(Start, Alice, Always);

        Assert.Equal(
            [new PresenceSignal(Alice, PresenceState.Offline), new PresenceSignal(Bob, PresenceState.Online)],
            planner.Update(Start.AddMinutes(10), Bob, Always));
    }

    [Fact]
    public void WithoutConsentOrProfile_NothingIsSent()
    {
        var planner = new PresenceSignalPlanner();

        Assert.Empty(planner.Update(Start, Alice, _ => false));
        Assert.Empty(planner.Update(Start.AddMinutes(90), Alice, _ => false));
        Assert.Empty(planner.Update(Start.AddMinutes(91), null, _ => false));
        Assert.Null(planner.Shutdown());
    }

    [Fact]
    public void ProfileAppearingLater_SendsOnlineThen()
    {
        var planner = new PresenceSignalPlanner();
        var hasProfile = false;

        Assert.Empty(planner.Update(Start, Alice, _ => hasProfile));
        hasProfile = true;
        Assert.Equal([new PresenceSignal(Alice, PresenceState.Online)], planner.Update(Start.AddSeconds(5), Alice, _ => hasProfile));
    }

    [Fact]
    public void SwitchingTheSettingOff_SendsOffline_AndStopsHeartbeats()
    {
        var planner = new PresenceSignalPlanner();
        var enabled = true;
        planner.Update(Start, Alice, _ => enabled);

        enabled = false;
        Assert.Equal([new PresenceSignal(Alice, PresenceState.Offline)], planner.Update(Start.AddMinutes(1), Alice, _ => enabled));
        Assert.Empty(planner.Update(Start.AddMinutes(61), Alice, _ => enabled));
    }

    [Fact]
    public void Shutdown_WhileLoggedIn_SendsOneOffline()
    {
        var planner = new PresenceSignalPlanner();
        planner.Update(Start, Alice, Always);

        Assert.Equal(new PresenceSignal(Alice, PresenceState.Offline), planner.Shutdown());
        Assert.Null(planner.Shutdown());
    }

    [Fact]
    public void Shutdown_AfterLogout_SendsNothing()
    {
        var planner = new PresenceSignalPlanner();
        planner.Update(Start, Alice, Always);
        planner.Update(Start.AddMinutes(1), null, Always);

        Assert.Null(planner.Shutdown());
    }

    // ----- Sofort-Signal beim Umschalten der Option -----

    [Theory]
    [InlineData(true, true, true, false, true)]
    [InlineData(false, true, true, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, true, true, true, false)] // Profil-PUT mit sharePresence noch unterwegs
    public void CanSend_RequiresLiveSyncConsentProfileAndNoPendingUpdate(
        bool liveSync, bool sharePresence, bool hasToken, bool updatePending, bool expected)
    {
        Assert.Equal(expected, PresenceSignalPlanner.CanSend(liveSync, sharePresence, hasToken, updatePending));
    }

    [Fact]
    public void SwitchingOn_SendsOnline_RightAfterTheProfileUpdate_NotWithTheNextHeartbeat()
    {
        var planner = new PresenceSignalPlanner();
        var sharePresence = false;
        var updatePending = false;
        bool CanSend(PresenceCharacter _) => PresenceSignalPlanner.CanSend(true, sharePresence, true, updatePending);

        Assert.Empty(planner.Update(Start, Alice, CanSend));

        // Einschalten: Profil-PUT angefordert -> noch kein Signal (der Worker würde es mit 403 ablehnen).
        sharePresence = true;
        updatePending = true;
        Assert.Empty(planner.Update(Start.AddSeconds(1), Alice, CanSend));

        // PUT fertig -> beim nächsten Tick sofort "online".
        updatePending = false;
        Assert.Equal([new PresenceSignal(Alice, PresenceState.Online)], planner.Update(Start.AddSeconds(2), Alice, CanSend));
    }

    [Fact]
    public void SwitchingOff_SendsOfflineImmediately_EvenWhileTheProfileUpdateIsPending()
    {
        var planner = new PresenceSignalPlanner();
        var sharePresence = true;
        var updatePending = false;
        bool CanSend(PresenceCharacter _) => PresenceSignalPlanner.CanSend(true, sharePresence, true, updatePending);
        planner.Update(Start, Alice, CanSend);

        sharePresence = false;
        updatePending = true;
        Assert.Equal([new PresenceSignal(Alice, PresenceState.Offline)], planner.Update(Start.AddSeconds(1), Alice, CanSend));
    }

    [Fact]
    public void LiveSyncService_SharePresenceUpdate_IsPendingAfterRequest()
    {
        var service = CreateService(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), new Configuration { LiveSyncEnabled = true, SharePresence = true });

        Assert.False(service.SharePresenceUpdatePending);
        service.RequestSharePresenceUpdate();
        Assert.True(service.SharePresenceUpdatePending);
    }

    // ----- Hinweise (Settings-Warnung, Bestandsnutzer-Hinweis) -----

    [Theory]
    [InlineData(true, true, true, false, true)]   // Profil vorhanden, nicht gelistet
    [InlineData(true, true, false, null, true)]   // noch gar kein Profil -> sicher nicht gelistet
    [InlineData(true, true, true, true, false)]   // gelistet
    [InlineData(true, true, true, null, false)]   // Stand noch unbekannt -> keine (womöglich falsche) Warnung
    [InlineData(true, false, true, false, false)] // Option aus
    [InlineData(false, true, true, false, false)] // Live-Sync aus
    public void NotListedWarning_OnlyWithOptionOnAndEntryNotListed(
        bool liveSync, bool sharePresence, bool hasProfile, bool? listed, bool expected)
    {
        var configuration = new Configuration { LiveSyncEnabled = liveSync, SharePresence = sharePresence };

        Assert.Equal(expected, PresenceHints.ShowNotListedWarning(configuration, hasProfile, listed));
    }

    [Fact]
    public void NewOptionNotice_ShownOnceForExistingLiveSyncUsers()
    {
        var configuration = new Configuration { LiveSyncEnabled = true };
        Onboarding.MarkExistingUserAsDecided(configuration);

        Assert.True(PresenceHints.ShowNewOptionNotice(configuration));

        PresenceHints.DismissNewOptionNotice(configuration);
        Assert.False(PresenceHints.ShowNewOptionNotice(configuration));
    }

    [Fact]
    public void NewOptionNotice_NotShown_WhenOptionActive_OnboardingPending_OrWithoutLiveSync()
    {
        Assert.False(PresenceHints.ShowNewOptionNotice(new Configuration { LiveSyncEnabled = true, SharePresence = true }));
        Assert.False(PresenceHints.ShowNewOptionNotice(new Configuration())); // Onboarding steht noch aus
        Assert.False(PresenceHints.ShowNewOptionNotice(new Configuration { OnboardingCompleted = true }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NewOptionNotice_NotShown_AfterTheOnboardingShowedTheOption(bool enableLiveSync)
    {
        var configuration = new Configuration();
        Onboarding.ApplyChoice(configuration, enableLiveSync, sharePresence: false);

        Assert.True(configuration.SharePresenceNoticeDismissed);
        Assert.False(PresenceHints.ShowNewOptionNotice(configuration));
    }

    // ----- Formatierung -----

    [Theory]
    [InlineData(0, "zuletzt online vor 1 Min.")]
    [InlineData(30, "zuletzt online vor 30 Min.")]
    [InlineData(59, "zuletzt online vor 59 Min.")]
    [InlineData(60, "zuletzt online vor 1 Std.")]
    [InlineData(23 * 60 + 59, "zuletzt online vor 23 Std.")]
    [InlineData(24 * 60, "zuletzt online vor 1 Tag(en)")]
    [InlineData(10 * 24 * 60, "zuletzt online vor 10 Tag(en)")]
    public void LastSeen_IsFormattedRoughly(int minutesAgo, string expected)
    {
        Assert.Equal(expected, PresenceFormatter.FormatLastSeen(Start, Start.AddMinutes(-minutesAgo), DisplayLanguage.German));
    }

    [Fact]
    public void LastSeen_InTheFuture_CountsAsJustNow()
    {
        Assert.Equal("last online 1 min ago", PresenceFormatter.FormatLastSeen(Start, Start.AddMinutes(10), DisplayLanguage.English));
    }

    [Fact]
    public void Status_OnlineWins_NothingWithoutPresence()
    {
        Assert.Equal("● online", PresenceFormatter.FormatStatus(true, Start, Start, DisplayLanguage.German));
        Assert.Equal("last online 2 h ago", PresenceFormatter.FormatStatus(false, Start.AddHours(-2), Start, DisplayLanguage.English));
        Assert.Null(PresenceFormatter.FormatStatus(false, null, Start, DisplayLanguage.English));
        Assert.Null(PresenceFormatter.FormatStatus(null, null, Start, DisplayLanguage.English));
    }

    [Fact]
    public void TellCommand_HasNameAtWorldAndTrailingSpace()
    {
        Assert.Equal("/tell Alice Test@Gilgamesh ", PresenceFormatter.BuildTellCommand("Alice Test", "Gilgamesh"));
    }

    // ----- LiveSyncService -----

    private static SpellDataService LoadFixtureSpellData()
    {
        var service = new SpellDataService(new TestPluginLog());
        service.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SpellData"));
        return service;
    }

    private static LiveSyncService CreateService(HttpMessageHandler handler, Configuration configuration, SpellDataService? spellData = null)
    {
        var dataService = spellData ?? LoadFixtureSpellData();
        return new LiveSyncService(null!, dataService, null!, new ManualCodeSyncProvider(dataService), configuration, new TestPluginLog(), handler);
    }

    [Fact]
    public async Task SendPresence_PostsStateWithEditToken()
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var configuration = new Configuration { LiveSyncEditTokens = { ["Alice Test@Gilgamesh"] = "secret" } };
        using var service = CreateService(handler, configuration);

        await service.SendPresenceAsync(new PresenceSignal(Alice, PresenceState.Online), CancellationToken.None);
        await service.SendPresenceAsync(new PresenceSignal(Alice, PresenceState.Offline), CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/presence/Gilgamesh/Alice%20Test", request.RequestUri!.AbsoluteUri);
            Assert.Equal("secret", request.EditTokenHeader);
        });
        Assert.Contains("\"state\":\"online\"", handler.Requests[0].Body);
        Assert.Contains("\"state\":\"offline\"", handler.Requests[1].Body);
    }

    [Fact]
    public async Task SendPresence_WithoutToken_SendsNothing_AndFailuresNeverThrow()
    {
        using var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("offline"));
        var configuration = new Configuration { LiveSyncEditTokens = { ["Alice Test@Gilgamesh"] = "secret" } };
        using var service = CreateService(handler, configuration);

        await service.SendPresenceAsync(new PresenceSignal(Bob, PresenceState.Online), CancellationToken.None);
        Assert.Empty(handler.Requests);

        // Netzwerkfehler: kein Throw, keine Wiederholung.
        await service.SendPresenceAsync(new PresenceSignal(Alice, PresenceState.Online), CancellationToken.None);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PushBody_CarriesTheSharePresenceSetting(bool sharePresence)
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var service = CreateService(handler, new Configuration { SharePresence = sharePresence });

        var method = typeof(LiveSyncService).GetMethod("BuildPushRequestBody", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var body = method.Invoke(service, ["bitmask==", null, false])!;

        Assert.Equal(sharePresence, (bool)body.GetType().GetProperty("SharePresence")!.GetValue(body)!);
    }

    [Fact]
    public async Task Browse_ParsesOnlineAndLastSeen_OnlyWhenPresent()
    {
        var spellData = LoadFixtureSpellData();
        var bitmask = Convert.ToBase64String(new byte[16]).TrimEnd('=');
        var json = $$"""
        [
          { "characterName": "Online Player", "world": "Gilgamesh", "spellBitmaskBase64": "{{bitmask}}", "online": true, "lastSeen": "2026-09-30T12:15:00.000Z" },
          { "characterName": "Quiet Player", "world": "Gilgamesh", "spellBitmaskBase64": "{{bitmask}}" }
        ]
        """;

        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        using var service = CreateService(handler, new Configuration(), spellData);
        typeof(LiveSyncService).GetProperty(nameof(LiveSyncService.LastKnownOwnProfile))!.SetValue(service, new OwnProfileSnapshot
        {
            DataCenter = "Aether",
            VisibleInGroupFinder = true,
            AvailabilityTags = [],
            Note = string.Empty,
            WantedPlayerCount = 0,
        });

        service.TriggerBrowse();
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (service.LastBrowseResults.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(5);

        var online = Assert.Single(service.LastBrowseResults, entry => entry.CharacterName == "Online Player");
        Assert.True(online.Online);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 12, 15, 0, TimeSpan.Zero), online.LastSeen);

        var quiet = Assert.Single(service.LastBrowseResults, entry => entry.CharacterName == "Quiet Player");
        Assert.Null(quiet.Online);
        Assert.Null(quiet.LastSeen);
    }
}
