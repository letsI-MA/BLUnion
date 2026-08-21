using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BLUnion.Models;
using Dalamud.Plugin.Services;

namespace BLUnion.Services;

/// <summary>Art des zuletzt abgeschlossenen Live-Sync-Vorgangs (siehe <see cref="LiveSyncService.TryTakePendingResult"/>) -
/// MainWindow übersetzt das je nach <c>Kind</c> in eine lokalisierte Erfolgs-/Fehlermeldung
/// (siehe UiStrings.Key.LiveSync*).</summary>
public enum LiveSyncEventKind
{
    PushSucceeded,
    PushFailed,
    FetchFailed,
    DeleteSucceeded,
    DeleteFailed,

    /// <summary>Phase 2 (Gruppenfinder): GET /profiles/browse fehlgeschlagen (siehe
    /// LiveSyncService.TriggerBrowse/TriggerBrowseAsync).</summary>
    BrowseFailed,

    /// <summary>DEV-ONLY (siehe LiveSyncService.PublishDevTestProfiles): alle drei
    /// DevTestFixtures-Testprofile erfolgreich beim Worker veröffentlicht.</summary>
    DevTestProfilesPublished,

    /// <summary>DEV-ONLY: mindestens ein DevTestFixtures-Testprofil konnte nicht veröffentlicht
    /// werden (siehe LiveSyncService.PublishDevTestProfiles) - Detail nennt die betroffene(n)
    /// Fixture(n).</summary>
    DevTestProfilesFailed,

    /// <summary>Phase 2 (Gruppenfinder-Publish-Flow, NICHT zu verwechseln mit PushSucceeded/der
    /// Einzelprofil-Sichtbarkeit): PUT /group/:groupId für die eigene Gruppen-Listung erfolgreich
    /// (Neuanlage ODER Update, siehe <see cref="LiveSyncService.PublishGroup"/>).</summary>
    GroupPublishSucceeded,

    /// <summary>Gegenstück zu <see cref="GroupPublishSucceeded"/> - Detail nennt den HTTP-/
    /// Netzwerkfehler bzw. bei einer ungültigen Mitgliederauswahl den Validierungsgrund.</summary>
    GroupPublishFailed,

    /// <summary>DELETE /group/:groupId für die eigene Gruppen-Listung erfolgreich (siehe
    /// <see cref="LiveSyncService.DeletePublishedGroup"/>).</summary>
    GroupUnpublishSucceeded,

    /// <summary>Gegenstück zu <see cref="GroupUnpublishSucceeded"/>.</summary>
    GroupUnpublishFailed,

    /// <summary>Phase 2 (Gruppenfinder, Anzeige FREMDER Gruppen): GET /groups/browse
    /// fehlgeschlagen (siehe <see cref="LiveSyncService.TriggerGroupBrowse"/>/TriggerGroupBrowseAsync) -
    /// analog zu <see cref="BrowseFailed"/> für Einzelprofile, aber ein eigener, paralleler
    /// Datenpfad (siehe LastGroupBrowseResults-Doc).</summary>
    GroupBrowseFailed,
}

/// <summary>
/// Sync-Option "Live-Sync" (Phase 1 - siehe worker/-Ordner im Repo für das Cloudflare-Worker-
/// Backend): synchronisiert den eigenen Spell-Status automatisch mit einem externen Server und
/// ruft die Profile anderer Blue-Mage-Party-Mitglieder darüber ab. Ersetzt für den Alltagsfall
/// "gemeinsam in einer Party" den manuellen "BLU:"-Code-Austausch (<see cref="ManualCodeSyncProvider"/>),
/// OHNE diesen zu ersetzen - Option A bleibt als Fallback (Freunde ohne Live-Sync, Web Companion)
/// bestehen und teilt sich mit Live-Sync dieselbe Datenhaltung (siehe unten).
///
/// WICHTIG - Cross-Thread-Zugriff: <see cref="System.Net.Http.HttpClient"/>-Aufrufe laufen async
/// im Threadpool und dürfen NIE den ImGui-Draw-Thread blockieren (siehe Aufgabenstellung). Es gab
/// im Projekt bisher KEIN etabliertes Muster dafür (Feature 3, der Chat-Hook, ist rein
/// synchron/ereignisbasiert, keine Netzwerk-/Threadpool-Arbeit) - das Muster hier ist daher neu:
/// alle Methoden, die Netzwerkzugriffe auslösen (<see cref="PushOwnProfile"/>, <see cref="TriggerFetch()"/>,
/// <see cref="DeleteOwnProfile"/>), feuern nur ein "fire-and-forget" <see cref="Task"/> ab und
/// kehren sofort zurück; das Ergebnis landet in einem einzelnen, lock-geschützten "Briefkasten"-
/// Feld und wird von MainWindow.Draw() im nächsten Frame über <see cref="TryTakePendingResult"/>
/// abgeholt (genau EIN Slot, wie das bestehende MainWindow.lastError-Muster - keine Historie
/// nötig, da immer nur eine Meldung gleichzeitig angezeigt wird).
///
/// Abgerufene Party-Profile fließen direkt in <see cref="ISyncProvider.PublishLocalStatus"/> des
/// bestehenden syncProvider ein (siehe FetchPartyMemberProfilesAsync) - dessen interne Ablage
/// dedupliziert bereits nach CharacterName (letzter Stand gewinnt, siehe ManualCodeSyncProvider),
/// Live-Sync-Ergebnisse und manuell importierte "BLU:"-Codes landen dadurch ohne zusätzliche
/// Merge-Logik im selben Comparison-Tab-Datenbestand.
///
/// PHASE 2 - GRUPPENFINDER: erweitert dasselbe Profil (siehe worker/src/index.ts) um
/// visibility/availabilityTags/note/wantedPlayerCount, KEIN separates Profil/Login - Sichtbarkeit
/// im Gruppenfinder setzt daher zwingend voraus, dass bereits (mindestens einmal) erfolgreich
/// gepusht wurde (siehe <see cref="pendingVisibility"/> ff. sowie <see cref="LastKnownOwnProfile"/>).
/// Der Browse-Abruf (<see cref="TriggerBrowse()"/>) folgt demselben fire-and-forget-/Briefkasten-
/// Muster wie Push/Fetch/Delete oben, siehe <see cref="LastBrowseResults"/>.
/// </summary>
public sealed class LiveSyncService : IDisposable
{
    // TODO: nach dem ersten "wrangler deploy" im worker/-Ordner (siehe worker/README.md) hier die
    // dabei ausgegebene *.workers.dev-URL eintragen. Ohne gültige URL schlagen alle Live-Sync-
    // Aufrufe fehl (HttpRequestException, siehe catch-Blöcke unten) - das Plugin bleibt aber voll
    // nutzbar, Live-Sync ist rein additiv/opt-in (siehe Configuration.LiveSyncEnabled Default false).
    private const string WorkerBaseUrl = "https://blunion-livesync.skysurfer101.workers.dev";

    /// <summary>Sanftes Party-Profil-Polling (siehe FetchPartyMemberProfilesAsync/Tick) - läuft
    /// NUR, solange Live-Sync aktiv ist UND mindestens ein fremdes Blue-Mage-Party-Mitglied
    /// erkannt wurde (siehe Aufgabenstellung: kein Polling solo/ohne Party).</summary>
    private static readonly TimeSpan PartyPollInterval = TimeSpan.FromSeconds(60);

    /// <summary>Wie oft <see cref="LocalSpellUnlockService.GetLearnedSpellIds"/> lokal auf neu
    /// gelernte Spells geprüft wird (siehe TickPushDiff). Das Projekt hat (noch) KEINEN
    /// ereignisbasierten "Spell gelernt"-Hook, an den sich hier andocken ließe (siehe Klassendoc)
    /// - dieser Wert ist der pragmatische Ersatz dafür: kein echtes Netzwerk-Polling (das prüft
    /// nur lokal, ohne HTTP-Aufruf, siehe unten), aber auch kein Aufruf bei JEDEM ImGui-Frame
    /// (60+ mal/Sekunde), da GetLearnedSpellIds() das komplette AozAction-Sheet durchläuft und das
    /// bei jeder einzelnen Bildwiederholung unnötige CPU-Last wäre. Ein tatsächlicher Server-Push
    /// (siehe PushOwnProfile) passiert weiterhin ausschließlich bei einer ECHTEN Änderung der
    /// gelernten Spells - das hier ist nur das Intervall der (billigen) lokalen Prüfung selbst.</summary>
    private static readonly TimeSpan LocalLearnedSpellCheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>DefaultIgnoreCondition.WhenWritingNull ist Phase-2-Pflicht (nicht nur Kosmetik):
    /// die Gruppenfinder-Felder in <see cref="PushRequestBody"/> sind nullable, weil der Worker
    /// ein FEHLENDES Feld ("undefined" im JSON) von einem explizit auf null gesetzten Feld
    /// unterscheidet (siehe worker/src/index.ts handlePut - "=== undefined" prüft NUR auf
    /// Abwesenheit, ein explizites JSON-"null" würde stattdessen als ungültiger Wert mit 400
    /// abgelehnt). Ohne diese Option würde System.Text.Json "propertyName": null mitschicken,
    /// sobald ein Feld lokal noch nie gesetzt wurde (siehe TickPushDiff: der rein automatische
    /// Spell-Diff-Push lässt alle vier Gruppenfinder-Felder bewusst ungesetzt).</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly PartyService partyService;
    private readonly SpellDataService spellDataService;
    private readonly LocalSpellUnlockService localSpellUnlockService;
    private readonly ISyncProvider syncProvider;
    private readonly Configuration configuration;
    private readonly IPluginLog log;

    private readonly object resultLock = new();
    private LiveSyncEventKind? pendingResultKind;
    private string? pendingResultDetail;

    private volatile bool pushInFlight;
    private volatile bool fetchInFlight;
    private volatile bool deleteInFlight;

    /// <summary>Analog zu <see cref="pushInFlight"/>/<see cref="deleteInFlight"/>, aber für die
    /// NEUE Gruppen-Veröffentlichung (siehe <see cref="PublishGroup"/>/<see cref="DeletePublishedGroup"/>) -
    /// eigene Felder statt die bestehenden mitzubenutzen, weil beide Vorgänge unabhängig
    /// voneinander (und potenziell gleichzeitig, z.B. Einzelprofil-Push durch TickPushDiff
    /// während der Spieler auf "Gruppe veröffentlichen" klickt) laufen können.</summary>
    private volatile bool groupPublishInFlight;
    private volatile bool groupDeleteInFlight;

    private DateTimeOffset? lastLocalLearnedSpellCheckAt;
    private HashSet<uint>? lastPushedLearnedSpellIds;

    private DateTimeOffset? lastPartyPollAt;
    private List<string>? lastKnownBlueMagePartyMemberNames;

    /// <summary>Lokal GEWÜNSCHTER Gruppenfinder-Stand für die vier Zusatzfelder, gesetzt über
    /// <see cref="SetGroupFinderVisibility"/>/<see cref="SetGroupFinderAvailabilityTags"/>/
    /// <see cref="SetGroupFinderNoteAndWantedPlayerCount"/> (siehe MainWindow.DrawGroupFinderTab).
    /// Bewusst NULL, solange der Spieler in dieser Session noch KEIN einziges Mal etwas am
    /// Gruppenfinder geändert hat - <see cref="PushOwnProfileAsync"/> lässt ein null-Feld dann
    /// im JSON komplett weg (siehe JsonOptions-Doc oben), wodurch der Worker den zuletzt
    /// GESPEICHERTEN Wert unangetastet lässt. OHNE dieses Nullable-Muster (z.B. mit hart
    /// codierten Defaults wie "unlisted"/leere Liste) würde der erste rein automatische
    /// Spell-Diff-Push nach einem Plugin-Neustart ein zuvor über mehrere Sessions gepflegtes
    /// "listed"-Profil samt Tags/Notiz stillschweigend auf die Defaults zurücksetzen - genau das
    /// verhindert diese Konstruktion.</summary>
    private string? pendingVisibility;
    private List<string>? pendingAvailabilityTags;
    private string? pendingNote;
    private int? pendingWantedPlayerCount;

    private volatile bool browseInFlight;

    /// <summary>Analog zu <see cref="browseInFlight"/>, aber für den EIGENSTÄNDIGEN, parallelen
    /// Gruppen-Datenpfad (siehe <see cref="TriggerGroupBrowse"/>/<see cref="LastGroupBrowseResults"/>) -
    /// eigenes Feld, damit ein Einzelprofil-Browse und ein Gruppen-Browse unabhängig voneinander
    /// (und ggf. gleichzeitig, siehe MainWindow.Draw() - beide werden beim Öffnen des Tabs UND
    /// über denselben "Aktualisieren"-Button gemeinsam ausgelöst) laufen können.</summary>
    private volatile bool groupBrowseInFlight;

    /// <summary>DEV-ONLY (siehe <see cref="PublishDevTestProfiles"/>/MainWindow-Dev-Tool-Button):
    /// Edit-Tokens der drei DevTestFixtures-Testprofile, Key wie <see cref="BuildTokenKey"/>
    /// ("CharacterName@World"). Bewusst NICHT in <see cref="Configuration.LiveSyncEditTokens"/>
    /// (das ist für den eigenen ECHTEN Charakter gedacht - Testprofile dort reinzumischen würde
    /// beim nächsten echten Push-Versuch für einen Charakter namens "Alice"/"Bob"/"Charles"
    /// falsche Tokens liefern) und bewusst NICHT auf Platte persistiert (rein In-Memory, lebt nur
    /// für die Dauer dieser Plugin-Session) - genau deshalb rein In-Memory: erlaubt dem
    /// vorgeschalteten best-effort-DELETE bei einem erneuten Klick, das jeweils VOR-herige
    /// Testprofil tatsächlich mit dem passenden Token zu löschen (statt nur mit 403 zu scheitern),
    /// damit der Button beliebig oft hintereinander klickbar bleibt, ohne in einen
    /// 409-Conflict durch ein liegen gebliebenes altes Token zu laufen.</summary>
    private readonly Dictionary<string, string> devTestProfileEditTokens = new();

    private volatile bool devPublishInFlight;

    /// <summary>Zuletzt vom Worker bestätigter Stand des eigenen Profils (siehe
    /// <see cref="OwnProfileSnapshot"/>-Doc) - null, bis der allererste Push in dieser (oder
    /// einer früheren, aber Live-Sync speichert das bewusst nicht persistent, siehe
    /// Configuration.cs) Session erfolgreich war. MainWindow zeigt in der Zwischenzeit einen
    /// "wird ermittelt..."-Platzhalter statt eines leeren/falschen Data-Center-Werts an.</summary>
    public OwnProfileSnapshot? LastKnownOwnProfile { get; private set; }

    /// <summary>RAW/ungefilterte Rohdaten des letzten erfolgreichen <see cref="TriggerBrowse()"/>-
    /// Aufrufs (siehe TriggerBrowseAsync) - leer, solange noch nie erfolgreich abgerufen wurde.
    /// KANN den eigenen Charakter enthalten (falls im Gruppenfinder sichtbar geschaltet) - wird
    /// hier bewusst NICHT herausgefiltert, weil das einen Namensabgleich über
    /// <see cref="PartyService.GetLocalPlayerName"/> bräuchte, der als Dalamud-Service-API NICHT
    /// aus dem asynchronen HTTP-Callback heraus aufgerufen werden darf (Cross-Thread, siehe
    /// TriggerBrowseAsync-Kommentar - führte in einer früheren Version zur Laufzeit-Exception
    /// "Not on main thread!"). Die Herausfilterung des eigenen Eintrags passiert stattdessen erst
    /// beim Rendern in MainWindow.DrawGroupFinderTab, dort garantiert auf dem Framework-Thread.</summary>
    public IReadOnlyList<GroupFinderEntry> LastBrowseResults { get; private set; } = Array.Empty<GroupFinderEntry>();

    /// <summary>RAW/ungefilterte Rohdaten des letzten erfolgreichen <see cref="TriggerGroupBrowse()"/>-
    /// Aufrufs (siehe TriggerGroupBrowseAsync) - leer, solange noch nie erfolgreich abgerufen
    /// wurde. EIGENSTÄNDIGER, zu <see cref="LastBrowseResults"/> (Einzelprofile) PARALLELER
    /// Datenpfad - Gruppen und Einzelprofile bleiben zwei getrennte Listen im UI (siehe
    /// MainWindow.DrawGroupFinderTab), keine Zusammenführung. Kann Gruppen enthalten, die den
    /// eigenen Charakter als Mitglied listen - wird hier bewusst NICHT herausgefiltert, aus
    /// demselben Cross-Thread-Grund wie bei LastBrowseResults (siehe dortigen Kommentar).</summary>
    public IReadOnlyList<GroupFinderGroupEntry> LastGroupBrowseResults { get; private set; } = Array.Empty<GroupFinderGroupEntry>();

    public LiveSyncService(
        PartyService partyService,
        SpellDataService spellDataService,
        LocalSpellUnlockService localSpellUnlockService,
        ISyncProvider syncProvider,
        Configuration configuration,
        IPluginLog log)
    {
        this.partyService = partyService;
        this.spellDataService = spellDataService;
        this.localSpellUnlockService = localSpellUnlockService;
        this.syncProvider = syncProvider;
        this.configuration = configuration;
        this.log = log;
    }

    /// <summary>Muss aus MainWindow.Draw() JEDEN Frame aufgerufen werden (unabhängig vom aktuell
    /// sichtbaren Tab, damit Push/Fetch auch laufen, während der Sync-/Settings-Tab gar nicht
    /// offen ist) - selbst rein lokal/zeitgesteuert und niemals blockierend, siehe Klassendoc.
    /// Löst bei Bedarf höchstens EINEN neuen Push und/oder EINEN neuen Fetch als Hintergrund-Task
    /// aus (siehe <see cref="pushInFlight"/>/<see cref="fetchInFlight"/> - keine überlappenden
    /// Requests für denselben Vorgang).</summary>
    public void Tick()
    {
        if (!this.configuration.LiveSyncEnabled)
            return;

        this.TickPushDiff();
        this.TickPartyPoll();
    }

    /// <summary>(a) aus der Aufgabenstellung: läuft implizit beim ersten <see cref="Tick"/> nach
    /// Aktivieren von <see cref="Configuration.LiveSyncEnabled"/>, weil <see cref="lastPushedLearnedSpellIds"/>
    /// dann noch null ist (siehe unten) - kein gesonderter Aufruf beim Checkbox-Klick nötig.
    /// (b): derselbe Mechanismus erkennt auch jeden späteren, tatsächlich neu gelernten Spell.</summary>
    private void TickPushDiff()
    {
        var now = DateTimeOffset.UtcNow;
        if (this.lastLocalLearnedSpellCheckAt is { } lastCheck && now - lastCheck < LocalLearnedSpellCheckInterval)
            return;

        this.lastLocalLearnedSpellCheckAt = now;

        var currentLearnedIds = this.localSpellUnlockService.GetLearnedSpellIds();
        if (this.lastPushedLearnedSpellIds is not null && currentLearnedIds.SetEquals(this.lastPushedLearnedSpellIds))
            return; // Kein neuer Spell seit dem letzten Push - kein unnötiger Netzwerk-Request (siehe Aufgabenstellung: (c) kein Timer/Polling für den eigentlichen Push).

        this.lastPushedLearnedSpellIds = currentLearnedIds;
        this.PushOwnProfile();
    }

    /// <summary>Löst bei Party-Änderung sofort und ansonsten höchstens alle <see cref="PartyPollInterval"/>
    /// einen Fetch aus - aber nur, solange mindestens ein fremdes Blue-Mage-Party-Mitglied
    /// bekannt ist (siehe Aufgabenstellung: kein Polling solo/ohne Party).</summary>
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

    /// <summary>Stößt einen Push des eigenen Profils an (fire-and-forget, siehe Klassendoc) -
    /// öffentlich, da MainWindow das auch direkt braucht, falls künftig ein manueller "Jetzt
    /// synchronisieren"-Button dazukommt; aktuell nur intern von <see cref="TickPushDiff"/>
    /// genutzt.</summary>
    public void PushOwnProfile()
    {
        if (this.pushInFlight)
            return;

        this.pushInFlight = true;
        _ = this.PushOwnProfileAsync();
    }

    private async Task PushOwnProfileAsync()
    {
        try
        {
            var localName = this.partyService.GetLocalPlayerName();
            var localWorld = this.partyService.GetLocalPlayerWorld();

            if (string.IsNullOrEmpty(localName) || string.IsNullOrEmpty(localWorld))
            {
                // Kein vollständig geladener eigener Charakter (z.B. Login-Screen/Zonenwechsel) -
                // stiller Abbruch statt Fehlermeldung, TickPushDiff versucht es beim nächsten
                // Intervall automatisch erneut.
                return;
            }

            var learnedIds = this.localSpellUnlockService.GetLearnedSpellIds();
            var bitmaskBase64 = ManualCodeSyncProvider.ToBase64Url(
                ManualCodeSyncProvider.EncodeBitmask(this.spellDataService, learnedIds));

            var tokenKey = BuildTokenKey(localName, localWorld);
            this.configuration.LiveSyncEditTokens.TryGetValue(tokenKey, out var existingToken);

            // pendingVisibility/-AvailabilityTags/-Note/-WantedPlayerCount sind null, solange der
            // Gruppenfinder in dieser Session noch nicht angefasst wurde (siehe Felddoc oben) -
            // JsonOptions lässt null-Felder beim Serialisieren komplett weg, der Worker behält in
            // dem Fall den zuletzt gespeicherten Wert bei (siehe worker/src/index.ts handlePut).
            var requestBody = new PushRequestBody(
                bitmaskBase64,
                existingToken,
                this.pendingVisibility,
                this.pendingAvailabilityTags,
                this.pendingNote,
                this.pendingWantedPlayerCount);
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
                // Nur beim ALLERERSTEN Push für diesen Charakter vom Server mitgeschickt (siehe
                // worker/README.md) - sofort persistieren: der Server gibt den Klartext-Token nur
                // dieses eine Mal zurück, ohne Speichern wäre das Profil nach einem Plugin-/
                // Spiel-Neustart nicht mehr bearbeitbar/löschbar.
                this.configuration.LiveSyncEditTokens[tokenKey] = responseBody!.EditToken!;
                this.configuration.Save();
            }

            // Phase 2: die Response spiegelt den tatsächlich gespeicherten (nicht nur den lokal
            // gewünschten) Stand wider - dataCenter ist die einzige Quelle dafür im gesamten
            // Plugin (siehe OwnProfileSnapshot-Doc: KEINE zweite World->DC-Herleitung im C#-Code).
            if (!string.IsNullOrEmpty(responseBody?.DataCenter))
            {
                this.LastKnownOwnProfile = new OwnProfileSnapshot
                {
                    DataCenter = responseBody!.DataCenter!,
                    VisibleInGroupFinder = responseBody.Visibility == "listed",
                    AvailabilityTags = (responseBody.AvailabilityTags ?? new List<string>())
                        .Select(AvailabilityTagExtensions.FromWireValue)
                        .Where(tag => tag is not null)
                        .Select(tag => tag!.Value)
                        .ToList(),
                    Note = responseBody.Note ?? string.Empty,
                    WantedPlayerCount = responseBody.WantedPlayerCount ?? 0,
                };
            }

            this.SetPendingResult(LiveSyncEventKind.PushSucceeded, null);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "LiveSyncService: unerwarteter Fehler beim Push des eigenen Profils.");
            this.SetPendingResult(LiveSyncEventKind.PushFailed, ex.Message);
        }
        finally
        {
            this.pushInFlight = false;
        }
    }

    /// <summary>Stößt einen Abruf ALLER aktuellen fremden Blue-Mage-Party-Mitglieder an (siehe
    /// Klassendoc) - öffentlich für einen möglichen manuellen "Jetzt aktualisieren"-Button;
    /// intern von <see cref="TickPartyPoll"/> genutzt.</summary>
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
                    continue; // World (noch) nicht ermittelbar (z.B. Objekt noch nicht vollständig geladen) - überspringen statt Fehler.

                try
                {
                    var url = BuildProfileUrl(member.World, member.Name);
                    using var response = await this.httpClient.GetAsync(url).ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.NotFound)
                        continue; // Erwarteter Fall (Spieler ohne Live-Sync, siehe Aufgabenstellung) - kein Fehler.

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

                    // Dedupliziert automatisch nach CharacterName (letzter Stand gewinnt, siehe
                    // Klassendoc) - läuft mit manuell importierten "BLU:"-Codes im selben
                    // Comparison-Tab-Datenbestand zusammen, ohne doppelte Einträge.
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

    /// <summary>Setzt NUR lokal die gewünschte Gruppenfinder-Sichtbarkeit (pendingVisibility) -
    /// pusht NICHT mehr selbst, das übernimmt jetzt ausschließlich der explizite
    /// "Jetzt veröffentlichen"-Button in MainWindow.DrawGroupFinderTab über <see cref="PushOwnProfile"/>.</summary>
    public void SetGroupFinderVisibility(bool visible)
    {
        this.pendingVisibility = visible ? "listed" : "unlisted";
    }

    /// <summary>Setzt NUR lokal die gewünschten Verfügbarkeits-Tags (komplette Menge, nicht nur
    /// der geänderte Tag - siehe MainWindow: einfacher, bei jedem Toggle-Klick den kompletten
    /// aktuellen Auswahlstand zu übergeben, als ein Diff zu bilden) - pusht NICHT mehr selbst,
    /// analog zu <see cref="SetGroupFinderVisibility"/>: der Push passiert erst über den
    /// "Jetzt veröffentlichen"-Button.</summary>
    public void SetGroupFinderAvailabilityTags(IReadOnlyCollection<AvailabilityTag> tags)
    {
        this.pendingAvailabilityTags = tags.Select(tag => tag.ToWireValue()).ToList();
    }

    /// <summary>Setzt NUR lokal Notiz UND gewünschte Mitspieleranzahl gemeinsam - pusht NICHT
    /// mehr selbst, das übernimmt jetzt ausschließlich der explizite "Jetzt veröffentlichen"-
    /// Button. Beide Werte zusammen statt einzeln, weil ohnehin immer beide lokal bekannt sind
    /// (MainWindow hält beide Puffer).</summary>
    public void SetGroupFinderNoteAndWantedPlayerCount(string note, int wantedPlayerCount)
    {
        this.pendingNote = note;
        this.pendingWantedPlayerCount = wantedPlayerCount;
    }

    /// <summary>Stößt einen Abruf aller aktuell im Gruppenfinder sichtbaren Profile auf dem
    /// eigenen Data Center an (fire-and-forget, siehe Klassendoc) - von MainWindow beim Öffnen
    /// des Gruppenfinder-Tabs UND über den "Aktualisieren"-Button aufgerufen (siehe
    /// Aufgabenstellung: NICHT bei jedem Draw-Call). No-Op, solange <see cref="LastKnownOwnProfile"/>
    /// noch unbekannt ist (kein eigener Push bisher erfolgt) - ohne ein bekanntes Data Center gibt
    /// es nichts, wonach gefragt werden könnte; MainWindow zeigt in dem Fall ohnehin den
    /// "wird ermittelt..."-Platzhalter statt des Tab-Inhalts an.</summary>
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

    private async Task TriggerBrowseAsync(string dataCenter)
    {
        try
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

            // ACHTUNG Cross-Thread: ab hier läuft der Code (wegen ConfigureAwait(false) oben) auf
            // einem Threadpool-Thread, NICHT auf dem Dalamud-Framework-Thread - siehe Klassendoc
            // "WICHTIG - Cross-Thread-Zugriff". Deshalb wird hier BEWUSST NICHT
            // this.partyService.GetLocalPlayerName() (oder eine andere Dalamud-Service-API)
            // aufgerufen, um den eigenen Charakter herauszufiltern - das hat in einer früheren
            // Version dieser Methode zur Laufzeit-Exception "Not on main thread!" geführt
            // (ObjectTable hat Thread-Affinität). Diese Methode legt deshalb NUR die rohen,
            // reinen Datenstrukturen in LastBrowseResults ab (inkl. des eigenen Charakters, falls
            // sichtbar geschaltet); das Herausfiltern des eigenen Eintrags passiert stattdessen
            // in MainWindow.DrawGroupFinderTab bei der Anzeige - dort läuft der Code garantiert
            // auf dem Framework-Thread (siehe MainWindow.Draw()/Dalamud.UiBuilder.Draw), Zugriffe
            // auf partyService sind dort unproblematisch.
            //
            // Bewusst pro Eintrag einzeln try/catch (statt eines LINQ-Selects über die ganze
            // Liste, das bei EINEM kaputten Eintrag - z.B. einer korrupten Bitmaske - die
            // gesamte Enumeration abbrechen und dadurch ALLE Treffer verwerfen würde) - analog
            // zum bereits etablierten Muster in FetchPartyMemberProfilesAsync oben: ein einzelner
            // fehlerhafter fremder Eintrag soll die übrigen, gültigen Gruppenfinder-Treffer nicht
            // mit sich reißen.
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.CharacterName) || string.IsNullOrEmpty(entry.SpellBitmaskBase64))
                    continue;

                try
                {
                    // Dieselbe kanonische Bit-Mapping-Implementierung wie beim Party-Fetch oben
                    // (siehe FetchPartyMemberProfilesAsync) - NICHT dupliziert.
                    var learnedIds = ManualCodeSyncProvider.DecodeBitmask(
                        this.spellDataService, ManualCodeSyncProvider.FromBase64Url(entry.SpellBitmaskBase64));

                    results.Add(new GroupFinderEntry
                    {
                        CharacterName = entry.CharacterName,
                        World = entry.World ?? string.Empty,
                        LearnedSpellIds = learnedIds,
                        AvailabilityTags = (entry.AvailabilityTags ?? new List<string>())
                            .Select(AvailabilityTagExtensions.FromWireValue)
                            .Where(tag => tag is not null)
                            .Select(tag => tag!.Value)
                            .ToList(),
                        Note = entry.Note ?? string.Empty,
                        WantedPlayerCount = entry.WantedPlayerCount ?? 0,
                    });
                }
                catch (Exception exEntry)
                {
                    this.log.Debug(exEntry, $"LiveSyncService: Gruppenfinder-Eintrag für \"{entry.CharacterName}\" übersprungen (ungültige Daten).");
                }
            }

            this.LastBrowseResults = results;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "LiveSyncService: unerwarteter Fehler beim Abrufen des Gruppenfinders.");
            this.SetPendingResult(LiveSyncEventKind.BrowseFailed, ex.Message);
        }
        finally
        {
            this.browseInFlight = false;
        }
    }

    /// <summary>Stößt einen Abruf aller aktuell im Gruppenfinder sichtbaren GRUPPEN-Listungen auf
    /// dem eigenen Data Center an (fire-and-forget, siehe Klassendoc) - 1:1 nach dem Muster von
    /// <see cref="TriggerBrowse"/> für Einzelprofile (gleiche in-flight-Absicherung, gleiches
    /// dataCenter aus <see cref="LastKnownOwnProfile"/>), aber ein EIGENSTÄNDIGER, paralleler
    /// Datenpfad (siehe <see cref="LastGroupBrowseResults"/>-Doc) - von MainWindow beim Öffnen des
    /// Gruppenfinder-Tabs UND über denselben "Aktualisieren"-Button wie TriggerBrowse aufgerufen
    /// (kein zweiter Button nötig). No-Op, solange LastKnownOwnProfile noch unbekannt ist, aus
    /// demselben Grund wie bei TriggerBrowse.</summary>
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

    private async Task TriggerGroupBrowseAsync(string dataCenter)
    {
        try
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

            // Cross-Thread-Hinweis wie bei TriggerBrowseAsync oben (siehe dortigen Kommentar) -
            // gilt hier identisch: kein Aufruf von partyService/anderen Dalamud-Service-APIs in
            // dieser Methode. Ebenso pro Eintrag einzeln try/catch, damit eine einzelne kaputte
            // Gruppen-Listung (z.B. korrupte Mitgliederdaten) nicht alle übrigen Treffer mit sich reißt.
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
                            // null bleibt null (siehe GroupFinderGroupMember.LearnedSpellIds-Doc) -
                            // NUR bei einem tatsächlich vorhandenen Bitmaskenstring dekodiert,
                            // NICHT versucht zu dekodieren, wenn der Worker kein Einzelprofil zu
                            // diesem Mitglied gefunden hat (siehe worker handleGroupsBrowse).
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
                        AvailabilityTags = (entry.AvailabilityTags ?? new List<string>())
                            .Select(AvailabilityTagExtensions.FromWireValue)
                            .Where(tag => tag is not null)
                            .Select(tag => tag!.Value)
                            .ToList(),
                        Note = entry.Note ?? string.Empty,
                        WantedPlayerCount = entry.WantedPlayerCount ?? 0,
                    });
                }
                catch (Exception exEntry)
                {
                    this.log.Debug(exEntry, $"LiveSyncService: Gruppen-Eintrag \"{entry.GroupId}\" übersprungen (ungültige Daten).");
                }
            }

            this.LastGroupBrowseResults = results;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "LiveSyncService: unerwarteter Fehler beim Abrufen der Gruppen-Listungen.");
            this.SetPendingResult(LiveSyncEventKind.GroupBrowseFailed, ex.Message);
        }
        finally
        {
            this.groupBrowseInFlight = false;
        }
    }

    /// <summary>DEV-ONLY (siehe MainWindow: der Button steht unkonditioniert neben den übrigen
    /// Dev-Fixture-Buttons im Sync-Tab, siehe dortiger Kommentar "absichtlich dauerhaft hier" -
    /// es gibt in diesem Projekt kein #if DEBUG/Konfigurationsflag für Dev-Tools). Veröffentlicht
    /// Alice/Bob/Charles aus <see cref="DevTestFixtures"/> als ECHTE, sichtbare Testprofile beim
    /// Live-Sync-Worker, alle auf dem Data Center des AKTUELL eingeloggten lokalen Charakters
    /// (sonst tauchen sie beim Browsen dort gar nicht auf) - erlaubt, den Gruppenfinder-Browse-/
    /// "In Vergleich aufnehmen"-Flow zu testen, ohne einen dritten echten Mitspieler zu brauchen.
    /// Alice+Charles simulieren dabei eine bestehende Zweier-Gruppe, die noch 1 weitere Person
    /// sucht; Bob sucht allein und ist für mehr Mitspieler offen (siehe PublishDevTestProfilesAsync).</summary>
    public void PublishDevTestProfiles()
    {
        if (this.devPublishInFlight)
            return;

        // localWorld MUSS synchron HIER auf dem Framework-Thread ermittelt werden (Dalamud-
        // Service-API über partyService, siehe Klassendoc "WICHTIG - Cross-Thread-Zugriff" sowie
        // den Bugfix an TriggerBrowseAsync/TriggerBrowse) - NICHT erst im async-Callback weiter
        // unten, sonst würde derselbe "Not on main thread!"-Fehler erneut auftreten.
        var localWorld = this.partyService.GetLocalPlayerWorld();
        if (string.IsNullOrEmpty(localWorld))
        {
            this.SetPendingResult(LiveSyncEventKind.DevTestProfilesFailed, "kein eingeloggter Charakter erkannt");
            return;
        }

        this.devPublishInFlight = true;
        _ = this.PublishDevTestProfilesAsync(localWorld);
    }

    /// <summary>Die drei zu veröffentlichenden Testprofile mit dem in der Aufgabenstellung
    /// vorgegebenen Szenario ("Alice+Charles = bestehende Zweier-Gruppe sucht 1 weitere Person,
    /// Bob sucht allein, offen für mehr") - als eigener Record statt anonymer Tupel, damit die
    /// foreach-Schleife in <see cref="PublishDevTestProfilesAsync"/> lesbar bleibt.</summary>
    private sealed record DevTestProfileSpec(
        string Name,
        Func<SpellDataService, PlayerSpellStatus> CreateFixture,
        List<string> AvailabilityTags,
        string Note,
        int WantedPlayerCount);

    private static readonly IReadOnlyList<DevTestProfileSpec> DevTestProfileSpecs = new List<DevTestProfileSpec>
    {
        new("Alice", DevTestFixtures.CreateAlice, new List<string> { "evening" }, "Testgruppe mit Charles", 1),
        new("Bob", DevTestFixtures.CreateBob, new List<string> { "flexible" }, "Suche Gruppe", 3),
        new("Charles", DevTestFixtures.CreateCharles, new List<string> { "evening" }, "Testgruppe mit Alice", 1),
    };

    private async Task PublishDevTestProfilesAsync(string localWorld)
    {
        try
        {
            var succeededNames = new List<string>();
            var failedDetails = new List<string>();

            // Sequenziell statt parallel (egal laut Aufgabenstellung) - bewusst so gewählt: alle
            // drei teilen sich denselben httpClient/dieselbe devTestProfileEditTokens-Dictionary,
            // sequenziell bleibt das trivial nebenläufigkeitssicher, ohne dafür extra Locking
            // einzuführen (3 kurze Requests, kein spürbarer Performance-Unterschied).
            foreach (var spec in DevTestProfileSpecs)
            {
                try
                {
                    var status = spec.CreateFixture(this.spellDataService);
                    var bitmaskBase64 = ManualCodeSyncProvider.ToBase64Url(
                        ManualCodeSyncProvider.EncodeBitmask(this.spellDataService, status.LearnedSpellIds));

                    var tokenKey = BuildTokenKey(spec.Name, localWorld);
                    var url = BuildProfileUrl(localWorld, spec.Name);

                    // Best-effort DELETE des vorherigen Testlaufs (siehe devTestProfileEditTokens-
                    // Doc) - NUR versucht, wenn aus einem früheren Klick in dieser Session
                    // überhaupt ein Token bekannt ist (ohne Token würde der Worker ohnehin nur mit
                    // 403 "Header fehlt" antworten, das spart einen sinnlosen Roundtrip). Ergebnis
                    // bewusst nicht ausgewertet - Fehler hier sind erwartet/kein Problem (siehe
                    // Aufgabenstellung: 403/404 sind hier normal).
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
                            // Netzwerkfehler beim best-effort-DELETE ebenfalls ignorieren.
                        }

                        this.devTestProfileEditTokens.Remove(tokenKey);
                    }

                    // Kein editToken im Body: nach dem (best-effort) DELETE oben soll der Worker
                    // ein FRISCHES Profil anlegen (existing === null-Zweig in handlePut, siehe
                    // worker/src/index.ts) statt eines Updates - genau das hält den Button
                    // wiederholt klickbar, siehe Aufgabenstellung.
                    var requestBody = new PushRequestBody(
                        bitmaskBase64,
                        null,
                        "listed",
                        spec.AvailabilityTags,
                        spec.Note,
                        spec.WantedPlayerCount);

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

            // Ergebnis/Fehler pro Person einzeln gesammelt statt beim ersten Fehler abzubrechen
            // (siehe Aufgabenstellung) - Erfolg (grün) NUR, wenn WIRKLICH alle drei geklappt haben,
            // sonst rot mit den betroffenen Fixtures (unabhängig davon, wie viele erfolgreich waren).
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

    /// <summary>True, wenn für den aktuellen Charakter ein Edit-Token bekannt ist - steuert in
    /// MainWindow, ob der "Mein Profil löschen"-Button sichtbar/aktiv ist (siehe Aufgabenstellung:
    /// nur sinnvoll bedienbar, wenn es überhaupt etwas zu löschen gibt).</summary>
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
        _ = this.DeleteOwnProfileAsync();
    }

    private async Task DeleteOwnProfileAsync()
    {
        try
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
                // Sollte praktisch nicht vorkommen (UI zeigt den Button nur an, wenn ein Token
                // existiert, siehe HasEditTokenForLocalCharacter) - defensiv trotzdem abgesichert.
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

            // Sonst würde der nächste TickPushDiff sofort wieder ein neues, tokenloses Profil
            // anlegen (siehe Aufgabenstellung) - das eigentliche Löschen soll auch wirklich
            // gelöscht bleiben, bis der Nutzer Live-Sync bewusst erneut aktiviert.
            this.configuration.LiveSyncEnabled = false;
            this.configuration.Save();

            this.SetPendingResult(LiveSyncEventKind.DeleteSucceeded, null);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "LiveSyncService: unerwarteter Fehler beim Löschen des eigenen Profils.");
            this.SetPendingResult(LiveSyncEventKind.DeleteFailed, ex.Message);
        }
        finally
        {
            this.deleteInFlight = false;
        }
    }

    /// <summary>Erlaubter Mitgliederbereich einer Gruppen-Listung (siehe <see cref="PublishGroup"/>) -
    /// exakt dieselben Grenzen wie worker/src/index.ts GROUP_MEMBER_COUNT_MIN/MAX. Die UI
    /// (siehe MainWindow.DrawGroupFinderTab) begrenzt die Auswahl bereits selbst auf diesen
    /// Bereich (Button deaktiviert außerhalb davon) - diese Prüfung hier ist die zusätzliche,
    /// defensive Absicherung direkt vor dem tatsächlichen Request.</summary>
    private const int GroupMemberCountMin = 1;
    private const int GroupMemberCountMax = 8;

    /// <summary>Veröffentlicht/aktualisiert die EIGENE Gruppen-Listung (Phase 2 "Gruppenfinder",
    /// PUT /group/:groupId, siehe worker/src/index.ts handleGroupPut) - ausschließlich bei
    /// explizitem Klick auf "Gruppe veröffentlichen" aufgerufen (siehe MainWindow.DrawGroupFinderTab),
    /// bewusst OHNE Debounce/Auto-Push (anders als PushOwnProfile/TickPushDiff): eine Gruppen-
    /// Listung hat keinen automatischen Aktualisierungs-Trigger wie "neuer Spell gelernt", jede
    /// Änderung bleibt bis zum nächsten Klick rein lokal.
    ///
    /// Erkennt Neuanlage vs. Update über <see cref="Configuration.GroupFinderOwnGroupIds"/>
    /// (Key = eigener Charakter über <see cref="BuildTokenKey"/>): ist dort bereits eine groupId
    /// hinterlegt, wird dieselbe Gruppe per PUT mit dem gespeicherten Edit-Token aktualisiert
    /// (kein zweiter, paralleler Eintrag) - sonst wird eine neue groupId erzeugt und eine frische
    /// Gruppe angelegt. GroupId + der vom Server zurückgegebene Edit-Token werden bei einer
    /// Neuanlage BEIDE sofort persistiert (siehe Configuration.cs-Doc zu beiden Dictionaries) -
    /// ohne das wäre die Gruppe nach einem Plugin-/Spiel-Neustart nicht mehr aktualisierbar/löschbar.</summary>
    public void PublishGroup(
        IReadOnlyList<(string World, string CharacterName)> members,
        bool visible,
        IReadOnlyCollection<AvailabilityTag> tags,
        string note,
        int wantedPlayerCount)
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

        // MUSS synchron HIER auf dem Framework-Thread ermittelt werden (Dalamud-Service-API über
        // partyService, siehe Klassendoc "WICHTIG - Cross-Thread-Zugriff" sowie den bestehenden
        // Bugfix an TriggerBrowseAsync/PublishDevTestProfiles) - NICHT erst im async-Callback
        // weiter unten.
        var localName = this.partyService.GetLocalPlayerName();
        var localWorld = this.partyService.GetLocalPlayerWorld();
        if (string.IsNullOrEmpty(localName) || string.IsNullOrEmpty(localWorld))
        {
            this.SetPendingResult(LiveSyncEventKind.GroupPublishFailed, "kein eingeloggter Charakter erkannt");
            return;
        }

        this.groupPublishInFlight = true;
        _ = this.PublishGroupAsync(members, visible, tags, note, wantedPlayerCount, localName, localWorld);
    }

    private async Task PublishGroupAsync(
        IReadOnlyList<(string World, string CharacterName)> members,
        bool visible,
        IReadOnlyCollection<AvailabilityTag> tags,
        string note,
        int wantedPlayerCount,
        string localName,
        string localWorld)
    {
        try
        {
            var tokenKey = BuildTokenKey(localName, localWorld);
            var isUpdate = this.configuration.GroupFinderOwnGroupIds.TryGetValue(tokenKey, out var existingGroupId);
            var groupId = isUpdate ? existingGroupId! : Guid.NewGuid().ToString();

            // editToken nur bei einem UPDATE mitgeschickt (siehe worker/src/index.ts
            // handleGroupPut: existing !== null erfordert einen passenden Token) - bei einer
            // Neuanlage bleibt er null, der Worker generiert dann selbst einen neuen.
            string? editToken = null;
            if (isUpdate)
                this.configuration.GroupFinderGroupEditTokens.TryGetValue(groupId, out editToken);

            var requestBody = new PutGroupRequestBody(
                members.Select(m => new GroupMemberWire(m.World, m.CharacterName)).ToList(),
                editToken,
                visible ? "listed" : "unlisted",
                tags.Select(tag => tag.ToWireValue()).ToList(),
                note,
                wantedPlayerCount);

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

            // groupId + editToken BEIDE persistieren (siehe Konfigurations-Doc/Methodendoc oben) -
            // bei einem Update ist editToken in der Response bewusst leer (Server gibt den
            // Klartext-Token nur einmal, bei der Neuanlage, zurück), dann bleibt der bereits
            // gespeicherte Token unverändert stehen.
            this.configuration.GroupFinderOwnGroupIds[tokenKey] = groupId;
            if (!string.IsNullOrEmpty(responseBody?.EditToken))
                this.configuration.GroupFinderGroupEditTokens[groupId] = responseBody!.EditToken!;
            this.configuration.Save();

            this.SetPendingResult(LiveSyncEventKind.GroupPublishSucceeded, null);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "LiveSyncService: unerwarteter Fehler beim Veröffentlichen der Gruppe.");
            this.SetPendingResult(LiveSyncEventKind.GroupPublishFailed, ex.Message);
        }
        finally
        {
            this.groupPublishInFlight = false;
        }
    }

    /// <summary>True, wenn für den aktuellen Charakter bereits eine Gruppen-Listung veröffentlicht
    /// ist - steuert in MainWindow, ob der "Gruppe wieder löschen"-Button sichtbar/aktiv ist
    /// (analog zu <see cref="HasEditTokenForLocalCharacter"/> für das Einzelprofil).</summary>
    public bool HasPublishedGroup()
    {
        var localName = this.partyService.GetLocalPlayerName();
        var localWorld = this.partyService.GetLocalPlayerWorld();
        if (string.IsNullOrEmpty(localName) || string.IsNullOrEmpty(localWorld))
            return false;

        return this.configuration.GroupFinderOwnGroupIds.ContainsKey(BuildTokenKey(localName, localWorld));
    }

    /// <summary>Löscht die EIGENE Gruppen-Listung (DELETE /group/:groupId, siehe
    /// worker/src/index.ts handleGroupDelete) - analog zu <see cref="DeleteOwnProfile"/>. Kein
    /// Request, wenn für den aktuellen Charakter keine groupId hinterlegt ist (die UI zeigt den
    /// zugehörigen Button ohnehin nur an, wenn <see cref="HasPublishedGroup"/> true ist, siehe
    /// MainWindow.DrawGroupFinderTab) - rührt NIE an den referenzierten Einzelprofilen der
    /// Mitglieder (siehe worker-seitigen Referenz-statt-Kopie-Ansatz).</summary>
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
            return; // Nichts zu löschen - siehe HasPublishedGroup/Methodendoc oben.

        this.groupDeleteInFlight = true;
        _ = this.DeletePublishedGroupAsync(tokenKey, groupId);
    }

    private async Task DeletePublishedGroupAsync(string tokenKey, string groupId)
    {
        try
        {
            if (!this.configuration.GroupFinderGroupEditTokens.TryGetValue(groupId, out var token))
            {
                // Sollte praktisch nicht vorkommen (eine hinterlegte groupId ohne zugehörigen
                // Edit-Token wäre ein inkonsistenter lokaler Zustand) - defensiv trotzdem
                // abgesichert, analog zu DeleteOwnProfileAsync.
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

            // BEIDE lokalen Einträge entfernen (siehe Configuration.cs-Doc) - sonst würde ein
            // erneuter Klick auf "Gruppe veröffentlichen" versuchen, mit der jetzt serverseitig
            // gelöschten groupId/dem verwaisten Token zu aktualisieren (409, da der Worker die
            // Gruppe nicht mehr kennt).
            this.configuration.GroupFinderOwnGroupIds.Remove(tokenKey);
            this.configuration.GroupFinderGroupEditTokens.Remove(groupId);
            this.configuration.Save();

            this.SetPendingResult(LiveSyncEventKind.GroupUnpublishSucceeded, null);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "LiveSyncService: unerwarteter Fehler beim Löschen der eigenen Gruppen-Listung.");
            this.SetPendingResult(LiveSyncEventKind.GroupUnpublishFailed, ex.Message);
        }
        finally
        {
            this.groupDeleteInFlight = false;
        }
    }

    /// <summary>Holt (und leert) den "Briefkasten" mit dem Ergebnis des zuletzt abgeschlossenen
    /// Push/Fetch/Delete-Vorgangs, falls seit dem letzten Aufruf ein neues vorliegt - von
    /// MainWindow.Draw() einmal pro Frame aufzurufen (siehe Klassendoc).</summary>
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

    private static string DescribeHttpFailure(HttpStatusCode statusCode, string? reasonPhrase) =>
        string.IsNullOrEmpty(reasonPhrase) ? $"HTTP {(int)statusCode}" : $"HTTP {(int)statusCode} {reasonPhrase}";

    /// <summary>Format "CharacterName@World" - siehe Configuration.LiveSyncEditTokens-Doc für die
    /// Begründung (Mehrfach-Charakter-Unterstützung auf demselben PC/Dalamud-Profil).</summary>
    private static string BuildTokenKey(string characterName, string world) => $"{characterName}@{world}";

    /// <summary>Uri.EscapeDataString für beide Segmente: Charakternamen enthalten oft
    /// Leerzeichen/Apostrophe ("Y'shtola Rhul"), der Worker dekodiert per decodeURIComponent
    /// wieder (siehe worker/src/index.ts).</summary>
    private static string BuildProfileUrl(string world, string characterName) =>
        $"{WorkerBaseUrl}/profile/{Uri.EscapeDataString(world)}/{Uri.EscapeDataString(characterName)}";

    /// <summary>Analog zu <see cref="BuildProfileUrl"/>, aber für Gruppen-Listungen - groupId ist
    /// EIN einzelnes, vom Client generiertes Pfadsegment (siehe worker/src/index.ts GROUP_PATH),
    /// trotzdem escaped (Guid.ToString() enthält zwar selbst keine URL-Sonderzeichen, aber es
    /// gibt keinen Grund, sich implizit darauf zu verlassen).</summary>
    private static string BuildGroupUrl(string groupId) => $"{WorkerBaseUrl}/group/{Uri.EscapeDataString(groupId)}";

    public void Dispose() => this.httpClient.Dispose();

    /// <summary>Visibility/AvailabilityTags/Note/WantedPlayerCount sind bewusst nullable (siehe
    /// pendingVisibility-Felddoc UND den JsonOptions-Kommentar oben) - null bedeutet "im Body
    /// weglassen", nicht "auf null/leer setzen".</summary>
    private sealed record PushRequestBody(
        string SpellBitmaskBase64,
        string? EditToken,
        string? Visibility,
        List<string>? AvailabilityTags,
        string? Note,
        int? WantedPlayerCount);

    private sealed record PushResponseBody(
        string? EditToken,
        string? DataCenter,
        string? Visibility,
        List<string>? AvailabilityTags,
        string? Note,
        int? WantedPlayerCount);

    private sealed record FetchResponseBody(string? SpellBitmaskBase64);

    /// <summary>Ein einzelnes members[]-Element für PUT /group/:groupId (siehe
    /// worker/src/index.ts isValidRawGroupMember) - NUR world+characterName, keine Bitmaske
    /// (siehe PublishGroup-Doc: Gruppen-Listungen referenzieren bestehende Einzelprofile, statt
    /// sie zu duplizieren).</summary>
    private sealed record GroupMemberWire(string World, string CharacterName);

    /// <summary>Body für PUT /group/:groupId - Visibility/AvailabilityTags/Note/WantedPlayerCount
    /// bewusst nullable wie bei <see cref="PushRequestBody"/> (dieselbe JsonOptions-
    /// WhenWritingNull-Begründung gilt hier genauso, siehe dortigen Kommentar), auch wenn
    /// PublishGroup diese Felder aktuell bei jedem Aufruf tatsächlich mitschickt (kein "stiller"
    /// Zwischenstand wie beim automatischen Einzelprofil-Diff-Push).</summary>
    private sealed record PutGroupRequestBody(
        List<GroupMemberWire> Members,
        string? EditToken,
        string? Visibility,
        List<string>? AvailabilityTags,
        string? Note,
        int? WantedPlayerCount);

    /// <summary>Response von PUT /group/:groupId (siehe worker/src/index.ts stripForGroupResponse) -
    /// nur EditToken wird hier tatsächlich gebraucht (siehe PublishGroupAsync); die übrigen vom
    /// Worker zurückgegebenen Felder (members/dataCenter/visibility/...) sind für den
    /// Publish-Flow selbst ohne Bedeutung, da der Client den gesendeten Stand bereits kennt.</summary>
    private sealed record PutGroupResponseBody(string? EditToken);

    /// <summary>Ein einzelner Eintrag aus der GET /profiles/browse-Antwort (siehe
    /// worker/src/index.ts stripForBrowseResponse) - bewusst alle Felder nullable/optional
    /// eingelesen, obwohl der Worker sie eigentlich immer mitschickt: ein robuster Client
    /// vertraut der Gegenseite nicht blind auf Anwesenheit/Nicht-Null (siehe TriggerBrowseAsync,
    /// wo fehlende Werte auf sichere Defaults abgebildet werden, statt eine NullReferenceException
    /// zu riskieren).</summary>
    private sealed record BrowseResponseEntry(
        string? CharacterName,
        string? World,
        string? SpellBitmaskBase64,
        List<string>? AvailabilityTags,
        string? Note,
        int? WantedPlayerCount,
        string? UpdatedAt);

    /// <summary>Ein einzelnes Mitglied innerhalb eines GET /groups/browse-Eintrags (siehe
    /// worker/src/index.ts handleGroupsBrowse) - SpellBitmaskBase64 ist hier (anders als bei
    /// <see cref="BrowseResponseEntry"/>) explizit optional/kann null sein: der Worker liefert
    /// null, wenn er zu diesem Mitglied kein Einzelprofil (mehr) finden konnte.</summary>
    private sealed record GroupBrowseResponseMember(string? World, string? CharacterName, string? SpellBitmaskBase64);

    /// <summary>Ein einzelner Eintrag aus der GET /groups/browse-Antwort (siehe worker/src/index.ts
    /// handleGroupsBrowse) - analog zu <see cref="BrowseResponseEntry"/>, aber für Gruppen-Listungen;
    /// bewusst alle Felder nullable/optional eingelesen (siehe dortigen Kommentar zum robusten
    /// Client, der der Gegenseite nicht blind vertraut).</summary>
    private sealed record GroupBrowseResponseEntry(
        string? GroupId,
        List<GroupBrowseResponseMember>? Members,
        List<string>? AvailabilityTags,
        string? Note,
        int? WantedPlayerCount);
}
