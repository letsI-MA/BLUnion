/**
 * BLUnion Live-Sync Worker (Phase 1 + Phase 2 "Gruppenfinder").
 *
 * Ersetzt für den Alltagsfall "gemeinsam mit anderen Blue Mages in einer Party" den manuellen
 * BLU:-Code-Austausch (siehe ManualCodeSyncProvider.cs im Plugin) durch automatisches
 * Veröffentlichen/Abrufen des eigenen Spell-Status über diesen Worker + einen KV-Namespace.
 *
 * Bewusst OHNE Framework/Router-Bibliothek (siehe Aufgabenstellung "keine unnötigen
 * Abhängigkeiten") - für diese Endpunkte reicht ein einfacher switch/Regex-Abgleich völlig.
 *
 * Endpunkte (siehe README.md für Details/Beispiele):
 *   PUT    /profile/:world/:characterName   - eigenes Profil anlegen/aktualisieren
 *   GET    /profile/:world/:characterName   - Profil abrufen (öffentlich, kein Token nötig)
 *   DELETE /profile/:world/:characterName   - eigenes Profil löschen (X-Edit-Token-Header nötig)
 *   GET    /profiles/browse?dataCenter=<DC> - öffentlicher Gruppenfinder (Phase 2, siehe unten)
 *   PUT    /group/:groupId                  - Gruppen-Listung anlegen/aktualisieren (Phase 2)
 *   DELETE /group/:groupId                  - Gruppen-Listung löschen (X-Edit-Token-Header nötig)
 *   GET    /groups/browse?dataCenter=<DC>   - öffentlicher GRUPPEN-Gruppenfinder (Phase 2, siehe unten)
 *
 * KV-Key-Schema: "profile:<world>:<characterName>", beide Teile lowercased (siehe kvKey()) -
 * verhindert Duplikate durch abweichende Groß-/Kleinschreibung (das Spiel liefert Namen/Welten
 * nicht immer konsistent kapitalisiert). :world/:characterName in der URL sind
 * URI-komponenten-kodiert zu übergeben (Charakternamen können Leer-/Sonderzeichen enthalten).
 * Gruppen-Listungen liegen unter einem eigenen Key-Schema "group:<groupId>" (siehe groupKvKey()) -
 * groupId ist ein vom Client beim Erstellen generierter, zufälliger String (z.B.
 * crypto.randomUUID() im C#-Client), NICHT lowercased, weil es hier - anders als bei
 * world+characterName - keinen natürlichen Schlüssel gibt, den man auf diese Weise deduplizieren
 * müsste.
 *
 * WICHTIG (Phase 2 - Gruppenfinder ist KEIN separates Profil): visibility/availabilityTags/
 * note/wantedPlayerCount erweitern dasselbe StoredProfile, das schon Phase 1 anlegt. Es gibt
 * keinen eigenständigen "Gruppenfinder-Login" - Sichtbarkeit im Gruppenfinder setzt zwingend
 * voraus, dass für den Charakter bereits ein Live-Sync-Profil (mit editToken) existiert.
 *
 * WICHTIG (Gruppen-Listungen referenzieren bestehende Einzelprofile, statt sie zu duplizieren):
 * ein "group:"-Eintrag (siehe StoredGroupProfile) speichert je Mitglied NUR world+characterName,
 * KEINE eigene Kopie von dessen Spell-Bitmaske - die wird erst bei GET /groups/browse live aus
 * dem zugehörigen "profile:"-Eintrag jedes Mitglieds nachgeladen (siehe handleGroupsBrowse). Der
 * editToken einer Gruppen-Listung identifiziert dabei AUSSCHLIESSLICH deren Ersteller/
 * Veröffentlicher, NICHT die referenzierten Mitglieder (siehe Doc an handleGroupPut/
 * handleGroupDelete) - wer die Gruppe veröffentlicht, ist der einzige, der sie später
 * ändern/löschen kann.
 *
 * Sowohl das Dalamud-Plugin ALS AUCH die Website (docs/index.html) sind Clients DIESES EINEN
 * Workers - kein zweites Backend, keine Parallelstruktur. Der optionale PUT-Body-Parameter
 * ttlHours (siehe resolveTtlSeconds) existiert eigens für die Website, die damit Profile mit
 * einer kürzeren als der Standard-Lebensdauer veröffentlichen kann.
 *
 * CACHING: GET /profiles/browse und GET /groups/browse sind jetzt für BROWSE_CACHE_TTL_SECONDS
 * serverseitig gecached (siehe withCache) - das Plugin pollt den Gruppenfinder-Tab automatisch
 * alle 15 Sekunden (siehe UI/MainWindow.cs GroupFinderAutoRefreshInterval), ohne diesen Cache
 * würde das direkt mit der Nutzerzahl skalieren (jeder offene Gruppenfinder-Tab = ein voller
 * KV-list()-Durchlauf alle 15s). PUT/DELETE bleiben UNVERÄNDERT live/ungecached - ein frisch
 * veröffentlichtes/gelöschtes Profil kann dadurch für bis zu BROWSE_CACHE_TTL_SECONDS Sekunden in
 * Browse-Ergebnissen leicht veraltet erscheinen. Bewusster Kompromiss (siehe Aufgabenstellung).
 */

import { base64UrlDecode, generateEditToken, sha256Hex } from "./crypto";
import { lookupDataCenter } from "./worlds";

export interface Env {
  BLUNION_PROFILES: KVNamespace;
}

/** Feste Größe der Spell-Bitmaske - MUSS mit ManualCodeSyncProvider.BitmaskBytes im Plugin
 * übereinstimmen (16 Byte = 128 Bit, siehe dortiger Klassendoc). Anders als im "BLU:"-Sync-Code
 * enthält spellBitmaskBase64 hier NUR die Bitmaske (kein Namens-Präfixbyte) - der Charaktername
 * steht schon im URL-Pfad bzw. im gespeicherten JSON-Feld "characterName". */
const BITMASK_BYTES = 16;

/** 90 Tage in Sekunden - kompletter Cleanup-Mechanismus für Phase 1 (siehe README.md): wird bei
 * JEDEM Put neu gesetzt (KV expirationTtl ist relativ zum Put-Zeitpunkt), aktive Profile laufen
 * dadurch nie ab, inaktive verschwinden automatisch, kein Cron-Job nötig. Default, wenn im
 * PUT-Body kein (gültiges) ttlHours mitgeschickt wird (siehe resolveTtlSeconds) - genau das
 * bisherige Phase-1/2-Verhalten für das Plugin, das dieses Feld nie mitschickt. */
const PROFILE_TTL_SECONDS = 90 * 24 * 3600;

/** Optionales TTL-Override fürs PUT (siehe README.md/docs/index.html - die Website setzt hier
 * IMMER einen kürzeren Wert als die Plugin-Default-Lebensdauer, u.a. weil Testprofile/Kurzzeit-
 * Veröffentlichungen von der Web-Seite aus nicht 90 Tage lang herumliegen sollen). Bewusst
 * GECLAMPT statt mit 400 abgelehnt (siehe Aufgabenstellung: "das Feld ist eine Optimierung, kein
 * Sicherheitsfeature, muss also nicht hart validiert werden") - TTL_HOURS_MAX entspricht exakt
 * PROFILE_TTL_SECONDS (90 Tage), ein manipulierter Client kann sich über dieses Feld also KEINE
 * längere als die reguläre Lebensdauer erschleichen, nur eine kürzere anfordern. */
const TTL_HOURS_MIN = 1;
const TTL_HOURS_MAX = 2160; // 90 Tage * 24 Stunden, siehe PROFILE_TTL_SECONDS

/** TTL für das serverseitige Kurzzeit-Caching von GET /profiles/browse und GET /groups/browse
 * (siehe withCache) - bewusst etwas LÄNGER als das 15-Sekunden-Client-Polling-Intervall (siehe
 * UI/MainWindow.cs GroupFinderAutoRefreshInterval), damit mehrere kurz hintereinander
 * eintreffende Requests (von einem oder mehreren Nutzern) auf denselben Cache-Eintrag treffen,
 * statt jedes Mal neu über alle KV-Keys zu iterieren. */
const BROWSE_CACHE_TTL_SECONDS = 20;

const PROFILE_PATH = /^\/profile\/([^/]+)\/([^/]+)\/?$/;
const BROWSE_PATH = /^\/profiles\/browse\/?$/;

/** Analog zu PROFILE_PATH/BROWSE_PATH, aber für Gruppen-Listungen (siehe Klassendoc/
 * StoredGroupProfile) - :groupId ist EIN einzelnes Pfadsegment (kein "/", anders als world+
 * characterName bei PROFILE_PATH), da es sich um einen einzelnen opaken, vom Client generierten
 * String handelt. */
const GROUP_PATH = /^\/group\/([^/]+)\/?$/;
const GROUPS_BROWSE_PATH = /^\/groups\/browse\/?$/;

/** Ursprünglich für Phase 2 (Website/Gruppenfinder) vorbereitet, jetzt tatsächlich gebraucht
 * (siehe handleBrowse) - vom Plugin aus für die reinen /profile/-Endpunkte weiterhin nicht
 * zwingend nötig, kostet aber nichts, überall gesetzt zu sein. */
const CORS_HEADERS: Record<string, string> = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, PUT, DELETE, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, X-Edit-Token",
  "Access-Control-Max-Age": "86400",
};

/** Erlaubte Werte für availabilityTags (siehe handlePut/isValidAvailabilityTags) - bewusst
 * intern englisch gehalten, die Übersetzung für die UI passiert ausschließlich im Plugin über
 * UiStrings, nicht hier im Worker (siehe Aufgabenstellung). */
const ALLOWED_AVAILABILITY_TAGS = ["morning", "afternoon", "evening", "weekend", "flexible"] as const;

/** Serverseitige Obergrenze für "note" (siehe handlePut) - wird bei Überschreitung gekappt,
 * nicht abgelehnt (siehe Aufgabenstellung: "serverseitig kappen/validieren, nicht nur
 * clientseitig vertrauen" - das Plugin begrenzt die ImGui-Eingabe zwar schon selbst auf 60
 * Zeichen, ein direkter/manipulierter API-Aufruf könnte das aber umgehen). */
const NOTE_MAX_LENGTH = 60;

/** Sinnvoller Bereich für wantedPlayerCount (siehe handlePut) - 0 bedeutet "egal wie viele"
 * (siehe Aufgabenstellung), 8 ist die maximale Party-/Alliance-artige Gruppengröße, für die ein
 * Blue-Mage-Gruppenfinder überhaupt Sinn ergibt. */
const WANTED_PLAYER_COUNT_MIN = 0;
const WANTED_PLAYER_COUNT_MAX = 8;

/** Größe von StoredGroupProfile.members (siehe handleGroupPut) - mindestens 1 (eine "Gruppe" aus
 * 0 Mitgliedern wäre sinnlos), höchstens 8 (dieselbe maximale Party-/Alliance-artige Gruppengröße
 * wie WANTED_PLAYER_COUNT_MAX oben). */
const GROUP_MEMBER_COUNT_MIN = 1;
const GROUP_MEMBER_COUNT_MAX = 8;

/** Das in KV gespeicherte JSON (siehe Datenmodell in README.md). editTokenHash verlässt diese
 * Datei NIE in Richtung Client (siehe stripForResponse/stripForBrowseResponse).
 *
 * availabilityTags/note/wantedPlayerCount sind Phase-2-Felder (Gruppenfinder) - bei Profilen,
 * die noch von Phase 1 stammen (vor diesem Update angelegt/zuletzt aktualisiert), fehlen sie im
 * gespeicherten JSON schlicht (KV kennt kein Schema/keine Migration). Jede Stelle, die ein
 * StoredProfile liest, behandelt sie deshalb als optional (siehe "?" hier UND die "??"-Fallbacks
 * in handlePut/stripForResponse/stripForBrowseResponse) statt sich auf ihre Anwesenheit zu
 * verlassen.
 */
interface StoredProfile {
  characterName: string;
  world: string;
  dataCenter: string;
  spellBitmaskBase64: string;
  editTokenHash: string;
  visibility: "listed" | "unlisted";
  availabilityTags?: string[];
  note?: string;
  wantedPlayerCount?: number;
  createdAt: string;
  updatedAt: string;
}

interface PutRequestBody {
  spellBitmaskBase64?: unknown;
  editToken?: unknown;
  visibility?: unknown;
  availabilityTags?: unknown;
  note?: unknown;
  wantedPlayerCount?: unknown;
  ttlHours?: unknown;
}

/** Referenz auf ein Mitglied EINER Gruppen-Listung (siehe StoredGroupProfile) - identifiziert
 * ausschließlich per world+characterName, denselben beiden Feldern, die auch den KV-Key eines
 * "profile:"-Eintrags bilden (siehe kvKey()). Enthält bewusst NICHTS aus dem Einzelprofil selbst
 * (keine Bitmaske, kein dataCenter) - das wird erst bei GET /groups/browse live nachgeladen
 * (siehe handleGroupsBrowse). world ist hier bereits die KANONISCHE Schreibweise (siehe
 * handleGroupPut/lookupDataCenter), nicht zwingend die vom Client übergebene. */
interface GroupMember {
  world: string;
  characterName: string;
}

/** Das in KV unter "group:<groupId>" gespeicherte JSON (siehe Klassendoc oben zum
 * Referenz-statt-Kopie-Ansatz und README.md für das vollständige Datenmodell). editTokenHash
 * verlässt diese Datei NIE in Richtung Client (siehe stripForGroupResponse), genau wie bei
 * StoredProfile.
 *
 * WICHTIG: editTokenHash identifiziert NUR den Ersteller/Veröffentlicher DIESER Gruppen-Listung -
 * NICHT die referenzierten members[]. Wer die Gruppe per PUT anlegt, ist der einzige, der sie
 * später per PUT/DELETE ändern bzw. löschen kann; die Mitglieder selbst haben darüber KEINEN
 * eigenen Zugriff (siehe Doc an handleGroupPut/handleGroupDelete). Das ist eine bewusste
 * Design-Entscheidung, kein Bug - falls das künftig geändert werden soll (z.B. soll jedes
 * Mitglied die Gruppen-Listung löschen dürfen), müsste der Token stattdessen an alle Mitglieder
 * verteilt oder durch ein anderes Berechtigungsmodell ersetzt werden.
 *
 * dataCenter wird - wie bei StoredProfile - server-seitig hergeleitet (hier vom world des ERSTEN
 * Mitglieds, siehe handleGroupPut), nicht vom Client übergeben. availabilityTags/note/
 * wantedPlayerCount sind exakt dieselben Phase-2-Felder wie bei StoredProfile, inklusive
 * derselben ALLOWED_AVAILABILITY_TAGS/NOTE_MAX_LENGTH/WANTED_PLAYER_COUNT_MIN/MAX-Regeln - daher
 * hier ebenfalls optional (siehe StoredProfile-Doc zur Rückwärtskompatibilität). */
interface StoredGroupProfile {
  groupId: string;
  members: GroupMember[];
  editTokenHash: string;
  visibility: "listed" | "unlisted";
  availabilityTags?: string[];
  note?: string;
  wantedPlayerCount?: number;
  dataCenter: string;
  createdAt: string;
  updatedAt: string;
}

interface PutGroupRequestBody {
  members?: unknown;
  editToken?: unknown;
  visibility?: unknown;
  availabilityTags?: unknown;
  note?: unknown;
  wantedPlayerCount?: unknown;
  ttlHours?: unknown;
}

/** Liefert die tatsächlich zu setzende expirationTtl (Sekunden) aus dem optionalen
 * ttlHours-Body-Feld (siehe TTL_HOURS_MIN/MAX-Doc) - fehlt es oder ist es kein gültiger
 * endlicher Zahlenwert, greift unverändert PROFILE_TTL_SECONDS (bisheriges Verhalten,
 * insbesondere für das Plugin, das dieses Feld nie mitschickt). Math.min/Math.max statt eines
 * if/else-Kaskaden-Clamps - bei nur zwei Grenzen knapper und genauso klar. */
function resolveTtlSeconds(ttlHours: unknown): number {
  if (typeof ttlHours !== "number" || !Number.isFinite(ttlHours))
    return PROFILE_TTL_SECONDS;

  const clampedHours = Math.min(Math.max(ttlHours, TTL_HOURS_MIN), TTL_HOURS_MAX);
  return clampedHours * 3600;
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8", ...CORS_HEADERS },
  });
}

function errorResponse(status: number, message: string): Response {
  return jsonResponse({ error: message }, status);
}

/** Serverseitiges Kurzzeit-Caching für GET-Endpunkte über die Standard-Workers-Cache-API
 * (caches.default) - bewusst KEINE neue Abhängigkeit, keine KV-basierte Eigenimplementierung.
 * NUR für die beiden Browse-Endpunkte genutzt (siehe handleBrowse/handleGroupsBrowse), NIEMALS
 * für PUT/DELETE - Schreiboperationen dürfen nie aus dem Cache beantwortet werden.
 *
 * WICHTIG: Cloudflares Cache-API ist PER EDGE-STANDORT, kein global geteilter Cache - ein
 * Request an einen anderen PoP trifft also ggf. noch nicht denselben Cache-Eintrag. Das
 * reduziert die Last trotzdem spürbar (die meisten Requests eines Nutzers/einer Region landen
 * wiederholt am selben PoP), auch ohne perfekte globale Deduplizierung.
 *
 * response.clone() vor cache.put() ist zwingend: der Response-Body ist ein einmal lesbarer
 * Stream, cache.put() UND der an den Aufrufer zurückgegebene Response würden sich sonst denselben
 * (bereits konsumierten) Stream teilen. ctx.waitUntil() sorgt dafür, dass der Worker nicht auf das
 * Schreiben in den Cache wartet, bevor die Response an den Client geht, der Cache-Put aber trotzdem
 * zuverlässig abgeschlossen wird, bevor die Instanz ggf. beendet wird. */
async function withCache(
  request: Request,
  ctx: ExecutionContext,
  compute: () => Promise<Response>,
): Promise<Response> {
  const cache = caches.default;
  const cached = await cache.match(request);
  if (cached)
    return cached;

  const response = await compute();
  if (response.ok) {
    const cacheableResponse = new Response(response.body, response);
    cacheableResponse.headers.set("Cache-Control", `public, max-age=${BROWSE_CACHE_TTL_SECONDS}`);
    ctx.waitUntil(cache.put(request, cacheableResponse.clone()));
    return cacheableResponse;
  }

  return response;
}

function kvKey(world: string, characterName: string): string {
  return `profile:${world.toLowerCase()}:${characterName.toLowerCase()}`;
}

/** KV-Key für eine Gruppen-Listung - anders als kvKey() bewusst NICHT lowercased: groupId ist
 * ein vom Client generierter, zufälliger String (siehe StoredGroupProfile-Doc) ohne natürlichen
 * Schlüssel, den man auf diese Weise deduplizieren müsste. */
function groupKvKey(groupId: string): string {
  return `group:${groupId}`;
}

/** Bewusst als eigene, kleine Funktion statt der Versuchung nachzugeben, einfach das gespeicherte
 * Objekt "minus editTokenHash" per Destrukturierung durchzureichen - so ist beim Hinzufügen eines
 * künftigen internen Felds nicht automatisch die Gefahr da, es versehentlich mit rauszugeben,
 * weil hier explizit aufgelistet wird, was rausgeht (Allowlist statt Blocklist). Für GET/PUT auf
 * das EIGENE Profil (per world+characterName) - für den Gruppenfinder-Listing-Endpoint siehe
 * stripForBrowseResponse, die bewusst NICHT dataCenter/visibility mit ausliefert (siehe dort). */
function stripForResponse(stored: StoredProfile) {
  return {
    characterName: stored.characterName,
    world: stored.world,
    dataCenter: stored.dataCenter,
    spellBitmaskBase64: stored.spellBitmaskBase64,
    visibility: stored.visibility,
    availabilityTags: stored.availabilityTags ?? [],
    note: stored.note ?? "",
    wantedPlayerCount: stored.wantedPlayerCount ?? 0,
    updatedAt: stored.updatedAt,
  };
}

/** Analog zu stripForResponse, aber für GET /profiles/browse (siehe handleBrowse): dataCenter
 * ist dort redundant (der Aufrufer hat es selbst als Query-Parameter übergeben, alle Ergebnisse
 * teilen es sich) und visibility ebenso (immer "listed", sonst wäre der Eintrag gar nicht erst
 * im Ergebnis) - beide bewusst weggelassen statt sie redundant mitzuschicken (siehe
 * Aufgabenstellung: exaktes Response-Shape für den Browse-Endpoint). editTokenHash verlässt auch
 * hier NIE die Datei, siehe Allowlist-Prinzip oben. */
function stripForBrowseResponse(stored: StoredProfile) {
  return {
    characterName: stored.characterName,
    world: stored.world,
    spellBitmaskBase64: stored.spellBitmaskBase64,
    availabilityTags: stored.availabilityTags ?? [],
    note: stored.note ?? "",
    wantedPlayerCount: stored.wantedPlayerCount ?? 0,
    updatedAt: stored.updatedAt,
  };
}

/** Analog zu stripForResponse, aber für PUT/DELETE-Antworten auf /group/:groupId (siehe
 * handleGroupPut) - Allowlist-Prinzip wie oben, editTokenHash verlässt auch hier NIE die Datei.
 * Für den GET /groups/browse-Response-Shape siehe stattdessen handleGroupsBrowse direkt: der
 * baut die members[] dort individuell zusammen (inkl. nachgeladener spellBitmaskBase64 je
 * Mitglied), stripForGroupResponse liefert das rohe StoredGroupProfile.members (nur world+
 * characterName, ohne Bitmaske) - das ist für die eigene PUT-Bestätigung ausreichend. */
function stripForGroupResponse(stored: StoredGroupProfile) {
  return {
    groupId: stored.groupId,
    members: stored.members,
    dataCenter: stored.dataCenter,
    visibility: stored.visibility,
    availabilityTags: stored.availabilityTags ?? [],
    note: stored.note ?? "",
    wantedPlayerCount: stored.wantedPlayerCount ?? 0,
    updatedAt: stored.updatedAt,
  };
}

function isValidBitmaskBase64(value: unknown): value is string {
  if (typeof value !== "string" || value.length === 0)
    return false;

  try {
    return base64UrlDecode(value).length === BITMASK_BYTES;
  } catch {
    return false;
  }
}

function isValidVisibility(value: unknown): value is "listed" | "unlisted" {
  return value === "listed" || value === "unlisted";
}

/** Einfaches Array.includes() statt einer Validierungsbibliothek (siehe Aufgabenstellung "keine
 * neuen Abhängigkeiten") - bei nur 5 erlaubten Werten völlig ausreichend. Leeres Array ist
 * gültig (Spieler hat noch keine Verfügbarkeit ausgewählt). */
function isValidAvailabilityTags(value: unknown): value is string[] {
  return Array.isArray(value) && value.every(
    (tag) => typeof tag === "string" && (ALLOWED_AVAILABILITY_TAGS as readonly string[]).includes(tag));
}

function isValidWantedPlayerCount(value: unknown): value is number {
  return typeof value === "number"
    && Number.isInteger(value)
    && value >= WANTED_PLAYER_COUNT_MIN
    && value <= WANTED_PLAYER_COUNT_MAX;
}

/** Reine Form-/Typ-Prüfung für EIN members[]-Element aus dem /group/:groupId-PUT-Body (siehe
 * handleGroupPut) - prüft nur, dass world/characterName als nicht-leere Strings vorhanden sind.
 * Ob world tatsächlich über lookupDataCenter auflösbar ist, prüft bewusst NICHT diese Funktion,
 * sondern der Aufrufer direkt - der braucht dafür eine spezifischere Fehlermeldung (welche World
 * genau unbekannt ist), die ein reiner boolean-Type-Guard hier nicht liefern könnte. */
function isValidRawGroupMember(value: unknown): value is { world: string; characterName: string } {
  if (typeof value !== "object" || value === null)
    return false;

  const candidate = value as Record<string, unknown>;
  return typeof candidate.world === "string" && candidate.world.length > 0
    && typeof candidate.characterName === "string" && candidate.characterName.length > 0;
}

async function handleGet(env: Env, world: string, characterName: string): Promise<Response> {
  const stored = await env.BLUNION_PROFILES.get<StoredProfile>(kvKey(world, characterName), "json");

  // Erwarteter Fall für Spieler ohne Live-Sync (siehe Aufgabenstellung) - bewusst kein Log.
  if (!stored)
    return errorResponse(404, "Kein Profil für diese World/diesen Charakternamen gefunden.");

  return jsonResponse(stripForResponse(stored));
}

async function handlePut(env: Env, request: Request, world: string, characterName: string): Promise<Response> {
  let body: PutRequestBody;
  try {
    body = await request.json();
  } catch {
    return errorResponse(400, "Ungültiger oder fehlender JSON-Body.");
  }

  if (!isValidBitmaskBase64(body.spellBitmaskBase64)) {
    return errorResponse(
      400,
      `spellBitmaskBase64 fehlt oder hat nicht die erwartete Länge (${BITMASK_BYTES} Bytes, ` +
        "Base64 URL-safe ohne Padding).",
    );
  }

  const dcLookup = lookupDataCenter(world);
  if (!dcLookup)
    return errorResponse(400, `Unbekannte World "${world}".`);

  const key = kvKey(world, characterName);
  const existing = await env.BLUNION_PROFILES.get<StoredProfile>(key, "json");
  const now = new Date().toISOString();

  // Alle vier Gruppenfinder-Felder sind im Body OPTIONAL (siehe Aufgabenstellung): fehlt ein
  // Feld, bleibt der bisherige Wert unverändert (bei einem neuen Profil greift stattdessen der
  // jeweilige Default) - so leert ein reiner Spell-Status-Push aus Phase 1 (der diese Felder gar
  // nicht kennt) die Gruppenfinder-Angaben NICHT versehentlich. Nur ein tatsächlich im Body
  // vorhandener, aber ungültiger Wert führt zu 400 - ein fehlendes Feld nie.
  let visibility: "listed" | "unlisted";
  if (body.visibility === undefined) {
    visibility = existing?.visibility ?? "unlisted";
  } else if (isValidVisibility(body.visibility)) {
    visibility = body.visibility;
  } else {
    return errorResponse(400, 'visibility muss "listed" oder "unlisted" sein.');
  }

  let availabilityTags: string[];
  if (body.availabilityTags === undefined) {
    availabilityTags = existing?.availabilityTags ?? [];
  } else if (isValidAvailabilityTags(body.availabilityTags)) {
    availabilityTags = body.availabilityTags;
  } else {
    return errorResponse(
      400, `availabilityTags enthält ungültige Werte (erlaubt: ${ALLOWED_AVAILABILITY_TAGS.join(", ")}).`);
  }

  let note: string;
  if (body.note === undefined) {
    note = existing?.note ?? "";
  } else if (typeof body.note === "string") {
    // Gekappt statt abgelehnt (siehe Konstantendoc NOTE_MAX_LENGTH) - das Plugin begrenzt die
    // Eingabe zwar schon selbst, ein direkter API-Aufruf könnte das aber umgehen.
    note = body.note.slice(0, NOTE_MAX_LENGTH);
  } else {
    return errorResponse(400, "note muss ein String sein.");
  }

  let wantedPlayerCount: number;
  if (body.wantedPlayerCount === undefined) {
    wantedPlayerCount = existing?.wantedPlayerCount ?? 0;
  } else if (isValidWantedPlayerCount(body.wantedPlayerCount)) {
    wantedPlayerCount = body.wantedPlayerCount;
  } else {
    return errorResponse(
      400,
      `wantedPlayerCount muss eine ganze Zahl zwischen ${WANTED_PLAYER_COUNT_MIN} und ${WANTED_PLAYER_COUNT_MAX} sein.`,
    );
  }

  let editTokenHash: string;
  let createdAt: string;
  // Nur bei einem NEU angelegten Profil gesetzt - der Klartext-Token wird genau einmal
  // zurückgegeben (siehe Aufgabenstellung), danach existiert er serverseitig nur noch als Hash.
  let plaintextEditTokenForResponse: string | undefined;

  if (existing) {
    const providedToken = typeof body.editToken === "string" ? body.editToken : null;
    if (!providedToken)
      return errorResponse(409, "Profil existiert bereits - editToken erforderlich, um es zu aktualisieren.");

    const providedHash = await sha256Hex(providedToken);
    if (providedHash !== existing.editTokenHash)
      return errorResponse(409, "editToken stimmt nicht mit dem gespeicherten Profil überein.");

    editTokenHash = existing.editTokenHash;
    createdAt = existing.createdAt;
  } else {
    const newToken = generateEditToken();
    editTokenHash = await sha256Hex(newToken);
    plaintextEditTokenForResponse = newToken;
    createdAt = now;
  }

  const record: StoredProfile = {
    characterName,
    world: dcLookup.canonicalWorld,
    dataCenter: dcLookup.dataCenter,
    spellBitmaskBase64: body.spellBitmaskBase64 as string,
    editTokenHash,
    visibility,
    availabilityTags,
    note,
    wantedPlayerCount,
    createdAt,
    updatedAt: now,
  };

  // expirationTtl bei JEDEM Put neu gesetzt (siehe Konstantendoc oben) - das ist Absicht, kein
  // Bug: aktive Profile (die bei jedem gelernten Spell erneut gepusht werden) sollen NIE
  // ablaufen, nur wirklich inaktive nach der jeweils gültigen TTL (siehe resolveTtlSeconds -
  // PROFILE_TTL_SECONDS ohne ttlHours im Body, sonst der geclampte Override).
  await env.BLUNION_PROFILES.put(key, JSON.stringify(record), { expirationTtl: resolveTtlSeconds(body.ttlHours) });

  const responseBody: Record<string, unknown> = stripForResponse(record);
  if (plaintextEditTokenForResponse)
    responseBody.editToken = plaintextEditTokenForResponse;

  return jsonResponse(responseBody, existing ? 200 : 201);
}

/** GET /profiles/browse?dataCenter=<DC> - öffentlicher Gruppenfinder (Phase 2). Liefert alle
 * Profile mit visibility === "listed" auf dem angegebenen Data Center.
 *
 * Bewusst OHNE Sekundär-Index (z.B. "dcindex:<DC>:<key>"): iteriert stattdessen über ALLE
 * "profile:"-Keys per list() und filtert danach in-memory auf dataCenter+visibility. Für die bei
 * Phase 2 zu erwartende Nutzerzahl unkritisch - ein Index wäre hier verfrühte Optimierung (siehe
 * Aufgabenstellung). Sollte die Zahl der Profile insgesamt über ca. 1000 wachsen, lohnt sich ein
 * echter DC-Index (z.B. ein zusätzlicher KV-Key pro Data Center mit einer Liste betroffener
 * Profil-Keys, bei jedem PUT/DELETE mitgepflegt) - dann müsste hier nicht mehr jedes einzelne
 * Profil unabhängig vom Data Center gelesen werden. */
async function handleBrowse(env: Env, request: Request, ctx: ExecutionContext): Promise<Response> {
  // NUR die eigentliche Berechnung läuft hinter withCache (siehe dortigen Kommentar) - der
  // "dataCenter fehlt"-400-Fehler unten wird über response.ok in withCache ohnehin nie gecached,
  // eine Sonderbehandlung dafür ist deshalb nicht nötig.
  return withCache(request, ctx, async () => {
    const url = new URL(request.url);
    const dataCenter = url.searchParams.get("dataCenter");
    if (!dataCenter)
      return errorResponse(400, 'Query-Parameter "dataCenter" fehlt.');

    const normalizedDataCenter = dataCenter.toLowerCase();
    const results: ReturnType<typeof stripForBrowseResponse>[] = [];

    // list() liefert maximal 1000 Keys pro Aufruf (Cloudflare-KV-Limit) - bei mehr Profilen als
    // das über "cursor" paginiert weiterlesen, bis list_complete true ist.
    let cursor: string | undefined;
    do {
      const listResult = await env.BLUNION_PROFILES.list({ prefix: "profile:", cursor });

      for (const listedKey of listResult.keys) {
        const stored = await env.BLUNION_PROFILES.get<StoredProfile>(listedKey.name, "json");
        if (!stored)
          continue; // Zwischen list() und get() gelöscht/abgelaufen - überspringen statt Fehler.

        if (stored.visibility === "listed" && stored.dataCenter.toLowerCase() === normalizedDataCenter)
          results.push(stripForBrowseResponse(stored));
      }

      cursor = listResult.list_complete ? undefined : listResult.cursor;
    } while (cursor);

    return jsonResponse(results);
  });
}

async function handleDelete(env: Env, request: Request, world: string, characterName: string): Promise<Response> {
  const token = request.headers.get("X-Edit-Token");
  if (!token)
    return errorResponse(403, 'Header "X-Edit-Token" fehlt.');

  const key = kvKey(world, characterName);
  const existing = await env.BLUNION_PROFILES.get<StoredProfile>(key, "json");
  if (!existing)
    return errorResponse(404, "Kein Profil für diese World/diesen Charakternamen gefunden.");

  const providedHash = await sha256Hex(token);
  if (providedHash !== existing.editTokenHash)
    return errorResponse(403, "editToken stimmt nicht mit dem gespeicherten Profil überein.");

  await env.BLUNION_PROFILES.delete(key);
  return jsonResponse({ deleted: true });
}

/** PUT /group/:groupId - Gruppen-Listung anlegen/aktualisieren (Phase 2, siehe Klassendoc/
 * StoredGroupProfile). Ablauf 1:1 wie handlePut für Einzelprofile (Existenz-Check,
 * editToken-Erzeugung/-Vergleich über generateEditToken/sha256Hex, dieselbe 409-Antwort bei
 * fehlendem/falschem Token, dieselbe TTL-Logik über resolveTtlSeconds/PROFILE_TTL_SECONDS,
 * dieselbe visibility/availabilityTags/note/wantedPlayerCount-Validierung) - der einzige
 * inhaltliche Unterschied ist die members[]-Validierung/-Auflösung unten anstelle von
 * spellBitmaskBase64.
 *
 * WICHTIG (Edit-Token-Besitzmodell): der editToken identifiziert NUR den Ersteller/
 * Veröffentlicher DIESER Gruppen-Listung, NICHT die referenzierten Mitglieder - wer die Gruppe
 * per PUT anlegt, ist der einzige, der sie später ändern/löschen kann (kein geteilter Zugriff
 * für alle Mitglieder). Das ist eine bewusste Design-Entscheidung, kein Bug - falls das künftig
 * geändert werden soll (z.B. soll jedes Mitglied die Gruppen-Listung löschen dürfen), müsste der
 * Token stattdessen an alle Mitglieder verteilt oder durch ein anderes Berechtigungsmodell
 * (z.B. eigene Tokens pro Mitglied) ersetzt werden. */
async function handleGroupPut(env: Env, request: Request, groupId: string): Promise<Response> {
  let body: PutGroupRequestBody;
  try {
    body = await request.json();
  } catch {
    return errorResponse(400, "Ungültiger oder fehlender JSON-Body.");
  }

  if (!Array.isArray(body.members) || body.members.length < GROUP_MEMBER_COUNT_MIN
      || body.members.length > GROUP_MEMBER_COUNT_MAX) {
    return errorResponse(
      400, `members muss ein Array mit ${GROUP_MEMBER_COUNT_MIN}-${GROUP_MEMBER_COUNT_MAX} Einträgen sein.`);
  }

  // dataCenter wird - wie bei handlePut - server-seitig hergeleitet, hier vom WORLD DES ERSTEN
  // Mitglieds (siehe Aufgabenstellung), nicht vom Client übergeben. Jedes einzelne members[].world
  // muss unabhängig davon über lookupDataCenter auflösbar sein, sonst 400 (gleiche
  // Fehlerbehandlung wie beim bestehenden Einzel-PUT).
  const members: GroupMember[] = [];
  let dataCenter: string | undefined;

  for (const rawMember of body.members) {
    if (!isValidRawGroupMember(rawMember))
      return errorResponse(400, "Jedes members[]-Element benötigt world und characterName als nicht-leeren String.");

    const memberDcLookup = lookupDataCenter(rawMember.world);
    if (!memberDcLookup)
      return errorResponse(400, `Unbekannte World "${rawMember.world}" in members[].`);

    members.push({ world: memberDcLookup.canonicalWorld, characterName: rawMember.characterName });

    if (dataCenter === undefined)
      dataCenter = memberDcLookup.dataCenter;
  }

  const key = groupKvKey(groupId);
  const existing = await env.BLUNION_PROFILES.get<StoredGroupProfile>(key, "json");
  const now = new Date().toISOString();

  // Rest 1:1 wie handlePut - alle vier Gruppenfinder-Felder sind im Body optional, fehlt eines,
  // bleibt der bisherige Wert unverändert (bzw. der jeweilige Default bei einer neuen
  // Gruppen-Listung). Nur ein tatsächlich im Body vorhandener, aber ungültiger Wert führt zu 400.
  let visibility: "listed" | "unlisted";
  if (body.visibility === undefined) {
    visibility = existing?.visibility ?? "unlisted";
  } else if (isValidVisibility(body.visibility)) {
    visibility = body.visibility;
  } else {
    return errorResponse(400, 'visibility muss "listed" oder "unlisted" sein.');
  }

  let availabilityTags: string[];
  if (body.availabilityTags === undefined) {
    availabilityTags = existing?.availabilityTags ?? [];
  } else if (isValidAvailabilityTags(body.availabilityTags)) {
    availabilityTags = body.availabilityTags;
  } else {
    return errorResponse(
      400, `availabilityTags enthält ungültige Werte (erlaubt: ${ALLOWED_AVAILABILITY_TAGS.join(", ")}).`);
  }

  let note: string;
  if (body.note === undefined) {
    note = existing?.note ?? "";
  } else if (typeof body.note === "string") {
    note = body.note.slice(0, NOTE_MAX_LENGTH);
  } else {
    return errorResponse(400, "note muss ein String sein.");
  }

  let wantedPlayerCount: number;
  if (body.wantedPlayerCount === undefined) {
    wantedPlayerCount = existing?.wantedPlayerCount ?? 0;
  } else if (isValidWantedPlayerCount(body.wantedPlayerCount)) {
    wantedPlayerCount = body.wantedPlayerCount;
  } else {
    return errorResponse(
      400,
      `wantedPlayerCount muss eine ganze Zahl zwischen ${WANTED_PLAYER_COUNT_MIN} und ${WANTED_PLAYER_COUNT_MAX} sein.`,
    );
  }

  let editTokenHash: string;
  let createdAt: string;
  let plaintextEditTokenForResponse: string | undefined;

  if (existing) {
    const providedToken = typeof body.editToken === "string" ? body.editToken : null;
    if (!providedToken)
      return errorResponse(409, "Gruppen-Listung existiert bereits - editToken erforderlich, um sie zu aktualisieren.");

    const providedHash = await sha256Hex(providedToken);
    if (providedHash !== existing.editTokenHash)
      return errorResponse(409, "editToken stimmt nicht mit der gespeicherten Gruppen-Listung überein.");

    editTokenHash = existing.editTokenHash;
    createdAt = existing.createdAt;
  } else {
    const newToken = generateEditToken();
    editTokenHash = await sha256Hex(newToken);
    plaintextEditTokenForResponse = newToken;
    createdAt = now;
  }

  const record: StoredGroupProfile = {
    groupId,
    members,
    editTokenHash,
    visibility,
    availabilityTags,
    note,
    wantedPlayerCount,
    dataCenter: dataCenter!,
    createdAt,
    updatedAt: now,
  };

  // expirationTtl bei JEDEM Put neu gesetzt, exakt wie bei handlePut (siehe dortigen Kommentar) -
  // ABER: anders als bei Einzelprofilen (die bei jedem gelernten Spell automatisch erneut
  // gepusht werden) gibt es für die Gruppen-Listung selbst KEINEN automatischen Refresh-Trigger
  // (siehe README.md) - eine einmal veröffentlichte Gruppe verschwindet also nach der TTL
  // automatisch, wenn niemand erneut PUT aufruft.
  await env.BLUNION_PROFILES.put(key, JSON.stringify(record), { expirationTtl: resolveTtlSeconds(body.ttlHours) });

  const responseBody: Record<string, unknown> = stripForGroupResponse(record);
  if (plaintextEditTokenForResponse)
    responseBody.editToken = plaintextEditTokenForResponse;

  return jsonResponse(responseBody, existing ? 200 : 201);
}

/** GET /groups/browse?dataCenter=<DC> - öffentlicher Gruppen-Gruppenfinder (Phase 2), analog zu
 * handleBrowse für Einzelprofile (gleiche list()+Cursor-Pagination, gleicher
 * dataCenter+visibility-Filter in-memory), ABER mit einem zusätzlichen Nachlade-Schritt: eine
 * Gruppen-Listung speichert selbst KEINE Spell-Bitmaske (siehe StoredGroupProfile-Doc), daher
 * wird hier pro Treffer für JEDES Mitglied das zugehörige "profile:"-KV-Objekt per get()
 * nachgeladen, um dessen spellBitmaskBase64 einzusetzen. Das bedeutet pro Gruppen-Treffer
 * zusätzlich N Einzelprofil-Lookups (N = Mitgliederzahl) und verschärft damit die bereits bei
 * handleBrowse dokumentierte Browse-Skalierungsgrenze zusätzlich - bewusst nicht optimiert,
 * siehe dortigen Kommentar zum selben Thema. */
async function handleGroupsBrowse(env: Env, request: Request, ctx: ExecutionContext): Promise<Response> {
  // Siehe handleBrowse-Kommentar oben - nur die eigentliche Berechnung läuft hinter withCache.
  return withCache(request, ctx, async () => {
    const url = new URL(request.url);
    const dataCenter = url.searchParams.get("dataCenter");
    if (!dataCenter)
      return errorResponse(400, 'Query-Parameter "dataCenter" fehlt.');

    const normalizedDataCenter = dataCenter.toLowerCase();
    const results: Record<string, unknown>[] = [];

    let cursor: string | undefined;
    do {
      const listResult = await env.BLUNION_PROFILES.list({ prefix: "group:", cursor });

      for (const listedKey of listResult.keys) {
        const stored = await env.BLUNION_PROFILES.get<StoredGroupProfile>(listedKey.name, "json");
        if (!stored)
          continue; // Zwischen list() und get() gelöscht/abgelaufen - überspringen statt Fehler.

        if (stored.visibility !== "listed" || stored.dataCenter.toLowerCase() !== normalizedDataCenter)
          continue;

        // Pro Mitglied das zugehörige Einzelprofil nachladen (siehe Funktionsdoc oben). Fehlt es
        // (gelöscht/abgelaufen/nie gepusht), wird das Mitglied TROTZDEM aufgelistet, nur mit
        // spellBitmaskBase64: null - NICHT der ganze Gruppen-Treffer verworfen (analog zum
        // bestehenden "Zwischen list() und get() gelöscht"-Muster oben, hier auf Mitglieder- statt
        // Eintrags-Ebene angewendet).
        const members = await Promise.all(stored.members.map(async (member) => {
          const memberProfile = await env.BLUNION_PROFILES.get<StoredProfile>(
            kvKey(member.world, member.characterName), "json");

          return {
            world: member.world,
            characterName: member.characterName,
            spellBitmaskBase64: memberProfile?.spellBitmaskBase64 ?? null,
          };
        }));

        results.push({
          groupId: stored.groupId,
          members,
          availabilityTags: stored.availabilityTags ?? [],
          note: stored.note ?? "",
          wantedPlayerCount: stored.wantedPlayerCount ?? 0,
        });
      }

      cursor = listResult.list_complete ? undefined : listResult.cursor;
    } while (cursor);

    return jsonResponse(results);
  });
}

/** DELETE /group/:groupId - X-Edit-Token-Header, exakt wie handleDelete für Einzelprofile.
 *
 * WICHTIG: löscht AUSSCHLIESSLICH den "group:"-Eintrag (Mitgliederliste/Tags/Notiz) - rührt NIE
 * an den referenzierten "profile:"-Einträgen der Mitglieder, die bleiben unabhängig davon
 * bestehen (das ist der ganze Punkt des Referenz-statt-Kopie-Ansatzes, siehe Klassendoc/
 * StoredGroupProfile). */
async function handleGroupDelete(env: Env, request: Request, groupId: string): Promise<Response> {
  const token = request.headers.get("X-Edit-Token");
  if (!token)
    return errorResponse(403, 'Header "X-Edit-Token" fehlt.');

  const key = groupKvKey(groupId);
  const existing = await env.BLUNION_PROFILES.get<StoredGroupProfile>(key, "json");
  if (!existing)
    return errorResponse(404, "Keine Gruppen-Listung mit dieser groupId gefunden.");

  const providedHash = await sha256Hex(token);
  if (providedHash !== existing.editTokenHash)
    return errorResponse(403, "editToken stimmt nicht mit der gespeicherten Gruppen-Listung überein.");

  await env.BLUNION_PROFILES.delete(key);
  return jsonResponse({ deleted: true });
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    if (request.method === "OPTIONS")
      return new Response(null, { status: 204, headers: CORS_HEADERS });

    const url = new URL(request.url);

    // Vor dem PROFILE_PATH-Abgleich geprüft, obwohl "/profiles/browse" ohnehin nie auf
    // PROFILE_PATH matchen würde (dessen erstes Segment ist literal "profile", nicht
    // "profiles") - explizit hier oben als eigener Zweig, damit der Gruppenfinder-Endpoint im
    // Code genauso sichtbar/prominent ist wie die drei /profile/-Endpunkte, nicht als
    // Nebenbemerkung im 404-Fallback versteckt.
    if (BROWSE_PATH.test(url.pathname)) {
      if (request.method !== "GET")
        return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);

      return handleBrowse(env, request, ctx);
    }

    // Analog zu BROWSE_PATH oben, nur für den Gruppen-Gruppenfinder (siehe handleGroupsBrowse) -
    // ebenfalls VOR dem GROUP_PATH-Abgleich geprüft (aus demselben Grund: "/groups/browse" würde
    // wegen des zusätzlichen "s" ohnehin nie auf GROUP_PATH matchen, aber explizit oben bleibt
    // der Endpoint im Code genauso sichtbar wie /group/:groupId).
    if (GROUPS_BROWSE_PATH.test(url.pathname)) {
      if (request.method !== "GET")
        return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);

      return handleGroupsBrowse(env, request, ctx);
    }

    const profileMatch = url.pathname.match(PROFILE_PATH);

    if (profileMatch) {
      // decodeURIComponent statt der rohen Pfadsegmente: Charakternamen enthalten oft
      // Leerzeichen ("Y'shtola Rhul") und ggf. Apostrophe/Unicode - das Plugin MUSS beim Aufbau
      // der URL encodeURIComponent verwenden (siehe LiveSyncService.cs), hier entsprechend
      // wieder dekodiert.
      const world = decodeURIComponent(profileMatch[1]!);
      const characterName = decodeURIComponent(profileMatch[2]!);

      if (world.length === 0 || characterName.length === 0)
        return errorResponse(400, "world und characterName dürfen nicht leer sein.");

      switch (request.method) {
        case "GET":
          return handleGet(env, world, characterName);
        case "PUT":
          return handlePut(env, request, world, characterName);
        case "DELETE":
          return handleDelete(env, request, world, characterName);
        default:
          return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);
      }
    }

    const groupMatch = url.pathname.match(GROUP_PATH);

    if (groupMatch) {
      const groupId = decodeURIComponent(groupMatch[1]!);

      if (groupId.length === 0)
        return errorResponse(400, "groupId darf nicht leer sein.");

      switch (request.method) {
        case "PUT":
          return handleGroupPut(env, request, groupId);
        case "DELETE":
          return handleGroupDelete(env, request, groupId);
        default:
          return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);
      }
    }

    return errorResponse(
      404,
      'Unbekannter Endpoint - erwartet wird "/profile/:world/:characterName", "/profiles/browse", ' +
        '"/group/:groupId" oder "/groups/browse".',
    );
  },
};
