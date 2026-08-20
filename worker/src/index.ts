/**
 * BLUnion Live-Sync Worker (Phase 1 + Phase 2 "Gruppenfinder").
 *
 * Ersetzt für den Alltagsfall "gemeinsam mit anderen Blue Mages in einer Party" den manuellen
 * BLU:-Code-Austausch (siehe ManualCodeSyncProvider.cs im Plugin) durch automatisches
 * Veröffentlichen/Abrufen des eigenen Spell-Status über diesen Worker + einen KV-Namespace.
 *
 * Bewusst OHNE Framework/Router-Bibliothek (siehe Aufgabenstellung "keine unnötigen
 * Abhängigkeiten") - für 4 Endpunkte reicht ein einfacher switch/Regex-Abgleich völlig.
 *
 * Endpunkte (siehe README.md für Details/Beispiele):
 *   PUT    /profile/:world/:characterName   - eigenes Profil anlegen/aktualisieren
 *   GET    /profile/:world/:characterName   - Profil abrufen (öffentlich, kein Token nötig)
 *   DELETE /profile/:world/:characterName   - eigenes Profil löschen (X-Edit-Token-Header nötig)
 *   GET    /profiles/browse?dataCenter=<DC> - öffentlicher Gruppenfinder (Phase 2, siehe unten)
 *
 * KV-Key-Schema: "profile:<world>:<characterName>", beide Teile lowercased (siehe kvKey()) -
 * verhindert Duplikate durch abweichende Groß-/Kleinschreibung (das Spiel liefert Namen/Welten
 * nicht immer konsistent kapitalisiert). :world/:characterName in der URL sind
 * URI-komponenten-kodiert zu übergeben (Charakternamen können Leer-/Sonderzeichen enthalten).
 *
 * WICHTIG (Phase 2 - Gruppenfinder ist KEIN separates Profil): visibility/availabilityTags/
 * note/wantedPlayerCount erweitern dasselbe StoredProfile, das schon Phase 1 anlegt. Es gibt
 * keinen eigenständigen "Gruppenfinder-Login" - Sichtbarkeit im Gruppenfinder setzt zwingend
 * voraus, dass für den Charakter bereits ein Live-Sync-Profil (mit editToken) existiert.
 *
 * Sowohl das Dalamud-Plugin ALS AUCH die Website (docs/index.html) sind Clients DIESES EINEN
 * Workers - kein zweites Backend, keine Parallelstruktur. Der optionale PUT-Body-Parameter
 * ttlHours (siehe resolveTtlSeconds) existiert eigens für die Website, die damit Profile mit
 * einer kürzeren als der Standard-Lebensdauer veröffentlichen kann.
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

const PROFILE_PATH = /^\/profile\/([^/]+)\/([^/]+)\/?$/;
const BROWSE_PATH = /^\/profiles\/browse\/?$/;

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

function kvKey(world: string, characterName: string): string {
  return `profile:${world.toLowerCase()}:${characterName.toLowerCase()}`;
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
async function handleBrowse(env: Env, request: Request): Promise<Response> {
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

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
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

      return handleBrowse(env, request);
    }

    const match = url.pathname.match(PROFILE_PATH);

    if (!match) {
      return errorResponse(
        404,
        'Unbekannter Endpoint - erwartet wird "/profile/:world/:characterName" oder "/profiles/browse".',
      );
    }

    // decodeURIComponent statt der rohen Pfadsegmente: Charakternamen enthalten oft Leerzeichen
    // ("Y'shtola Rhul") und ggf. Apostrophe/Unicode - das Plugin MUSS beim Aufbau der URL
    // encodeURIComponent verwenden (siehe LiveSyncService.cs), hier entsprechend wieder dekodiert.
    const world = decodeURIComponent(match[1]!);
    const characterName = decodeURIComponent(match[2]!);

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
  },
};
