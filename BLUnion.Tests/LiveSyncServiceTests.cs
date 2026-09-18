using System.Net;
using System.Reflection;
using System.Text;
using BLUnion.Models;
using BLUnion.Services;
using BLUnion.Tests.Fakes;
using Xunit;

namespace BLUnion.Tests;

// Deckt die HTTP-/JSON-Logik von LiveSyncService ab, die OHNE echten Dalamud-Laufzeitkontext
// testbar ist - siehe TEST_REPORT.md §2.4 für die Begründung, warum PartyService/
// LocalSpellUnlockService NICHT gefaked werden (IObjectTable.LocalPlayer.HomeWorld ist ein
// Lumina.Excel.RowRef<World>, dessen einziger Konstruktor ein echtes, mit echten Spieldateien
// befülltes ExcelModule braucht - per Reflection gegen die echte Dalamud.dll verifiziert, kein
// Fake kann das ohne laufendes Spiel umgehen). partyService/localSpellUnlockService werden deshalb
// bewusst als `null!` übergeben - alle hier getesteten Codepfade (TriggerBrowseAsync/
// TriggerGroupBrowseAsync, die reinen Body-Bau-Helfer, PublishGroups frühe Validierung) rühren
// diese beiden Felder nachweislich nie an.
//
// Bewusst NICHT abgedeckt (siehe obige Begründung): PushOwnProfileAsync/PublishGroupAsync/
// DeleteOwnProfileAsync/DeletePublishedGroupAsync als GANZE End-to-End-Abläufe - alle vier lesen
// zuerst partyService.GetLocalPlayerName()/GetLocalPlayerWorld(), bevor irgendeine HTTP-Logik
// läuft. Die darin enthaltene reine Body-Bau-Logik (BuildPushRequestBody/BuildPutGroupRequestBody)
// wurde dafür aus den *Async-Methoden herausgelöst und wird hier isoliert getestet.
public class LiveSyncServiceTests
{
    private static SpellDataService LoadFixtureSpellData()
    {
        var service = new SpellDataService(new TestPluginLog());
        service.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SpellData"));
        return service;
    }

    private static LiveSyncService CreateService(
        HttpMessageHandler handler, SpellDataService? spellDataService = null, Configuration? configuration = null)
    {
        var dataService = spellDataService ?? LoadFixtureSpellData();
        return new LiveSyncService(
            partyService: null!,
            spellDataService: dataService,
            localSpellUnlockService: null!,
            syncProvider: new ManualCodeSyncProvider(dataService),
            configuration: configuration ?? new Configuration(),
            log: new TestPluginLog(),
            httpMessageHandler: handler);
    }

    // Reines Test-Hilfsmittel, um Antwort-JSON mit einer gültigen Bitmaske zu bauen - bewusst KEIN
    // Aufruf von ManualCodeSyncProvider.EncodeBitmask/ToBase64Url (beide internal, ohne
    // InternalsVisibleTo von hier aus nicht erreichbar, siehe bestehende
    // ManualCodeSyncProviderTests, die aus demselben Grund nur die PUBLIC ExportToCode/ImportCode
    // nutzen). Bit-Packung/Base64Url-Transformation 1:1 wie dort, aber eigenständig - hier geht es
    // NICHT darum, diesen Algorithmus erneut zu testen (das tut ManualCodeSyncProviderTests
    // bereits), sondern nur darum, Test-Fixture-JSON zu erzeugen.
    private static string EncodeBitmaskForTest(IEnumerable<uint> learnedSpellIds, IReadOnlyList<uint> orderedSpellIds)
    {
        var bitmask = new byte[16];
        var learnedSet = learnedSpellIds.ToHashSet();
        for (var idx = 0; idx < orderedSpellIds.Count; idx++)
        {
            if (learnedSet.Contains(orderedSpellIds[idx]))
                bitmask[idx >> 3] |= (byte)(1 << (idx % 8));
        }

        return Convert.ToBase64String(bitmask).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static void SetOwnDataCenter(LiveSyncService service, string dataCenter)
    {
        SetProperty(service, nameof(LiveSyncService.LastKnownOwnProfile), new OwnProfileSnapshot
        {
            DataCenter = dataCenter,
            VisibleInGroupFinder = true,
            AvailabilityTags = [],
            Note = string.Empty,
            WantedPlayerCount = 0,
        });
    }

    private static object? InvokeInstance(object instance, string methodName, params object?[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Instanzmethode '{methodName}' nicht gefunden.");
        return method.Invoke(instance, args);
    }

    private static object? InvokeStatic(string methodName, params object?[] args)
    {
        var method = typeof(LiveSyncService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Statische Methode '{methodName}' nicht gefunden.");
        return method.Invoke(null, args);
    }

    private static T GetProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' nicht gefunden auf {instance.GetType()}.");
        return (T)property.GetValue(instance)!;
    }

    private static void SetProperty(object instance, string propertyName, object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' nicht gefunden auf {instance.GetType()}.");
        property.SetValue(instance, value);
    }

    // Wartet, bis das genannte private "...InFlight"-Feld wieder false ist (siehe try/finally in
    // jeder *Async-Methode) - funktioniert unabhängig davon, ob der Aufruf erfolgreich war oder
    // fehlschlug, da JEDE der sieben *Async-Methoden ihr eigenes Feld in genau einem finally-Block
    // zurücksetzt.
    private static async Task WaitWhileInFlightAsync(LiveSyncService service, string inFlightFieldName)
    {
        var field = typeof(LiveSyncService).GetField(inFlightFieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Feld '{inFlightFieldName}' nicht gefunden.");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while ((bool)field.GetValue(service)! && DateTime.UtcNow < deadline)
            await Task.Delay(5).ConfigureAwait(false);

        Assert.False((bool)field.GetValue(service)!, $"'{inFlightFieldName}' wurde nicht innerhalb des Timeouts zurückgesetzt.");
    }

    // Wartet, bis eine Bedingung zutrifft - gebraucht für den In-Flight-Guard-Test: der (echte,
    // über Task.Yield in FakeHttpMessageHandler asynchron losgeschickte) erste Request braucht
    // einen Moment, bis er tatsächlich beim Handler ankommt, bevor geprüft werden kann, dass kein
    // zweiter ausgelöst wurde.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(5).ConfigureAwait(false);

        Assert.True(condition(), "Bedingung wurde nicht innerhalb des Timeouts erfüllt.");
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // ----- BuildPushRequestBody (reine Body-Bau-Logik, siehe LiveSyncService.cs) -----

    [Fact]
    public void BuildPushRequestBody_IncludesAllPendingGroupFinderFields()
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(handler);

        service.SetGroupFinderVisibility(true);
        service.SetGroupFinderAvailabilityTags([AvailabilityTag.Evening, AvailabilityTag.Weekend]);
        service.SetGroupFinderNoteAndWantedPlayerCount("Testnotiz", 4);
        service.SetGroupFinderTargetSpellIds([1u, 2u]);

        var body = InvokeInstance(service, "BuildPushRequestBody", "bitmask==", "existing-token")!;

        Assert.Equal("bitmask==", GetProperty<string>(body, "SpellBitmaskBase64"));
        Assert.Equal("existing-token", GetProperty<string?>(body, "EditToken"));
        Assert.Equal("listed", GetProperty<string?>(body, "Visibility"));
        Assert.Equal(["evening", "weekend"], GetProperty<List<string>?>(body, "AvailabilityTags"));
        Assert.Equal("Testnotiz", GetProperty<string?>(body, "Note"));
        Assert.Equal(4, GetProperty<int?>(body, "WantedPlayerCount"));
        Assert.Equal([1u, 2u], GetProperty<List<uint>?>(body, "TargetSpellIds"));
    }

    [Fact]
    public void BuildPushRequestBody_WithoutPendingFields_LeavesThemNull()
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(handler);

        var body = InvokeInstance(service, "BuildPushRequestBody", "bitmask==", null)!;

        Assert.Null(GetProperty<string?>(body, "EditToken"));
        Assert.Null(GetProperty<string?>(body, "Visibility"));
        Assert.Null(GetProperty<List<string>?>(body, "AvailabilityTags"));
        Assert.Null(GetProperty<string?>(body, "Note"));
        Assert.Null(GetProperty<int?>(body, "WantedPlayerCount"));
        Assert.Null(GetProperty<List<uint>?>(body, "TargetSpellIds"));
    }

    // ----- BuildPutGroupRequestBody (reine Body-Bau-Logik) -----

    [Fact]
    public void BuildPutGroupRequestBody_IncludesMembersAndAllFields()
    {
        IReadOnlyList<(string World, string CharacterName)> members =
        [
            ("Gilgamesh", "Alice"),
            ("Gilgamesh", "Bob"),
        ];

        var body = InvokeStatic(
            "BuildPutGroupRequestBody",
            members, true, new List<AvailabilityTag> { AvailabilityTag.Morning }, "Gruppennotiz", 3, new List<uint> { 5u }, "grp-token")!;

        Assert.Equal("listed", GetProperty<string?>(body, "Visibility"));
        Assert.Equal("grp-token", GetProperty<string?>(body, "EditToken"));
        Assert.Equal("Gruppennotiz", GetProperty<string?>(body, "Note"));
        Assert.Equal(3, GetProperty<int?>(body, "WantedPlayerCount"));
        Assert.Equal(["morning"], GetProperty<List<string>?>(body, "AvailabilityTags"));
        Assert.Equal([5u], GetProperty<List<uint>?>(body, "TargetSpellIds"));

        var membersWire = ((System.Collections.IEnumerable)GetProperty<object>(body, "Members")!).Cast<object>().ToList();
        Assert.Equal(2, membersWire.Count);
        Assert.Equal("Gilgamesh", GetProperty<string>(membersWire[0], "World"));
        Assert.Equal("Alice", GetProperty<string>(membersWire[0], "CharacterName"));
        Assert.Equal("Bob", GetProperty<string>(membersWire[1], "CharacterName"));
    }

    [Fact]
    public void BuildPutGroupRequestBody_UnlistedWhenNotVisible()
    {
        IReadOnlyList<(string World, string CharacterName)> members = [("Gilgamesh", "Alice")];

        var body = InvokeStatic(
            "BuildPutGroupRequestBody", members, false, new List<AvailabilityTag>(), "", 0, new List<uint>(), null)!;

        Assert.Equal("unlisted", GetProperty<string?>(body, "Visibility"));
        Assert.Null(GetProperty<string?>(body, "EditToken"));
    }

    // ----- Pure statische Helfer -----

    [Fact]
    public void BuildProfileUrl_UrlEncodesWorldAndCharacterName()
    {
        var url = (string)InvokeStatic("BuildProfileUrl", "Gilgamesh", "Test Name")!;
        Assert.Equal("https://blunion-livesync.skysurfer101.workers.dev/profile/Gilgamesh/Test%20Name", url);
    }

    [Fact]
    public void BuildGroupUrl_UrlEncodesGroupId()
    {
        var url = (string)InvokeStatic("BuildGroupUrl", "group id")!;
        Assert.Equal("https://blunion-livesync.skysurfer101.workers.dev/group/group%20id", url);
    }

    [Fact]
    public void BuildTokenKey_CombinesCharacterNameAndWorld()
    {
        var key = (string)InvokeStatic("BuildTokenKey", "Alice", "Gilgamesh")!;
        Assert.Equal("Alice@Gilgamesh", key);
    }

    [Fact]
    public void DescribeHttpFailure_IncludesReasonPhraseWhenPresent()
    {
        var description = (string)InvokeStatic("DescribeHttpFailure", HttpStatusCode.BadRequest, "Bad Request")!;
        Assert.Equal("HTTP 400 Bad Request", description);
    }

    [Fact]
    public void DescribeHttpFailure_OmitsReasonPhraseWhenMissing()
    {
        var description = (string)InvokeStatic("DescribeHttpFailure", HttpStatusCode.InternalServerError, null)!;
        Assert.Equal("HTTP 500", description);
    }

    // ----- TriggerBrowseAsync (Spieler-Browse) -----

    [Fact]
    public async Task TriggerBrowse_Success_ParsesResponseIntoLastBrowseResults()
    {
        var spellDataService = LoadFixtureSpellData();
        var bitmaskBase64 = EncodeBitmaskForTest([1u, 3u], spellDataService.OrderedSpellIds);

        var responseJson = $$"""
        [
          {
            "characterName": "Aurelia",
            "world": "Gilgamesh",
            "spellBitmaskBase64": "{{bitmaskBase64}}",
            "availabilityTags": ["evening", "weekend", "not-a-real-tag"],
            "note": "Suche Gruppe",
            "wantedPlayerCount": 3,
            "targetSpellIds": [2, 4],
            "updatedAt": "2026-01-01T00:00:00.000Z"
          }
        ]
        """;

        using var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, responseJson));
        using var service = CreateService(handler, spellDataService);
        SetOwnDataCenter(service, "Aether");

        service.TriggerBrowse();
        await WaitWhileInFlightAsync(service, "browseInFlight");

        var entry = Assert.Single(service.LastBrowseResults);
        Assert.Equal("Aurelia", entry.CharacterName);
        Assert.Equal("Gilgamesh", entry.World);
        Assert.Equal(new HashSet<uint> { 1u, 3u }, entry.LearnedSpellIds);
        // "not-a-real-tag" wird von ParseAvailabilityTags stillschweigend übersprungen (siehe
        // Audit-Refactoring Runde 1) statt den ganzen Eintrag zu verwerfen.
        Assert.Equal([AvailabilityTag.Evening, AvailabilityTag.Weekend], entry.AvailabilityTags);
        Assert.Equal("Suche Gruppe", entry.Note);
        Assert.Equal(3, entry.WantedPlayerCount);
        Assert.Equal([2u, 4u], entry.TargetSpellIds);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://blunion-livesync.skysurfer101.workers.dev/profiles/browse?dataCenter=Aether",
            request.RequestUri!.ToString());
    }

    [Fact]
    public async Task TriggerBrowse_HttpError_SetsBrowseFailedPendingResult()
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var service = CreateService(handler);
        SetOwnDataCenter(service, "Aether");

        service.TriggerBrowse();
        await WaitWhileInFlightAsync(service, "browseInFlight");

        Assert.True(service.TryTakePendingResult(out var kind, out var detail));
        Assert.Equal(LiveSyncEventKind.BrowseFailed, kind);
        Assert.Contains("500", detail);
        Assert.Empty(service.LastBrowseResults);
    }

    [Fact]
    public async Task TriggerBrowse_HandlerThrows_SetsBrowseFailedPendingResultInsteadOfUnobservedException()
    {
        using var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulierter Netzwerkfehler"));
        using var service = CreateService(handler);
        SetOwnDataCenter(service, "Aether");

        service.TriggerBrowse();
        await WaitWhileInFlightAsync(service, "browseInFlight");

        Assert.True(service.TryTakePendingResult(out var kind, out var detail));
        Assert.Equal(LiveSyncEventKind.BrowseFailed, kind);
        Assert.Equal("simulierter Netzwerkfehler", detail);
    }

    [Fact]
    public async Task TriggerBrowse_SecondCallWhileInFlight_DoesNotIssueSecondRequest()
    {
        using var gate = new ManualResetEventSlim(false);
        using var handler = new FakeHttpMessageHandler(_ =>
        {
            Assert.True(gate.Wait(TimeSpan.FromSeconds(2)), "Gate wurde nicht rechtzeitig geöffnet.");
            return JsonResponse(HttpStatusCode.OK, "[]");
        });
        using var service = CreateService(handler);
        SetOwnDataCenter(service, "Aether");

        service.TriggerBrowse();
        service.TriggerBrowse(); // browseInFlight ist ab dem ERSTEN Aufruf synchron true - muss ignoriert werden

        // browseInFlight ist zwar schon synchron gesetzt, der Request selbst kommt aber erst nach
        // dem echten Task.Yield()-Umschaltpunkt (siehe FakeHttpMessageHandler) beim Handler an.
        await WaitUntilAsync(() => handler.Requests.Count > 0);
        Assert.Single(handler.Requests);

        gate.Set();
        await WaitWhileInFlightAsync(service, "browseInFlight");

        Assert.Single(handler.Requests);
    }

    // ----- TriggerGroupBrowseAsync (Gruppen-Browse) -----

    [Fact]
    public async Task TriggerGroupBrowse_Success_ParsesResponseIntoLastGroupBrowseResults()
    {
        var spellDataService = LoadFixtureSpellData();
        var bitmaskBase64 = EncodeBitmaskForTest([2u], spellDataService.OrderedSpellIds);

        var responseJson = $$"""
        [
          {
            "groupId": "grp-1",
            "members": [
              { "world": "Gilgamesh", "characterName": "Alice", "spellBitmaskBase64": "{{bitmaskBase64}}" },
              { "world": "Gilgamesh", "characterName": "Bob", "spellBitmaskBase64": null }
            ],
            "availabilityTags": ["morning"],
            "note": "Testgruppe",
            "wantedPlayerCount": 2,
            "targetSpellIds": [1, 3]
          }
        ]
        """;

        using var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, responseJson));
        using var service = CreateService(handler, spellDataService);
        SetOwnDataCenter(service, "Aether");

        service.TriggerGroupBrowse();
        await WaitWhileInFlightAsync(service, "groupBrowseInFlight");

        var entry = Assert.Single(service.LastGroupBrowseResults);
        Assert.Equal("grp-1", entry.GroupId);
        Assert.Equal(2, entry.Members.Count);
        Assert.Equal("Alice", entry.Members[0].CharacterName);
        Assert.Equal(new HashSet<uint> { 2u }, entry.Members[0].LearnedSpellIds);
        Assert.Null(entry.Members[1].LearnedSpellIds);
        Assert.Equal([AvailabilityTag.Morning], entry.AvailabilityTags);
        Assert.Equal("Testgruppe", entry.Note);
        Assert.Equal(2, entry.WantedPlayerCount);
        Assert.Equal([1u, 3u], entry.TargetSpellIds);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://blunion-livesync.skysurfer101.workers.dev/groups/browse?dataCenter=Aether",
            request.RequestUri!.ToString());
    }

    [Fact]
    public async Task TriggerGroupBrowse_HttpError_SetsGroupBrowseFailedPendingResult()
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var service = CreateService(handler);
        SetOwnDataCenter(service, "Aether");

        service.TriggerGroupBrowse();
        await WaitWhileInFlightAsync(service, "groupBrowseInFlight");

        Assert.True(service.TryTakePendingResult(out var kind, out var detail));
        Assert.Equal(LiveSyncEventKind.GroupBrowseFailed, kind);
        Assert.Contains("502", detail);
    }

    [Fact]
    public async Task TriggerGroupBrowse_HandlerThrows_SetsGroupBrowseFailedPendingResult()
    {
        using var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulierter Netzwerkfehler"));
        using var service = CreateService(handler);
        SetOwnDataCenter(service, "Aether");

        service.TriggerGroupBrowse();
        await WaitWhileInFlightAsync(service, "groupBrowseInFlight");

        Assert.True(service.TryTakePendingResult(out var kind, out var detail));
        Assert.Equal(LiveSyncEventKind.GroupBrowseFailed, kind);
        Assert.Equal("simulierter Netzwerkfehler", detail);
    }

    [Fact]
    public async Task TriggerGroupBrowse_SecondCallWhileInFlight_DoesNotIssueSecondRequest()
    {
        using var gate = new ManualResetEventSlim(false);
        using var handler = new FakeHttpMessageHandler(_ =>
        {
            Assert.True(gate.Wait(TimeSpan.FromSeconds(2)), "Gate wurde nicht rechtzeitig geöffnet.");
            return JsonResponse(HttpStatusCode.OK, "[]");
        });
        using var service = CreateService(handler);
        SetOwnDataCenter(service, "Aether");

        service.TriggerGroupBrowse();
        service.TriggerGroupBrowse(); // groupBrowseInFlight ist ab dem ERSTEN Aufruf synchron true

        await WaitUntilAsync(() => handler.Requests.Count > 0);
        Assert.Single(handler.Requests);

        gate.Set();
        await WaitWhileInFlightAsync(service, "groupBrowseInFlight");

        Assert.Single(handler.Requests);
    }

    // ----- PublishGroup: frühe Validierung (läuft VOR jedem partyService-Zugriff) -----

    [Fact]
    public void PublishGroup_TooManyMembers_SetsFailedResultWithoutHttpCall()
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var service = CreateService(handler);

        var members = Enumerable.Range(0, 9).Select(i => ("Gilgamesh", $"Member{i}")).ToList();
        service.PublishGroup(members, true, [], "note", 0, []);

        Assert.Empty(handler.Requests);
        Assert.True(service.TryTakePendingResult(out var kind, out var detail));
        Assert.Equal(LiveSyncEventKind.GroupPublishFailed, kind);
        Assert.Contains("Mitgliederanzahl", detail);
    }

    [Fact]
    public void PublishGroup_TooManyTargetSpellIds_SetsFailedResultWithoutHttpCall()
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var service = CreateService(handler);

        var members = new List<(string, string)> { ("Gilgamesh", "Alice") };
        var targetSpellIds = Enumerable.Range(0, 31).Select(i => (uint)i).ToList();
        service.PublishGroup(members, true, [], "note", 0, targetSpellIds);

        Assert.Empty(handler.Requests);
        Assert.True(service.TryTakePendingResult(out var kind, out var detail));
        Assert.Equal(LiveSyncEventKind.GroupPublishFailed, kind);
        Assert.Contains("Ziel-Spells", detail);
    }
}
