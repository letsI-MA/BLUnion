using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BLUnion.Models;
using Dalamud.Plugin.Services;

namespace BLUnion.Services;

public enum LiveSyncEventKind
{
    PushSucceeded,
    PushFailed,
    FetchFailed,
    DeleteSucceeded,
    DeleteFailed,

    BrowseFailed,

    DevTestProfilesPublished,

    DevTestProfilesFailed,

    GroupPublishSucceeded,

    GroupPublishFailed,

    GroupUnpublishSucceeded,

    GroupUnpublishFailed,

    GroupBrowseFailed,
}

public sealed class LiveSyncService : IDisposable
{
    private const string WorkerBaseUrl = "https://blunion-livesync.skysurfer101.workers.dev";

    private static readonly TimeSpan PartyPollInterval = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan LocalLearnedSpellCheckInterval = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient httpClient;
    private readonly PartyService partyService;
    private readonly SpellDataService spellDataService;
    private readonly LocalSpellUnlockService localSpellUnlockService;
    private readonly ManualCodeSyncProvider syncProvider;
    private readonly Configuration configuration;
    private readonly IPluginLog log;

    private readonly object resultLock = new();
    private LiveSyncEventKind? pendingResultKind;
    private string? pendingResultDetail;

    private volatile bool pushInFlight;
    private volatile bool fetchInFlight;
    private volatile bool deleteInFlight;

    private volatile bool groupPublishInFlight;
    private volatile bool groupDeleteInFlight;

    private DateTimeOffset? lastLocalLearnedSpellCheckAt;
    private HashSet<uint>? lastPushedLearnedSpellIds;

    private DateTimeOffset? lastPartyPollAt;
    private List<string>? lastKnownBlueMagePartyMemberNames;

    private List<string>? pendingAvailabilityTags;
    private string? pendingNote;
    private int? pendingWantedPlayerCount;
    private List<uint>? pendingTargetSpellIds;

    private volatile bool browseInFlight;

    private volatile bool groupBrowseInFlight;

#if DEBUG
    private readonly Dictionary<string, string> devTestProfileEditTokens = new();

    private volatile bool devPublishInFlight;
#endif

    // Rein lesend für die Statuszeile (siehe DrawStatusBar in MainWindow.cs) - pushInFlight/
    // fetchInFlight selbst bleiben unverändert private und werden weiterhin nur intern gesetzt.
    public bool IsSyncing => this.pushInFlight || this.fetchInFlight;

    public OwnProfileSnapshot? LastKnownOwnProfile { get; private set; }

    // Kein eigener GroupPublishSnapshot-Record wie bei LastKnownOwnProfile oben - dafür gibt es
    // (anders als bei OwnProfileSnapshot) noch keinen bestehenden "letzter bekannter Gruppenstand"-
    // Zustand, den man erweitern könnte; zwei einzelne Felder reichen für den einen Anwendungsfall
    // (Discord-Hinweis im Gruppen-Formular, siehe DrawGroupPublishSection). Beide werden zusammen
    // in PublishGroupAsync gesetzt und in DeletePublishedGroupAsync wieder auf null zurückgesetzt.
    public string? LastKnownPublishedGroupDiscordChannelUrl { get; private set; }

    public string? LastKnownPublishedGroupDiscordChannelName { get; private set; }

    public IReadOnlyList<GroupFinderEntry> LastBrowseResults { get; private set; } = Array.Empty<GroupFinderEntry>();

    public IReadOnlyList<GroupFinderGroupEntry> LastGroupBrowseResults { get; private set; } = Array.Empty<GroupFinderGroupEntry>();

    // httpMessageHandler ist ausschließlich ein Testbarkeits-Seam (siehe BLUnion.Tests/
    // LiveSyncServiceTests.cs) - ohne Angabe (der einzige Produktionsaufruf in Plugin.cs) ist das
    // Verhalten exakt wie zuvor (neuer HttpClient mit demselben Timeout). Der HttpClient übernimmt
    // per Default-Konstruktor-Verhalten den Besitz des Handlers (disposeHandler: true) - Dispose()
    // unten schließt beide.
    public LiveSyncService(
        PartyService partyService,
        SpellDataService spellDataService,
        LocalSpellUnlockService localSpellUnlockService,
        ManualCodeSyncProvider syncProvider,
        Configuration configuration,
        IPluginLog log,
        HttpMessageHandler? httpMessageHandler = null)
    {
        this.httpClient = httpMessageHandler is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(10) }
            : new HttpClient(httpMessageHandler) { Timeout = TimeSpan.FromSeconds(10) };
        this.partyService = partyService;
        this.spellDataService = spellDataService;
        this.localSpellUnlockService = localSpellUnlockService;
        this.syncProvider = syncProvider;
        this.configuration = configuration;
        this.log = log;
    }

    public void Tick()
    {
        if (!this.configuration.LiveSyncEnabled)
            return;

        this.TickPushDiff();
        this.TickPartyPoll();
    }

    private void TickPushDiff()
    {
        var now = DateTimeOffset.UtcNow;
        if (this.lastLocalLearnedSpellCheckAt is { } lastCheck && now - lastCheck < LocalLearnedSpellCheckInterval)
            return;

        this.lastLocalLearnedSpellCheckAt = now;

        var currentLearnedIds = this.localSpellUnlockService.GetLearnedSpellIds();
        if (this.lastPushedLearnedSpellIds is not null && currentLearnedIds.SetEquals(this.lastPushedLearnedSpellIds))
            return;

        this.lastPushedLearnedSpellIds = currentLearnedIds;

        // markListed: false - dieser Push läuft automatisch im Hintergrund (Diff der gelernten
        // Spells, siehe oben), NICHT als Reaktion auf einen Klick auf "Veröffentlichen"/
        // "Aktualisieren" im Group-Finder-Tab. Er darf daher niemals von sich aus visibility auf
        // "listed" setzen - sonst würde jeder gelernte Spell einen Nutzer, der nie/nicht mehr aktiv
        // veröffentlicht hat, unbeabsichtigt (wieder) im Group Finder sichtbar machen (siehe
        // PushOwnProfile-Doc).
        this.PushOwnProfile(markListed: false);
    }

    private void TickPartyPoll()
    {
        var others = this.GetOtherBlueMagePartyMembers();
        var currentNames = others.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

        var partyChanged = this.lastKnownBlueMagePartyMemberNames is null
            || !currentNames.SequenceEqual(this.lastKnownBlueMagePartyMemberNames, StringComparer.Ordinal);
        this.lastKnownBlueMagePartyMemberNames = currentNames;

        if (others.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        if (!partyChanged && this.lastPartyPollAt is { } lastPoll && now - lastPoll < PartyPollInterval)
            return;

        this.lastPartyPollAt = now;
        this.TriggerFetch(others);
    }

    private IReadOnlyList<PartyMemberInfo> GetOtherBlueMagePartyMembers()
    {
        var localName = this.partyService.GetLocalPlayerName();
        return this.partyService.GetBlueMagePartyMembers()
            .Where(m => !string.Equals(m.Name, localName, StringComparison.Ordinal))
            .ToList();
    }

    // markListed steuert AUSSCHLIESSLICH das gesendete visibility-Feld (siehe BuildPushRequestBody):
    // true nur bei einem expliziten Klick auf "Veröffentlichen"/"Aktualisieren" im Group-Finder-Tab
    // (siehe MainWindow.GroupFinder.cs), false beim automatischen Hintergrund-Push aus
    // TickPushDiff (Diff der gelernten Spells) - der soll weiterhin andere Felder aktuell halten,
    // ohne dabei versehentlich einen nie/nicht mehr veröffentlichten Eintrag sichtbar zu machen.
    public void PushOwnProfile(bool markListed)
    {
        if (this.pushInFlight)
            return;

        this.pushInFlight = true;
        _ = this.PushOwnProfileAsync(markListed);
    }

    private Task PushOwnProfileAsync(bool markListed) =>
        this.RunGuardedAsync(
            async () =>
            {
                var localName = this.partyService.GetLocalPlayerName();
                var localWorld = this.partyService.GetLocalPlayerWorld();

                if (string.IsNullOrEmpty(localName) || string.IsNullOrEmpty(localWorld))
                {
                    return;
                }

                var learnedIds = this.localSpellUnlockService.GetLearnedSpellIds();
                var bitmaskBase64 = ManualCodeSyncProvider.ToBase64Url(
                    ManualCodeSyncProvider.EncodeBitmask(this.spellDataService, learnedIds));

                var tokenKey = BuildTokenKey(localName, localWorld);
                this.configuration.LiveSyncEditTokens.TryGetValue(tokenKey, out var existingToken);

                var requestBody = this.BuildPushRequestBody(bitmaskBase64, existingToken, markListed);
                var url = BuildProfileUrl(localWorld, localName);

                using var response = await this.httpClient.PutAsJsonAsync(url, requestBody, JsonOptions).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var detail = DescribeHttpFailure(response.StatusCode, response.ReasonPhrase);
                    this.log.Warning($"LiveSyncService: Push fehlgeschlagen ({detail}) für \"{localName}@{localWorld}\".");
                    this.SetPendingResult(LiveSyncEventKind.PushFailed, detail);
                    return;
                }

                var responseBody = await response.Content.ReadFromJsonAsync<PushResponseBody>(JsonOptions).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(responseBody?.EditToken))
                {
                    this.configuration.LiveSyncEditTokens[tokenKey] = responseBody!.EditToken!;
                    this.configuration.Save();
                }

                if (!string.IsNullOrEmpty(responseBody?.DataCenter))
                {
                    this.LastKnownOwnProfile = new OwnProfileSnapshot
                    {
                        DataCenter = responseBody!.DataCenter!,
                        VisibleInGroupFinder = responseBody.Visibility == "listed",
                        AvailabilityTags = ParseAvailabilityTags(responseBody.AvailabilityTags),
                        Note = responseBody.Note ?? string.Empty,
                        WantedPlayerCount = responseBody.WantedPlayerCount ?? 0,
                        DiscordChannelUrl = responseBody.DiscordChannelUrl,
                        DiscordChannelName = responseBody.DiscordChannelName,
                    };
                }

                this.SetPendingResult(LiveSyncEventKind.PushSucceeded, null);
            },
            () => this.pushInFlight = false,
            LiveSyncEventKind.PushFailed,
            "LiveSyncService: unerwarteter Fehler beim Push des eigenen Profils.");

    // Aus PushOwnProfileAsync herausgelöst (siehe BLUnion.Tests/LiveSyncServiceTests.cs) - nimmt
    // spellBitmaskBase64/editToken bewusst als Parameter statt sie selbst zu ermitteln, damit diese
    // reine Body-Bau-Logik ohne PartyService/LocalSpellUnlockService (beide nicht isoliert testbar,
    // siehe TEST_REPORT.md) testbar ist.
    private PushRequestBody BuildPushRequestBody(string spellBitmaskBase64, string? editToken, bool markListed) =>
        new(
            spellBitmaskBase64,
            editToken,
            markListed ? "listed" : null,
            this.pendingAvailabilityTags,
            this.pendingNote,
            this.pendingWantedPlayerCount,
            this.pendingTargetSpellIds);

    public void TriggerFetch() => this.TriggerFetch(this.GetOtherBlueMagePartyMembers());

    private void TriggerFetch(IReadOnlyList<PartyMemberInfo> otherBlueMages)
    {
        if (this.fetchInFlight || otherBlueMages.Count == 0)
            return;

        this.fetchInFlight = true;
        _ = this.FetchPartyMemberProfilesAsync(otherBlueMages);
    }

    private async Task FetchPartyMemberProfilesAsync(IReadOnlyList<PartyMemberInfo> otherBlueMages)
    {
        try
        {
            var anyFailure = false;
            string? lastFailureDetail = null;

            foreach (var member in otherBlueMages)
            {
                if (string.IsNullOrEmpty(member.World))
                    continue;

                try
                {
                    var url = BuildProfileUrl(member.World, member.Name);
                    using var response = await this.httpClient.GetAsync(url).ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.NotFound)
                        continue;

                    if (!response.IsSuccessStatusCode)
                    {
                        anyFailure = true;
                        lastFailureDetail = $"{member.Name}: {DescribeHttpFailure(response.StatusCode, response.ReasonPhrase)}";
                        continue;
                    }

                    var profile = await response.Content.ReadFromJsonAsync<FetchResponseBody>(JsonOptions).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(profile?.SpellBitmaskBase64))
                        continue;

                    var learnedIds = ManualCodeSyncProvider.DecodeBitmask(
                        this.spellDataService, ManualCodeSyncProvider.FromBase64Url(profile.SpellBitmaskBase64));

                    var status = new PlayerSpellStatus
                    {
                        CharacterName = member.Name,
                        LearnedSpellIds = learnedIds,
                        IsLocalPlayer = false,
                        World = member.World,
                    };

                    this.syncProvider.PublishLocalStatus(status);
                }
                catch (Exception exMember)
                {
                    anyFailure = true;
                    lastFailureDetail = $"{member.Name}: {exMember.Message}";
                    this.log.Debug(exMember, $"LiveSyncService: Abruf für \"{member.Name}\" fehlgeschlagen.");
                }
            }

            if (anyFailure)
                this.SetPendingResult(LiveSyncEventKind.FetchFailed, lastFailureDetail);
        }
        finally
        {
            this.fetchInFlight = false;
        }
    }

    public void SetGroupFinderAvailabilityTags(IReadOnlyCollection<AvailabilityTag> tags)
    {
        this.pendingAvailabilityTags = tags.Select(tag => tag.ToWireValue()).ToList();
    }

    public void SetGroupFinderTargetSpellIds(IReadOnlyCollection<uint> spellIds)
    {
        this.pendingTargetSpellIds = spellIds.ToList();
    }

    public void SetGroupFinderNoteAndWantedPlayerCount(string note, int wantedPlayerCount)
    {
        this.pendingNote = note;
        this.pendingWantedPlayerCount = wantedPlayerCount;
    }

    public void TriggerBrowse()
    {
        if (this.browseInFlight)
            return;

        var dataCenter = this.LastKnownOwnProfile?.DataCenter;
        if (string.IsNullOrEmpty(dataCenter))
            return;

        this.browseInFlight = true;
        _ = this.TriggerBrowseAsync(dataCenter);
    }

    private Task TriggerBrowseAsync(string dataCenter) =>
        this.RunGuardedAsync(
            async () =>
            {
                var url = $"{WorkerBaseUrl}/profiles/browse?dataCenter={Uri.EscapeDataString(dataCenter)}";
                using var response = await this.httpClient.GetAsync(url).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    this.SetPendingResult(LiveSyncEventKind.BrowseFailed, DescribeHttpFailure(response.StatusCode, response.ReasonPhrase));
                    return;
                }

                var entries = await response.Content.ReadFromJsonAsync<List<BrowseResponseEntry>>(JsonOptions).ConfigureAwait(false)
                    ?? new List<BrowseResponseEntry>();

                var results = new List<GroupFinderEntry>();

                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.CharacterName) || string.IsNullOrEmpty(entry.SpellBitmaskBase64))
                        continue;

                    try
                    {
                        var learnedIds = ManualCodeSyncProvider.DecodeBitmask(
                            this.spellDataService, ManualCodeSyncProvider.FromBase64Url(entry.SpellBitmaskBase64));

                        results.Add(new GroupFinderEntry
                        {
                            CharacterName = entry.CharacterName,
                            World = entry.World ?? string.Empty,
                            LearnedSpellIds = learnedIds,
                            AvailabilityTags = ParseAvailabilityTags(entry.AvailabilityTags),
                            Note = entry.Note ?? string.Empty,
                            WantedPlayerCount = entry.WantedPlayerCount ?? 0,
                            TargetSpellIds = entry.TargetSpellIds ?? new List<uint>(),
                        });
                    }
                    catch (Exception exEntry)
                    {
                        this.log.Debug(exEntry, $"LiveSyncService: Gruppenfinder-Eintrag für \"{entry.CharacterName}\" übersprungen (ungültige Daten).");
                    }
                }

                this.LastBrowseResults = results;
            },
            () => this.browseInFlight = false,
            LiveSyncEventKind.BrowseFailed,
            "LiveSyncService: unerwarteter Fehler beim Abrufen des Gruppenfinders.");

    public void TriggerGroupBrowse()
    {
        if (this.groupBrowseInFlight)
            return;

        var dataCenter = this.LastKnownOwnProfile?.DataCenter;
        if (string.IsNullOrEmpty(dataCenter))
            return;

        this.groupBrowseInFlight = true;
        _ = this.TriggerGroupBrowseAsync(dataCenter);
    }

    private Task TriggerGroupBrowseAsync(string dataCenter) =>
        this.RunGuardedAsync(
            async () =>
            {
                var url = $"{WorkerBaseUrl}/groups/browse?dataCenter={Uri.EscapeDataString(dataCenter)}";
                using var response = await this.httpClient.GetAsync(url).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    this.SetPendingResult(LiveSyncEventKind.GroupBrowseFailed, DescribeHttpFailure(response.StatusCode, response.ReasonPhrase));
                    return;
                }

                var entries = await response.Content.ReadFromJsonAsync<List<GroupBrowseResponseEntry>>(JsonOptions).ConfigureAwait(false)
                    ?? new List<GroupBrowseResponseEntry>();

                var results = new List<GroupFinderGroupEntry>();

                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.GroupId))
                        continue;

                    try
                    {
                        var members = (entry.Members ?? new List<GroupBrowseResponseMember>())
                            .Where(m => !string.IsNullOrEmpty(m.CharacterName) && !string.IsNullOrEmpty(m.World))
                            .Select(m => new GroupFinderGroupMember
                            {
                                World = m.World!,
                                CharacterName = m.CharacterName!,
                                LearnedSpellIds = string.IsNullOrEmpty(m.SpellBitmaskBase64)
                                    ? null
                                    : ManualCodeSyncProvider.DecodeBitmask(
                                        this.spellDataService, ManualCodeSyncProvider.FromBase64Url(m.SpellBitmaskBase64)),
                            })
                            .ToList();

                        results.Add(new GroupFinderGroupEntry
                        {
                            GroupId = entry.GroupId!,
                            Members = members,
                            AvailabilityTags = ParseAvailabilityTags(entry.AvailabilityTags),
                            Note = entry.Note ?? string.Empty,
                            WantedPlayerCount = entry.WantedPlayerCount ?? 0,
                            TargetSpellIds = entry.TargetSpellIds ?? new List<uint>(),
                        });
                    }
                    catch (Exception exEntry)
                    {
                        this.log.Debug(exEntry, $"LiveSyncService: Gruppen-Eintrag \"{entry.GroupId}\" übersprungen (ungültige Daten).");
                    }
                }

                this.LastGroupBrowseResults = results;
            },
            () => this.groupBrowseInFlight = false,
            LiveSyncEventKind.GroupBrowseFailed,
            "LiveSyncService: unerwarteter Fehler beim Abrufen der Gruppen-Listungen.");

    // Dev-Only: veröffentlicht die festen Alice/Bob/Charles-Testprofile aus DevTestFixtures im
    // Gruppenfinder (siehe UI/MainWindow.cs DrawSyncTab, "Dev: Testprofile im Gruppenfinder
    // veröffentlichen"-Button). Komplett per #if DEBUG entfernt, damit ein Release-Build weder
    // die Dev-UI dafür noch diesen Aufruf/die Abhängigkeit auf DevTestFixtures enthält.
#if DEBUG
    public void PublishDevTestProfiles()
    {
        if (this.devPublishInFlight)
            return;

        var localWorld = this.partyService.GetLocalPlayerWorld();
        if (string.IsNullOrEmpty(localWorld))
        {
            this.SetPendingResult(LiveSyncEventKind.DevTestProfilesFailed, "kein eingeloggter Charakter erkannt");
            return;
        }

        this.devPublishInFlight = true;
        _ = this.PublishDevTestProfilesAsync(localWorld);
    }

    private sealed record DevTestProfileSpec(
        string Name,
        Func<SpellDataService, PlayerSpellStatus> CreateFixture,
        List<string> AvailabilityTags,
        string Note,
        int WantedPlayerCount,
        IReadOnlyList<uint> TargetSpellIds);

    // TargetSpellIds sind jeweils Spells aus Data/spells.json, die die jeweilige Fixture laut
    // DevTestFixtures.CreateFixture (Filter "Stars <= maxStars") NICHT gelernt hat - nur so liefert
    // ein Test des Browse-Zielspell-Filters (siehe DrawBrowseTargetSpellFilterSection in
    // MainWindow.GroupFinder.cs) sinnvolle "fehlt noch"-Ergebnisse statt bereits gelernter Spells.
    // Alice (maxStars 1): 2-Stern-Spells. Bob (maxStars 3): 4-Stern-Spells.
    // Charles (maxStars 4): 5-Stern-Spells (das aktuelle Maximum).
    private static readonly IReadOnlyList<DevTestProfileSpec> DevTestProfileSpecs = new List<DevTestProfileSpec>
    {
        new("Alice", DevTestFixtures.CreateAlice, new List<string> { "evening" }, "Testgruppe mit Charles", 1,
            new List<uint> { 11386, 11391, 11393 }), // Song of Torment, Plaincracker, Bristle
        new("Bob", DevTestFixtures.CreateBob, new List<string> { "flexible" }, "Suche Gruppe", 3,
            new List<uint> { 11383, 11384, 11387 }), // Snort, 4-tonze Weight, High Voltage
        new("Charles", DevTestFixtures.CreateCharles, new List<string> { "evening" }, "Testgruppe mit Alice", 1,
            new List<uint> { 11426, 11427, 11428 }), // Feather Rain, Eruption, Mountain Buster
    };

    // Nutzt RunGuardedAsync (siehe dortige Doc) BEWUSST NICHT: anders als die übrigen sechs
    // *Async-Methoden hat diese hier KEINEN einzelnen Erfolg/Fehlschlag, sondern veröffentlicht
    // DREI Fixtures unabhängig voneinander (jede mit eigenem inneren try/catch) und leitet das
    // Gesamtergebnis erst danach aus succeededNames/failedDetails ab (Published nur bei
    // Fehler:0, sonst Failed mit der Sammel-Fehlermeldung aller drei) - es gibt also gar keine
    // einzelne "geworfene Exception", die RunGuardedAsync uniform in einen Kind/ex.Message
    // umwandeln könnte, ohne die Semantik zu verbiegen.
    private async Task PublishDevTestProfilesAsync(string localWorld)
    {
        try
        {
            var succeededNames = new List<string>();
            var failedDetails = new List<string>();

            foreach (var spec in DevTestProfileSpecs)
            {
                try
                {
                    var status = spec.CreateFixture(this.spellDataService);
                    var bitmaskBase64 = ManualCodeSyncProvider.ToBase64Url(
                        ManualCodeSyncProvider.EncodeBitmask(this.spellDataService, status.LearnedSpellIds));

                    var tokenKey = BuildTokenKey(spec.Name, localWorld);
                    var url = BuildProfileUrl(localWorld, spec.Name);

                    if (this.devTestProfileEditTokens.TryGetValue(tokenKey, out var previousToken))
                    {
                        try
                        {
                            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, url);
                            deleteRequest.Headers.Add("X-Edit-Token", previousToken);
                            using var deleteResponse = await this.httpClient.SendAsync(deleteRequest).ConfigureAwait(false);
                        }
                        catch
                        {
                        }

                        this.devTestProfileEditTokens.Remove(tokenKey);
                    }

                    var requestBody = new PushRequestBody(
                        bitmaskBase64,
                        null,
                        "listed",
                        spec.AvailabilityTags,
                        spec.Note,
                        spec.WantedPlayerCount,
                        spec.TargetSpellIds.ToList());

                    using var putResponse = await this.httpClient.PutAsJsonAsync(url, requestBody, JsonOptions).ConfigureAwait(false);

                    if (!putResponse.IsSuccessStatusCode)
                    {
                        failedDetails.Add($"{spec.Name}: {DescribeHttpFailure(putResponse.StatusCode, putResponse.ReasonPhrase)}");
                        continue;
                    }

                    var putResponseBody = await putResponse.Content.ReadFromJsonAsync<PushResponseBody>(JsonOptions).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(putResponseBody?.EditToken))
                        this.devTestProfileEditTokens[tokenKey] = putResponseBody!.EditToken!;

                    succeededNames.Add(spec.Name);
                }
                catch (Exception exProfile)
                {
                    failedDetails.Add($"{spec.Name}: {exProfile.Message}");
                    this.log.Debug(exProfile, $"LiveSyncService: Dev-Testprofil \"{spec.Name}\" konnte nicht veröffentlicht werden.");
                }
            }

            if (failedDetails.Count > 0)
                this.SetPendingResult(LiveSyncEventKind.DevTestProfilesFailed, string.Join("; ", failedDetails));
            else
                this.SetPendingResult(LiveSyncEventKind.DevTestProfilesPublished, succeededNames.Count.ToString());
        }
        finally
        {
            this.devPublishInFlight = false;
        }
    }
#endif

    public bool HasEditTokenForLocalCharacter()
    {
        var localName = this.partyService.GetLocalPlayerName();
        var localWorld = this.partyService.GetLocalPlayerWorld();
        if (string.IsNullOrEmpty(localName) || string.IsNullOrEmpty(localWorld))
            return false;

        return this.configuration.LiveSyncEditTokens.ContainsKey(BuildTokenKey(localName, localWorld));
    }

    public void DeleteOwnProfile()
    {
        if (this.deleteInFlight)
            return;

        this.deleteInFlight = true;
        _ = this.DeleteOwnProfileAsync(disableLiveSync: true);
    }

    // Wie DeleteOwnProfile, aber ohne LiveSyncEnabled abzuschalten - für den Löschen-Button im
    // Group-Finder-Publish-Formular (DrawMyEntrySection), wo der Nutzer im Tab bleiben und nur den
    // veröffentlichten Eintrag samt lokalem Formular zurücksetzen will, nicht Live Sync als Ganzes
    // deaktivieren (das bleibt dem Settings-Button/DeleteOwnProfile vorbehalten).
    public void UnpublishOwnProfile()
    {
        if (this.deleteInFlight)
            return;

        this.deleteInFlight = true;
        _ = this.DeleteOwnProfileAsync(disableLiveSync: false);
    }

    private Task DeleteOwnProfileAsync(bool disableLiveSync) =>
        this.RunGuardedAsync(
            async () =>
            {
                var localName = this.partyService.GetLocalPlayerName();
                var localWorld = this.partyService.GetLocalPlayerWorld();
                if (string.IsNullOrEmpty(localName) || string.IsNullOrEmpty(localWorld))
                {
                    this.SetPendingResult(LiveSyncEventKind.DeleteFailed, null);
                    return;
                }

                var tokenKey = BuildTokenKey(localName, localWorld);
                if (!this.configuration.LiveSyncEditTokens.TryGetValue(tokenKey, out var token))
                {
                    this.SetPendingResult(LiveSyncEventKind.DeleteFailed, null);
                    return;
                }

                var url = BuildProfileUrl(localWorld, localName);
                using var request = new HttpRequestMessage(HttpMethod.Delete, url);
                request.Headers.Add("X-Edit-Token", token);

                using var response = await this.httpClient.SendAsync(request).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    this.SetPendingResult(LiveSyncEventKind.DeleteFailed, DescribeHttpFailure(response.StatusCode, response.ReasonPhrase));
                    return;
                }

                this.configuration.LiveSyncEditTokens.Remove(tokenKey);

                // DataCenter bleibt erhalten (wird von TriggerBrowse/TriggerGroupBrowse für den
                // Browse-Sub-Tab gebraucht, siehe dortige Doc) - nur die "veröffentlicht"-Felder
                // werden zurückgesetzt, sonst zeigt DrawMyEntrySection nach dem Löschen weiterhin
                // den alten Sichtbarkeits-/Discord-Hinweis (VisibleInGroupFinder bliebe sonst true).
                if (this.LastKnownOwnProfile is { } existingProfile)
                {
                    this.LastKnownOwnProfile = existingProfile with
                    {
                        VisibleInGroupFinder = false,
                        AvailabilityTags = Array.Empty<AvailabilityTag>(),
                        Note = string.Empty,
                        WantedPlayerCount = 0,
                        DiscordChannelUrl = null,
                        DiscordChannelName = null,
                    };
                }

                if (disableLiveSync)
                    this.configuration.LiveSyncEnabled = false;

                this.configuration.Save();

                this.SetPendingResult(LiveSyncEventKind.DeleteSucceeded, null);
            },
            () => this.deleteInFlight = false,
            LiveSyncEventKind.DeleteFailed,
            "LiveSyncService: unerwarteter Fehler beim Löschen des eigenen Profils.");

    private const int GroupMemberCountMin = 1;
    private const int GroupMemberCountMax = 8;

    // Muss mit GROUP_TARGET_SPELL_COUNT_MAX im Worker übereinstimmen (siehe worker/src/index.ts) -
    // derselbe früh-abbrechende Client-Check wie oben für GroupMemberCountMin/Max, damit ein
    // offensichtlich zu langes targetSpellIds erst gar nicht den Netzwerk-Roundtrip auslöst.
    private const int GroupTargetSpellCountMax = 30;

    public void PublishGroup(
        IReadOnlyList<(string World, string CharacterName)> members,
        bool visible,
        IReadOnlyCollection<AvailabilityTag> tags,
        string note,
        int wantedPlayerCount,
        IReadOnlyCollection<uint> targetSpellIds)
    {
        if (this.groupPublishInFlight)
            return;

        if (members.Count < GroupMemberCountMin || members.Count > GroupMemberCountMax)
        {
            this.SetPendingResult(
                LiveSyncEventKind.GroupPublishFailed,
                $"Mitgliederanzahl muss zwischen {GroupMemberCountMin} und {GroupMemberCountMax} liegen (aktuell {members.Count}).");
            return;
        }

        if (targetSpellIds.Count > GroupTargetSpellCountMax)
        {
            this.SetPendingResult(
                LiveSyncEventKind.GroupPublishFailed,
                $"Höchstens {GroupTargetSpellCountMax} Ziel-Spells erlaubt (aktuell {targetSpellIds.Count}).");
            return;
        }

        var localName = this.partyService.GetLocalPlayerName();
        var localWorld = this.partyService.GetLocalPlayerWorld();
        if (string.IsNullOrEmpty(localName) || string.IsNullOrEmpty(localWorld))
        {
            this.SetPendingResult(LiveSyncEventKind.GroupPublishFailed, "kein eingeloggter Charakter erkannt");
            return;
        }

        this.groupPublishInFlight = true;
        _ = this.PublishGroupAsync(members, visible, tags, note, wantedPlayerCount, targetSpellIds, localName, localWorld);
    }

    private Task PublishGroupAsync(
        IReadOnlyList<(string World, string CharacterName)> members,
        bool visible,
        IReadOnlyCollection<AvailabilityTag> tags,
        string note,
        int wantedPlayerCount,
        IReadOnlyCollection<uint> targetSpellIds,
        string localName,
        string localWorld) =>
        this.RunGuardedAsync(
            async () =>
            {
                var tokenKey = BuildTokenKey(localName, localWorld);
                var isUpdate = this.configuration.GroupFinderOwnGroupIds.TryGetValue(tokenKey, out var existingGroupId);
                var groupId = isUpdate ? existingGroupId! : Guid.NewGuid().ToString();

                string? editToken = null;
                if (isUpdate)
                    this.configuration.GroupFinderGroupEditTokens.TryGetValue(groupId, out editToken);

                var requestBody = BuildPutGroupRequestBody(members, visible, tags, note, wantedPlayerCount, targetSpellIds, editToken);

                var url = BuildGroupUrl(groupId);
                using var response = await this.httpClient.PutAsJsonAsync(url, requestBody, JsonOptions).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var detail = DescribeHttpFailure(response.StatusCode, response.ReasonPhrase);
                    this.log.Warning($"LiveSyncService: Gruppen-Publish fehlgeschlagen ({detail}) für groupId \"{groupId}\".");
                    this.SetPendingResult(LiveSyncEventKind.GroupPublishFailed, detail);
                    return;
                }

                var responseBody = await response.Content.ReadFromJsonAsync<PutGroupResponseBody>(JsonOptions).ConfigureAwait(false);

                this.configuration.GroupFinderOwnGroupIds[tokenKey] = groupId;
                if (!string.IsNullOrEmpty(responseBody?.EditToken))
                    this.configuration.GroupFinderGroupEditTokens[groupId] = responseBody!.EditToken!;
                this.configuration.Save();

                var groupIsListed = responseBody?.Visibility == "listed";
                this.LastKnownPublishedGroupDiscordChannelUrl = groupIsListed ? responseBody?.DiscordChannelUrl : null;
                this.LastKnownPublishedGroupDiscordChannelName = groupIsListed ? responseBody?.DiscordChannelName : null;

                this.SetPendingResult(LiveSyncEventKind.GroupPublishSucceeded, null);
            },
            () => this.groupPublishInFlight = false,
            LiveSyncEventKind.GroupPublishFailed,
            "LiveSyncService: unerwarteter Fehler beim Veröffentlichen der Gruppe.");

    public bool HasPublishedGroup()
    {
        var localName = this.partyService.GetLocalPlayerName();
        var localWorld = this.partyService.GetLocalPlayerWorld();
        if (string.IsNullOrEmpty(localName) || string.IsNullOrEmpty(localWorld))
            return false;

        return this.configuration.GroupFinderOwnGroupIds.ContainsKey(BuildTokenKey(localName, localWorld));
    }

    public void DeletePublishedGroup()
    {
        if (this.groupDeleteInFlight)
            return;

        var localName = this.partyService.GetLocalPlayerName();
        var localWorld = this.partyService.GetLocalPlayerWorld();
        if (string.IsNullOrEmpty(localName) || string.IsNullOrEmpty(localWorld))
        {
            this.SetPendingResult(LiveSyncEventKind.GroupUnpublishFailed, null);
            return;
        }

        var tokenKey = BuildTokenKey(localName, localWorld);
        if (!this.configuration.GroupFinderOwnGroupIds.TryGetValue(tokenKey, out var groupId))
            return;

        this.groupDeleteInFlight = true;
        _ = this.DeletePublishedGroupAsync(tokenKey, groupId);
    }

    private Task DeletePublishedGroupAsync(string tokenKey, string groupId) =>
        this.RunGuardedAsync(
            async () =>
            {
                if (!this.configuration.GroupFinderGroupEditTokens.TryGetValue(groupId, out var token))
                {
                    this.SetPendingResult(LiveSyncEventKind.GroupUnpublishFailed, null);
                    return;
                }

                var url = BuildGroupUrl(groupId);
                using var request = new HttpRequestMessage(HttpMethod.Delete, url);
                request.Headers.Add("X-Edit-Token", token);

                using var response = await this.httpClient.SendAsync(request).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    this.SetPendingResult(LiveSyncEventKind.GroupUnpublishFailed, DescribeHttpFailure(response.StatusCode, response.ReasonPhrase));
                    return;
                }

                this.configuration.GroupFinderOwnGroupIds.Remove(tokenKey);
                this.configuration.GroupFinderGroupEditTokens.Remove(groupId);
                this.configuration.Save();

                this.LastKnownPublishedGroupDiscordChannelUrl = null;
                this.LastKnownPublishedGroupDiscordChannelName = null;

                this.SetPendingResult(LiveSyncEventKind.GroupUnpublishSucceeded, null);
            },
            () => this.groupDeleteInFlight = false,
            LiveSyncEventKind.GroupUnpublishFailed,
            "LiveSyncService: unerwarteter Fehler beim Löschen der eigenen Gruppen-Listung.");

    public bool TryTakePendingResult(out LiveSyncEventKind kind, out string? detail)
    {
        lock (this.resultLock)
        {
            if (this.pendingResultKind is null)
            {
                kind = default;
                detail = null;
                return false;
            }

            kind = this.pendingResultKind.Value;
            detail = this.pendingResultDetail;
            this.pendingResultKind = null;
            this.pendingResultDetail = null;
            return true;
        }
    }

    private void SetPendingResult(LiveSyncEventKind kind, string? detail)
    {
        lock (this.resultLock)
        {
            this.pendingResultKind = kind;
            this.pendingResultDetail = detail;
        }
    }

    // Gemeinsames try/catch/finally-Gerüst für sechs der sieben *Async-Methoden mit In-Flight-Flag
    // (alle außer PublishDevTestProfilesAsync, siehe dortige Doc für den Grund) - vorher an jeder
    // einzeln dupliziert. clearInFlight setzt NUR das jeweilige Flag zurück auf false; der
    // "bereits in Flight"-Guard-Check UND das Setzen auf true bleiben bewusst in den öffentlichen
    // Trigger-Methoden (z.B. PushOwnProfile()) - die haben teils eigene zusätzliche
    // Vorbedingungen (z.B. PublishGroups Mitglieder-/Zielspell-Zahl-Prüfung), die sich nicht
    // generisch fassen lassen, ohne den Helfer unnötig zu verbiegen.
    private async Task RunGuardedAsync(Func<Task> work, Action clearInFlight, LiveSyncEventKind failureKind, string failureLogMessage)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, failureLogMessage);
            this.SetPendingResult(failureKind, ex.Message);
        }
        finally
        {
            clearInFlight();
        }
    }

    // Wandelt die vom Worker gelieferten Wire-Werte (z.B. "evening") in AvailabilityTag-Enumwerte
    // um - unbekannte/nicht (mehr) unterstützte Werte werden über FromWireValue/Where stillschweigend
    // übersprungen statt einen Fehler zu werfen (ein älterer Client könnte künftig neue Werte
    // liefern, mit denen dieser Client noch nichts anfangen kann). Gemeinsam genutzt von
    // PushOwnProfileAsync (eigenes Profil), TriggerBrowseAsync (Spieler-Browse) und
    // TriggerGroupBrowseAsync (Gruppen-Browse) - vorher an allen drei Stellen identisch dupliziert.
    private static List<AvailabilityTag> ParseAvailabilityTags(List<string>? wireValues) =>
        (wireValues ?? new List<string>())
            .Select(AvailabilityTagExtensions.FromWireValue)
            .Where(tag => tag is not null)
            .Select(tag => tag!.Value)
            .ToList();

    private static string DescribeHttpFailure(HttpStatusCode statusCode, string? reasonPhrase) =>
        string.IsNullOrEmpty(reasonPhrase) ? $"HTTP {(int)statusCode}" : $"HTTP {(int)statusCode} {reasonPhrase}";

    private static string BuildTokenKey(string characterName, string world) => $"{characterName}@{world}";

    private static string BuildProfileUrl(string world, string characterName) =>
        $"{WorkerBaseUrl}/profile/{Uri.EscapeDataString(world)}/{Uri.EscapeDataString(characterName)}";

    private static string BuildGroupUrl(string groupId) => $"{WorkerBaseUrl}/group/{Uri.EscapeDataString(groupId)}";

    // Aus PublishGroupAsync herausgelöst (siehe BuildPushRequestBody-Doc für denselben Grund) -
    // bewusst static, da außer editToken (Config-Lookup, braucht PartyService für den tokenKey)
    // alle Werte bereits als Parameter von PublishGroup/PublishGroupAsync durchgereicht werden.
    private static PutGroupRequestBody BuildPutGroupRequestBody(
        IReadOnlyList<(string World, string CharacterName)> members,
        bool visible,
        IReadOnlyCollection<AvailabilityTag> tags,
        string note,
        int wantedPlayerCount,
        IReadOnlyCollection<uint> targetSpellIds,
        string? editToken) =>
        new(
            members.Select(m => new GroupMemberWire(m.World, m.CharacterName)).ToList(),
            editToken,
            visible ? "listed" : "unlisted",
            tags.Select(tag => tag.ToWireValue()).ToList(),
            note,
            wantedPlayerCount,
            targetSpellIds.ToList());

    public void Dispose() => this.httpClient.Dispose();

    private sealed record PushRequestBody(
        string SpellBitmaskBase64,
        string? EditToken,
        string? Visibility,
        List<string>? AvailabilityTags,
        string? Note,
        int? WantedPlayerCount,
        List<uint>? TargetSpellIds);

    private sealed record PushResponseBody(
        string? EditToken,
        string? DataCenter,
        string? Visibility,
        List<string>? AvailabilityTags,
        string? Note,
        int? WantedPlayerCount,
        string? DiscordChannelUrl,
        string? DiscordChannelName);

    private sealed record FetchResponseBody(string? SpellBitmaskBase64);

    private sealed record GroupMemberWire(string World, string CharacterName);

    private sealed record PutGroupRequestBody(
        List<GroupMemberWire> Members,
        string? EditToken,
        string? Visibility,
        List<string>? AvailabilityTags,
        string? Note,
        int? WantedPlayerCount,
        List<uint>? TargetSpellIds);

    private sealed record PutGroupResponseBody(
        string? EditToken,
        string? Visibility,
        string? DiscordChannelUrl,
        string? DiscordChannelName);

    private sealed record BrowseResponseEntry(
        string? CharacterName,
        string? World,
        string? SpellBitmaskBase64,
        List<string>? AvailabilityTags,
        string? Note,
        int? WantedPlayerCount,
        List<uint>? TargetSpellIds,
        string? UpdatedAt);

    private sealed record GroupBrowseResponseMember(string? World, string? CharacterName, string? SpellBitmaskBase64);

    private sealed record GroupBrowseResponseEntry(
        string? GroupId,
        List<GroupBrowseResponseMember>? Members,
        List<string>? AvailabilityTags,
        string? Note,
        int? WantedPlayerCount,
        List<uint>? TargetSpellIds);
}
