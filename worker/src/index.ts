/**
 * BLUnion Live-Sync Worker (Phase 1 + Phase 2 "Gruppenfinder"). Ersetzt den manuellen
 * BLU:-Code-Austausch durch automatisches Veröffentlichen/Abrufen über Worker + KV. Bewusst ohne
 * Framework/Router - dafür reicht ein einfacher switch/Regex-Abgleich.
 *
 * Endpunkte (siehe README.md):
 *   PUT    /profile/:world/:characterName   - Profil anlegen/aktualisieren
 *   GET    /profile/:world/:characterName   - Profil abrufen (öffentlich)
 *   DELETE /profile/:world/:characterName   - Profil löschen (X-Edit-Token)
 *   GET    /profiles/browse?dataCenter=<DC> - Gruppenfinder (Phase 2)
 *   PUT    /group/:groupId                  - Gruppen-Listung anlegen/aktualisieren
 *   DELETE /group/:groupId                  - Gruppen-Listung löschen (X-Edit-Token)
 *   GET    /groups/browse?dataCenter=<DC>   - Gruppen-Gruppenfinder
 *   POST   /discord/interactions            - Discord-Slash-Command "/blunion browse" (nur lesend)
 *
 * Gruppenfinder ist kein separates Profil: visibility/availabilityTags/note/wantedPlayerCount/
 * targetSpellIds erweitern dasselbe StoredProfile aus Phase 1, kein eigener "Login". Eine
 * Gruppen-Listung referenziert ihre Mitglieder nur über world+characterName, ohne Kopie der
 * Spell-Bitmaske (die wird bei GET /groups/browse live nachgeladen).
 *
 * Plugin UND Web-Companion (docs/index.html) sind Clients DIESES EINEN Workers.
 */

import { base64UrlDecode, generateEditToken, hexToBytes, sha256Hex } from "./crypto";
import { createWebhookMessage, deleteWebhookMessage, editWebhookMessage, extractWebhookId } from "./discordWebhook";
import { lookupCanonicalDataCenter, lookupDataCenter } from "./worlds";
import { KNOWN_SPELL_IDS } from "./spellIds";
import { SPELL_ORDER_BY_ID } from "./spellOrder";

export interface Env {
  BLUNION_PROFILES: KVNamespace;
  /** Rate-Limiting-Binding für die vier schreibenden Endpunkte (siehe enforceWriteRateLimit). */
  WRITE_RATE_LIMITER: RateLimit;
  /** Rate-Limiting-Binding für die lesenden Endpunkte GET /profile/..., /profiles/browse und
   * /groups/browse (siehe enforceReadRateLimit). */
  READ_RATE_LIMITER: RateLimit;
  /** Ed25519-Public-Key der Discord-App (Hex) - Secret, nie im Code (wrangler secret put
   * DISCORD_PUBLIC_KEY, lokal via .dev.vars). */
  DISCORD_PUBLIC_KEY: string;
  /** Regions-Webhooks für die persistenten Discord-Gruppen-Karten (Secrets wie oben). Anders als
   * DISCORD_PUBLIC_KEY optional: eine nicht konfigurierte Region deaktiviert nur die Kartenfunktion
   * dort, statt den Worker abstürzen oder den PUT/DELETE fehlschlagen zu lassen. */
  DISCORD_WEBHOOK_NA?: string;
  DISCORD_WEBHOOK_EU?: string;
  DISCORD_WEBHOOK_JP?: string;
  DISCORD_WEBHOOK_OC?: string;
  /** Für den klickbaren Discord-Link im Plugin (siehe buildDiscordChannelLink) - anders als die
   * Webhook-URLs oben KEIN Secret (Guild-/Channel-IDs sind nicht geheim), daher als normale [vars]
   * statt per "wrangler secret put" gesetzt. Fehlt Guild-ID oder die Channel-ID einer Region, fällt
   * der Link auf die allgemeine Server-Einladung zurück statt einen Deep-Link zu bauen. */
  DISCORD_GUILD_ID?: string;
  DISCORD_CHANNEL_NA?: string;
  DISCORD_CHANNEL_EU?: string;
  DISCORD_CHANNEL_JP?: string;
  DISCORD_CHANNEL_OC?: string;
  /** Nur für die Anzeige im Plugin ("Discord: #<name>") - rein kosmetisch, keine Logik hängt daran
   * (fehlt der Name, zeigt das Plugin den Hinweis ohne Kanalnamen). */
  DISCORD_CHANNEL_NAME_NA?: string;
  DISCORD_CHANNEL_NAME_EU?: string;
  DISCORD_CHANNEL_NAME_JP?: string;
  DISCORD_CHANNEL_NAME_OC?: string;
  /** Feature-Flag für den Gruppen-Ersteller-Nachweis (siehe verifyGroupCreator): "true" macht
   * creatorWorld/creatorCharacterName/X-Creator-Token bei PUT /group zur Pflicht. Bei jedem anderen
   * Wert (Default "false" in wrangler.toml) werden mitgeschickte Creator-Angaben trotzdem voll
   * geprüft und gespeichert, fehlende aber toleriert (ältere Plugin-/Website-Versionen). */
  REQUIRE_GROUP_CREATOR?: string;
  /** Moderations-Secret für die /admin/*-Endpunkte (siehe handleAdminRequest) - nur per
   * "wrangler secret put ADMIN_TOKEN" bzw. lokal über .dev.vars. Fehlt es, antworten alle
   * Admin-Endpunkte mit demselben 404 wie ein unbekannter Pfad. */
  ADMIN_TOKEN?: string;
}

/** Muss mit ManualCodeSyncProvider.BitmaskBytes im Plugin übereinstimmen (16 Byte = 128 Bit). */
const BITMASK_BYTES = 16;

/** 90 Tage - wird bei jedem Put erneuert, aktive Profile laufen nie ab (siehe README.md). */
const PROFILE_TTL_SECONDS = 90 * 24 * 3600;

/** Optionales PUT-Override (genutzt von docs/index.html) - geclampt statt mit 400 abgelehnt, da
 * reine Optimierung ohne Sicherheitsrelevanz; TTL_HOURS_MAX = PROFILE_TTL_SECONDS, ein Client kann
 * die reguläre Lebensdauer also nie verlängern, nur verkürzen. */
const TTL_HOURS_MIN = 1;
const TTL_HOURS_MAX = 2160; // 90 Tage * 24 Stunden

/** Cache-TTL für GET /profiles/browse und GET /groups/browse (siehe withCache) - etwas länger als
 * das 15s-Client-Polling-Intervall. */
const BROWSE_CACHE_TTL_SECONDS = 20;

const PROFILE_PATH = /^\/profile\/([^/]+)\/([^/]+)\/?$/;
const BROWSE_PATH = /^\/profiles\/browse\/?$/;
const GROUP_PATH = /^\/group\/([^/]+)\/?$/;
const GROUPS_BROWSE_PATH = /^\/groups\/browse\/?$/;
const DISCORD_INTERACTIONS_PATH = /^\/discord\/interactions\/?$/;
const ADMIN_PATH_PREFIX = "/admin/";
const ADMIN_PROFILE_PATH = /^\/admin\/profile\/([^/]+)\/([^/]+)\/?$/;
const ADMIN_GROUP_PATH = /^\/admin\/group\/([^/]+)\/?$/;

/** Plugin erzeugt Guid.NewGuid(), Web-Companion crypto.randomUUID() - beides ist eine UUID. */
const GROUP_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** FFXIV-Namensregeln: Vor- und Nachname, je 2-15 Zeichen aus Buchstaben/Apostroph/Bindestrich,
 * beginnend mit einem Buchstaben, genau ein Leerzeichen dazwischen, insgesamt max. 21 Zeichen. */
const CHARACTER_NAME_PATTERN = /^[A-Za-z][A-Za-z'-]{1,14} [A-Za-z][A-Za-z'-]{1,14}$/;
const CHARACTER_NAME_MAX_LENGTH = 21;

export function isValidCharacterName(name: string): boolean {
  return name.length <= CHARACTER_NAME_MAX_LENGTH && CHARACTER_NAME_PATTERN.test(name);
}

export function isValidGroupId(groupId: string): boolean {
  return GROUP_ID_PATTERN.test(groupId);
}

const INVALID_CHARACTER_NAME_MESSAGE =
  "Ungültiger Charaktername - erwartet werden Vor- und Nachname (je 2-15 Buchstaben, ' oder -, " +
  "beginnend mit einem Buchstaben), getrennt durch genau ein Leerzeichen, insgesamt max. 21 Zeichen.";

const CORS_HEADERS: Record<string, string> = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, PUT, DELETE, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, X-Edit-Token, X-Creator-Token",
  "Access-Control-Max-Age": "86400",
};

/** Intern englisch, Übersetzung passiert ausschließlich im Plugin über UiStrings. */
const ALLOWED_AVAILABILITY_TAGS = ["morning", "afternoon", "evening", "weekend", "flexible"] as const;

/** Wird bei Überschreitung gekappt statt abgelehnt - das Plugin begrenzt die Eingabe zwar schon
 * selbst, ein direkter API-Aufruf könnte das aber umgehen. */
const NOTE_MAX_LENGTH = 60;

/** 0 = "egal wie viele"; 8 = maximale Party-/Alliance-Größe. */
const WANTED_PLAYER_COUNT_MIN = 0;
const WANTED_PLAYER_COUNT_MAX = 8;

const GROUP_MEMBER_COUNT_MIN = 1;
const GROUP_MEMBER_COUNT_MAX = 8;

/** Für den Link-Button im Discord-Browse-Embed (siehe buildGroupsBrowseEmbed). */
const COMPANION_WEBSITE_URL = "https://letsi-ma.github.io/BLUnion/";

// Discord-Interaction-/Response-Typen, siehe deren API-Doku - nur die für Phase 1 gebrauchten Werte.
const DISCORD_INTERACTION_TYPE_PING = 1;
const DISCORD_INTERACTION_TYPE_APPLICATION_COMMAND = 2;
const DISCORD_OPTION_TYPE_SUB_COMMAND = 1;
const DISCORD_RESPONSE_TYPE_PONG = 1;
const DISCORD_RESPONSE_TYPE_CHANNEL_MESSAGE_WITH_SOURCE = 4;
/** Aktuell ungenutzt, für eine spätere schreibende Discord-Command-Phase vorgehalten. */
const DISCORD_RESPONSE_TYPE_DEFERRED_CHANNEL_MESSAGE_WITH_SOURCE = 5;

/** Bewusst deutlich unter der Gesamtzahl bekannter Spells - ein konkretes Session-Ziel, kein
 * Abbild der kompletten Spellliste. */
const TARGET_SPELL_COUNT_MAX = 30;

/** FFXIV-Region ("physische" Server-Region, NICHT das einzelne Data Center). */
type Region = "NA" | "EU" | "JP" | "OC";

const ALL_REGIONS: readonly Region[] = ["NA", "EU", "JP", "OC"];

/** Data-Center -> Region für die persistenten Discord-Gruppen-Karten - Region statt Data Center,
 * weil Spieler per World Visit innerhalb einer Region jederzeit wechseln können, zwischen Regionen
 * aber nicht; ein Kanal pro Data Center würde dieselbe Spielerschaft künstlich verstreuen. Bewusst
 * ohne "Shadow" (temporäres, 2024 wieder geschlossenes Data Center, das lookupDataCenter() nie
 * zurückliefert). */
const DATA_CENTER_TO_REGION: Readonly<Record<string, Region>> = {
  // North America
  Aether: "NA",
  Crystal: "NA",
  Dynamis: "NA",
  Primal: "NA",

  // Europe
  Chaos: "EU",
  Light: "EU",

  // Japan
  Elemental: "JP",
  Gaia: "JP",
  Mana: "JP",
  Meteor: "JP",

  // Oceania
  Materia: "OC",
};

/** Case-insensitiv wie lookupDataCenter() in worlds.ts; null bei unbekanntem Data Center statt
 * Wurf. */
export function regionForDataCenter(dataCenter: string): Region | null {
  const normalized = dataCenter.toLowerCase();
  for (const [dc, region] of Object.entries(DATA_CENTER_TO_REGION)) {
    if (dc.toLowerCase() === normalized)
      return region;
  }
  return null;
}

function getRegionWebhookUrl(env: Env, region: Region): string | undefined {
  switch (region) {
    case "NA": return env.DISCORD_WEBHOOK_NA;
    case "EU": return env.DISCORD_WEBHOOK_EU;
    case "JP": return env.DISCORD_WEBHOOK_JP;
    case "OC": return env.DISCORD_WEBHOOK_OC;
  }
}

function getRegionChannelId(env: Env, region: Region): string | undefined {
  switch (region) {
    case "NA": return env.DISCORD_CHANNEL_NA;
    case "EU": return env.DISCORD_CHANNEL_EU;
    case "JP": return env.DISCORD_CHANNEL_JP;
    case "OC": return env.DISCORD_CHANNEL_OC;
  }
}

function getRegionChannelName(env: Env, region: Region): string | undefined {
  switch (region) {
    case "NA": return env.DISCORD_CHANNEL_NAME_NA;
    case "EU": return env.DISCORD_CHANNEL_NAME_EU;
    case "JP": return env.DISCORD_CHANNEL_NAME_JP;
    case "OC": return env.DISCORD_CHANNEL_NAME_OC;
  }
}

/** Statischer Server-Invite als Fallback (siehe buildDiscordChannelLink) - IMMER klickbar, auch
 * ohne jede Region-Konfiguration. */
const DISCORD_INVITE_FALLBACK_URL = "https://discord.gg/uGW7kBG67";

interface DiscordChannelLink {
  url: string;
  /** null, wenn für die Region kein DISCORD_CHANNEL_NAME_* gesetzt ist - rein kosmetisch, siehe
   * Env.DISCORD_CHANNEL_NAME_*-Doc. */
  channelName: string | null;
}

/** Klickbarer Discord-Link für eine aktuell gelistete Veröffentlichung (Profil ODER Gruppe, siehe
 * stripForResponse/stripForGroupResponse) - Deep-Link direkt in den Region-Kanal, wenn Guild-ID UND
 * Channel-ID dafür konfiguriert sind, sonst die allgemeine Server-Einladung. null nur, wenn gar
 * keine Karte existieren kann (nicht "listed" oder Data Center nicht auflösbar). */
function buildDiscordChannelLink(
  env: Env, dataCenter: string, visibility: "listed" | "unlisted",
): DiscordChannelLink | null {
  if (visibility !== "listed")
    return null;

  const region = regionForDataCenter(dataCenter);
  if (!region)
    return null;

  const channelId = getRegionChannelId(env, region);
  const channelName = getRegionChannelName(env, region) ?? null;

  const url = env.DISCORD_GUILD_ID && channelId
    ? `https://discord.com/channels/${env.DISCORD_GUILD_ID}/${channelId}`
    : DISCORD_INVITE_FALLBACK_URL;

  return { url, channelName };
}

/** In KV gespeichertes JSON (siehe README.md). editTokenHash verlässt die Datei nie Richtung
 * Client. Phase-2-Felder (availabilityTags/note/wantedPlayerCount/targetSpellIds) sind optional,
 * da ältere Einträge sie noch nicht kennen (KV hat kein Schema/keine Migration). */
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
  /** Spieler-Pendant zu StoredGroupProfile.targetSpellIds - Spells, die dieser Solo-Spieler selbst
   * farmen möchte. */
  targetSpellIds?: number[];
  /** Persistente Discord-Kanal-Karte dieses Profils (siehe syncPlayerDiscordCard). */
  discordCard?: DiscordCard;
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
  targetSpellIds?: unknown;
  ttlHours?: unknown;
}

/** Mitglied einer Gruppen-Listung - nur world+characterName, keine Kopie der Bitmaske (die wird
 * erst bei GET /groups/browse live nachgeladen). world ist bereits die kanonische Schreibweise. */
interface GroupMember {
  world: string;
  characterName: string;
}

/** In KV unter "group:<groupId>" gespeichert, editTokenHash verlässt die Datei nie Richtung
 * Client. editTokenHash identifiziert NUR den Ersteller, NICHT die members[] - nur der Ersteller
 * kann später ändern/löschen (bewusst, kein Bug). dataCenter wird vom world des ersten Mitglieds
 * hergeleitet. Phase-2-Felder optional wie bei StoredProfile. */
interface StoredGroupProfile {
  groupId: string;
  members: GroupMember[];
  editTokenHash: string;
  visibility: "listed" | "unlisted";
  availabilityTags?: string[];
  note?: string;
  wantedPlayerCount?: number;
  targetSpellIds?: number[];
  dataCenter: string;
  /** Ersteller mit nachgewiesenem eigenem Live-Sync-Profil (siehe verifyGroupCreator). Optional,
   * da ältere Einträge/Clients ihn nicht kennen. Verlässt KV nie - weder in PUT-/Browse-Antworten
   * (Allowlist in stripForGroupResponse/computeGroupsBrowse) noch in Discord-Embeds. */
  creator?: GroupMember;
  discordCard?: DiscordCard;
  createdAt: string;
  updatedAt: string;
}

/** channelWebhookId identifiziert nur den zuletzt genutzten Webhook, nie den Token selbst (der
 * bleibt im Env.DISCORD_WEBHOOK_*-Secret und landet nie in KV). */
interface DiscordCard {
  region: Region;
  channelWebhookId: string;
  messageId: string;
}

interface PutGroupRequestBody {
  members?: unknown;
  editToken?: unknown;
  creatorWorld?: unknown;
  creatorCharacterName?: unknown;
  visibility?: unknown;
  availabilityTags?: unknown;
  note?: unknown;
  wantedPlayerCount?: unknown;
  targetSpellIds?: unknown;
  ttlHours?: unknown;
}

/** Minimales Interaction-Schema, nur die für Phase 1 gebrauchten Felder (Discord schickt deutlich
 * mehr, z.B. guild_id/member/channel_id). */
interface DiscordInteraction {
  type: number;
  data?: DiscordInteractionCommandData;
}

/** Ein Sub-Command-Aufruf ("/blunion browse ...") verschachtelt seine Parameter: das äußere
 * options[] enthält EIN SUB_COMMAND-Element, dessen eigene options[] die echten Parameter trägt
 * (siehe findDiscordSubcommand/findDiscordStringOption). */
interface DiscordInteractionCommandData {
  name: string;
  options?: DiscordInteractionOption[];
}

interface DiscordInteractionOption {
  name: string;
  type: number;
  value?: string | number | boolean;
  options?: DiscordInteractionOption[];
}

/** Response-Shape von computeGroupsBrowse, separat typisiert fürs Discord-Embed (siehe
 * buildGroupsBrowseEmbed). */
interface DiscordBrowseGroupMember {
  world: string;
  characterName: string;
}

interface DiscordBrowseGroup {
  groupId: string;
  members: DiscordBrowseGroupMember[];
  availabilityTags: string[];
  note: string;
  wantedPlayerCount: number;
  targetSpellIds: number[];
}

/** Fehlt ttlHours oder ist kein gültiger Zahlenwert, greift PROFILE_TTL_SECONDS; sonst geclampt
 * auf TTL_HOURS_MIN/MAX. */
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

/** Cache-API (per Edge-Standort, kein globaler Cache) nur für die Browse-Endpunkte, nie für
 * PUT/DELETE. response.clone() vor cache.put() ist zwingend - der Body ist sonst nur einmal lesbar
 * und cache.put()/die zurückgegebene Response würden sich einen bereits konsumierten Stream teilen.
 * Cache-Key ist NICHT die Original-URL, sondern origin + pathname + kanonisches dataCenter - sonst
 * könnte jeder beliebige Zusatzparameter (?x=<zufall>) den Cache umgehen und pro Aufruf einen
 * kompletten KV-Scan auslösen. */
async function withCache(
  request: Request,
  canonicalDataCenter: string,
  ctx: ExecutionContext,
  compute: () => Promise<Response>,
): Promise<Response> {
  const url = new URL(request.url);
  const cacheKey = new Request(
    `${url.origin}${url.pathname}?dataCenter=${encodeURIComponent(canonicalDataCenter)}`, { method: "GET" });

  const cache = caches.default;
  const cached = await cache.match(cacheKey);
  if (cached)
    return cached;

  const response = await compute();
  if (response.ok) {
    const cacheableResponse = new Response(response.body, response);
    cacheableResponse.headers.set("Cache-Control", `public, max-age=${BROWSE_CACHE_TTL_SECONDS}`);
    ctx.waitUntil(cache.put(cacheKey, cacheableResponse.clone()));
    return cacheableResponse;
  }

  return response;
}

/** IP-basiertes Rate-Limiting für die schreibenden Endpunkte über das native Workers-Binding
 * WRITE_RATE_LIMITER (siehe wrangler.toml) statt Dashboard-"Rate Limiting Rules" - die sind ein
 * Zonen-/WAF-Feature und setzen eine eigene Zone voraus, dieser Worker läuft aber zonenlos unter
 * *.workers.dev. Fehlt der CF-Connecting-IP-Header (z.B. lokal ohne echten Edge davor), wird NICHT
 * limitiert statt mit 500 zu antworten. Rückgabe: fertige 429-Response oder null (weitermachen). */
async function enforceWriteRateLimit(env: Env, request: Request): Promise<Response | null> {
  return enforceRateLimit(env.WRITE_RATE_LIMITER, request);
}

/** Lese-Pendant zu enforceWriteRateLimit (Binding READ_RATE_LIMITER, siehe wrangler.toml) für
 * GET /profile/..., /profiles/browse und /groups/browse - gleiche Logik, eigenes Kontingent. */
async function enforceReadRateLimit(env: Env, request: Request): Promise<Response | null> {
  return enforceRateLimit(env.READ_RATE_LIMITER, request);
}

async function enforceRateLimit(limiter: RateLimit, request: Request): Promise<Response | null> {
  const ip = request.headers.get("CF-Connecting-IP");
  if (!ip)
    return null;

  const { success } = await limiter.limit({ key: ip });
  if (!success)
    return errorResponse(429, "Zu viele Anfragen von dieser IP - bitte kurz warten und erneut versuchen.");

  return null;
}

/** Gemeinsame dataCenter-Prüfung für /profiles/browse, /groups/browse und den Discord-Browse -
 * liefert die kanonische Schreibweise (siehe lookupCanonicalDataCenter) oder eine fertige
 * 400-Response. */
function resolveBrowseDataCenter(dataCenter: string | null | undefined): string | Response {
  if (!dataCenter)
    return errorResponse(400, 'Query-Parameter "dataCenter" fehlt.');

  const canonicalDataCenter = lookupCanonicalDataCenter(dataCenter);
  if (!canonicalDataCenter)
    return errorResponse(400, "Unbekanntes Data Center.");

  return canonicalDataCenter;
}

function kvKey(world: string, characterName: string): string {
  return `profile:${world.toLowerCase()}:${characterName.toLowerCase()}`;
}

/** Anders als kvKey() NICHT lowercased - groupId ist ein zufälliger, vom Client generierter
 * String ohne natürlichen Schlüssel. */
function groupKvKey(groupId: string): string {
  return `group:${groupId}`;
}

function discordCardsIndexKey(region: Region): string {
  return `discordcards:${region}`;
}

/** Separater Index nötig: ein abgelaufener group:-Eintrag verschwindet still per KV-TTL, ohne
 * diesen Index bliebe eine verwaiste Karte für immer unentdeckt (siehe
 * cleanupOrphanedDiscordCards). Bewusst ohne eigenes TTL, sonst könnte der Index-Eintrag selbst
 * zuerst verschwinden. */
interface DiscordCardIndexEntry {
  groupId: string;
  messageId: string;
}

/** Gemeinsame Add-Logik für beide Karten-Indizes (Gruppen/Spieler) - isSameEntry kapselt den
 * einzigen Unterschied (welches Feld identifiziert einen Eintrag). */
async function addCardIndexEntry<T>(
  env: Env, indexKey: string, isSameEntry: (entry: T) => boolean, newEntry: T,
): Promise<void> {
  const existing = (await env.BLUNION_PROFILES.get<T[]>(indexKey, "json")) ?? [];
  const withoutExisting = existing.filter((entry) => !isSameEntry(entry));
  withoutExisting.push(newEntry);
  await env.BLUNION_PROFILES.put(indexKey, JSON.stringify(withoutExisting));
}

/** Tut nichts, wenn kein passender Eintrag existiert (kein unnötiger Schreibzugriff). */
async function removeCardIndexEntry<T>(
  env: Env, indexKey: string, isSameEntry: (entry: T) => boolean,
): Promise<void> {
  const existing = await env.BLUNION_PROFILES.get<T[]>(indexKey, "json");
  if (!existing)
    return;

  const filtered = existing.filter((entry) => !isSameEntry(entry));
  if (filtered.length === existing.length)
    return;

  await env.BLUNION_PROFILES.put(indexKey, JSON.stringify(filtered));
}

async function addDiscordCardIndexEntry(env: Env, region: Region, groupId: string, messageId: string): Promise<void> {
  await addCardIndexEntry<DiscordCardIndexEntry>(
    env, discordCardsIndexKey(region), (entry) => entry.groupId === groupId, { groupId, messageId });
}

async function removeDiscordCardIndexEntry(env: Env, region: Region, groupId: string): Promise<void> {
  await removeCardIndexEntry<DiscordCardIndexEntry>(
    env, discordCardsIndexKey(region), (entry) => entry.groupId === groupId);
}

export function playerCardsIndexKey(region: Region): string {
  return `playercards:${region}`;
}

/** Spieler-Pendant zu DiscordCardIndexEntry - eigener Index (statt Mitbenutzung von
 * discordcards:<region>), da ein Spieler-Profil über world+characterName statt groupId
 * identifiziert wird. Exportiert für direkte Unit-Tests. */
export interface PlayerCardIndexEntry {
  world: string;
  characterName: string;
  messageId: string;
}

export async function addPlayerCardIndexEntry(
  env: Env, region: Region, world: string, characterName: string, messageId: string,
): Promise<void> {
  await addCardIndexEntry<PlayerCardIndexEntry>(
    env, playerCardsIndexKey(region),
    (entry) => entry.world === world && entry.characterName === characterName,
    { world, characterName, messageId },
  );
}

export async function removePlayerCardIndexEntry(
  env: Env, region: Region, world: string, characterName: string,
): Promise<void> {
  await removeCardIndexEntry<PlayerCardIndexEntry>(
    env, playerCardsIndexKey(region),
    (entry) => entry.world === world && entry.characterName === characterName,
  );
}

/** Allowlist statt Blocklist - editTokenHash verlässt die Datei nie. */
function stripForResponse(env: Env, stored: StoredProfile) {
  const discordChannelLink = buildDiscordChannelLink(env, stored.dataCenter, stored.visibility);

  return {
    characterName: stored.characterName,
    world: stored.world,
    dataCenter: stored.dataCenter,
    spellBitmaskBase64: stored.spellBitmaskBase64,
    visibility: stored.visibility,
    availabilityTags: stored.availabilityTags ?? [],
    note: stored.note ?? "",
    wantedPlayerCount: stored.wantedPlayerCount ?? 0,
    targetSpellIds: stored.targetSpellIds ?? [],
    updatedAt: stored.updatedAt,
    discordChannelUrl: discordChannelLink?.url ?? null,
    discordChannelName: discordChannelLink?.channelName ?? null,
  };
}

/** Wie stripForResponse, aber ohne dataCenter/visibility (redundant für den Browse-Aufrufer). */
function stripForBrowseResponse(stored: StoredProfile) {
  return {
    characterName: stored.characterName,
    world: stored.world,
    spellBitmaskBase64: stored.spellBitmaskBase64,
    availabilityTags: stored.availabilityTags ?? [],
    note: stored.note ?? "",
    wantedPlayerCount: stored.wantedPlayerCount ?? 0,
    targetSpellIds: stored.targetSpellIds ?? [],
    updatedAt: stored.updatedAt,
  };
}

/** Wie stripForResponse, für PUT/DELETE-Antworten auf /group/:groupId. GET /groups/browse baut
 * members[] stattdessen individuell in handleGroupsBrowse zusammen (mit nachgeladener Bitmaske). */
function stripForGroupResponse(env: Env, stored: StoredGroupProfile) {
  const discordChannelLink = buildDiscordChannelLink(env, stored.dataCenter, stored.visibility);

  return {
    groupId: stored.groupId,
    members: stored.members,
    dataCenter: stored.dataCenter,
    visibility: stored.visibility,
    availabilityTags: stored.availabilityTags ?? [],
    note: stored.note ?? "",
    wantedPlayerCount: stored.wantedPlayerCount ?? 0,
    targetSpellIds: stored.targetSpellIds ?? [],
    updatedAt: stored.updatedAt,
    discordChannelUrl: discordChannelLink?.url ?? null,
    discordChannelName: discordChannelLink?.channelName ?? null,
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

/** Von Profilen UND Gruppen gemeinsam genutzt - jede ID muss in KNOWN_SPELL_IDS existieren. */
function isValidTargetSpellIds(value: unknown): value is number[] {
  return Array.isArray(value)
    && value.length <= TARGET_SPELL_COUNT_MAX
    && value.every((id) => typeof id === "number" && Number.isInteger(id) && KNOWN_SPELL_IDS.has(id));
}

/** Nur Form-/Typ-Prüfung; ob world über lookupDataCenter auflösbar ist, prüft der Aufrufer selbst
 * (braucht dafür eine spezifischere Fehlermeldung). */
function isValidRawGroupMember(value: unknown): value is { world: string; characterName: string } {
  if (typeof value !== "object" || value === null)
    return false;

  const candidate = value as Record<string, unknown>;
  return typeof candidate.world === "string" && candidate.world.length > 0
    && typeof candidate.characterName === "string" && candidate.characterName.length > 0;
}

interface Phase2Fields {
  visibility: "listed" | "unlisted";
  availabilityTags: string[];
  note: string;
  wantedPlayerCount: number;
  targetSpellIds: number[];
}

/** StoredProfile/StoredGroupProfile erfüllen diese Form bereits strukturell. */
interface ExistingPhase2Fields {
  visibility: "listed" | "unlisted";
  availabilityTags?: string[];
  note?: string;
  wantedPlayerCount?: number;
  targetSpellIds?: number[];
}

/** Validiert die fünf Phase-2-Felder aus einem PUT-Body (vorher dupliziert zwischen handlePut und
 * handleGroupPut) - fehlt ein Feld, bleibt der bisherige Wert aus `existing` erhalten (bzw. der
 * Default für einen neuen Datensatz), ein vorhandenes aber ungültiges Feld liefert sofort 400.
 * Rückgabe ist entweder die validierten Felder oder eine fertige Response, die der Aufrufer per
 * `instanceof Response` erkennen und durchreichen muss. */
function resolvePhase2Fields(
  body: {
    visibility?: unknown;
    availabilityTags?: unknown;
    note?: unknown;
    wantedPlayerCount?: unknown;
    targetSpellIds?: unknown;
  },
  existing: ExistingPhase2Fields | null,
): Phase2Fields | Response {
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
    note = body.note.slice(0, NOTE_MAX_LENGTH); // gekappt statt abgelehnt
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

  let targetSpellIds: number[];
  if (body.targetSpellIds === undefined) {
    targetSpellIds = existing?.targetSpellIds ?? [];
  } else if (isValidTargetSpellIds(body.targetSpellIds)) {
    targetSpellIds = body.targetSpellIds;
  } else {
    return errorResponse(
      400,
      `targetSpellIds muss ein Array aus höchstens ${TARGET_SPELL_COUNT_MAX} gültigen, bekannten Spell-IDs sein.`,
    );
  }

  return { visibility, availabilityTags, note, wantedPlayerCount, targetSpellIds };
}

async function handleGet(env: Env, request: Request, world: string, characterName: string): Promise<Response> {
  const rateLimited = await enforceReadRateLimit(env, request);
  if (rateLimited)
    return rateLimited;

  const stored = await env.BLUNION_PROFILES.get<StoredProfile>(kvKey(world, characterName), "json");

  if (!stored)
    return errorResponse(404, "Kein Profil für diese World/diesen Charakternamen gefunden.");

  return jsonResponse(stripForResponse(env, stored));
}

async function handlePut(
  env: Env, request: Request, world: string, characterName: string, ctx: ExecutionContext,
): Promise<Response> {
  const rateLimited = await enforceWriteRateLimit(env, request);
  if (rateLimited)
    return rateLimited;

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

  const phase2Fields = resolvePhase2Fields(body, existing);
  if (phase2Fields instanceof Response)
    return phase2Fields;

  const { visibility, availabilityTags, note, wantedPlayerCount, targetSpellIds } = phase2Fields;

  let editTokenHash: string;
  let createdAt: string;
  let plaintextEditTokenForResponse: string | undefined; // nur beim Neuanlegen gesetzt, siehe unten

  if (existing) {
    const providedToken = typeof body.editToken === "string" ? body.editToken : null;
    // reason ist maschinenlesbar, damit Plugin/Website "Name gehört jemand anderem" (name_taken,
    // kein eigenes Token) von "gespeichertes Token veraltet" (token_mismatch) unterscheiden können.
    if (!providedToken) {
      return jsonResponse({
        error: "Profil existiert bereits - editToken erforderlich, um es zu aktualisieren.",
        reason: "name_taken",
      }, 409);
    }

    const providedHash = await sha256Hex(providedToken);
    if (providedHash !== existing.editTokenHash) {
      return jsonResponse({
        error: "editToken stimmt nicht mit dem gespeicherten Profil überein.",
        reason: "token_mismatch",
      }, 409);
    }

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
    targetSpellIds,
    discordCard: existing?.discordCard, // nur syncPlayerDiscordCard schreibt dieses Feld
    createdAt,
    updatedAt: now,
  };

  await env.BLUNION_PROFILES.put(key, JSON.stringify(record), { expirationTtl: resolveTtlSeconds(body.ttlHours) });

  // Nach dem KV-Put, per waitUntil statt awaited: ein Discord-Ausfall darf die Response nie verzögern.
  ctx.waitUntil(syncPlayerDiscordCard(env, key, record, resolveTtlSeconds(body.ttlHours)));

  const responseBody: Record<string, unknown> = stripForResponse(env, record);
  if (plaintextEditTokenForResponse)
    responseBody.editToken = plaintextEditTokenForResponse;

  return jsonResponse(responseBody, existing ? 200 : 201);
}

/** Kernlogik von GET /profiles/browse, ohne das HTTP-Cache-Wrapping von handleBrowse - so kann
 * handleDiscordBrowse dieselbe Logik nutzen. Liefert alle "listed"-Profile auf dem Data Center.
 * Bewusst ohne Sekundär-Index: iteriert über alle "profile:"-Keys und filtert in-memory - für die
 * erwartete Nutzerzahl unkritisch, ein Index wäre verfrühte Optimierung. */
async function computePlayersBrowse(env: Env, dataCenter: string | null): Promise<Response> {
  const canonicalDataCenter = resolveBrowseDataCenter(dataCenter);
  if (canonicalDataCenter instanceof Response)
    return canonicalDataCenter;

  const normalizedDataCenter = canonicalDataCenter.toLowerCase();
  const results: ReturnType<typeof stripForBrowseResponse>[] = [];

  // list() liefert max. 1000 Keys/Aufruf (KV-Limit) - per cursor weiter paginieren.
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

/** Reihenfolge: Rate-Limit, dann dataCenter-Prüfung, erst danach Cache/KV - ein unbekanntes
 * dataCenter legt so nie einen Cache-Eintrag an. */
async function handleBrowse(env: Env, request: Request, ctx: ExecutionContext): Promise<Response> {
  const rateLimited = await enforceReadRateLimit(env, request);
  if (rateLimited)
    return rateLimited;

  const canonicalDataCenter = resolveBrowseDataCenter(new URL(request.url).searchParams.get("dataCenter"));
  if (canonicalDataCenter instanceof Response)
    return canonicalDataCenter;

  return withCache(request, canonicalDataCenter, ctx, () => computePlayersBrowse(env, canonicalDataCenter));
}

async function handleDelete(
  env: Env, request: Request, world: string, characterName: string, ctx: ExecutionContext,
): Promise<Response> {
  const rateLimited = await enforceWriteRateLimit(env, request);
  if (rateLimited)
    return rateLimited;

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

  await deleteProfileRecord(env, key, existing, ctx);
  return jsonResponse({ deleted: true });
}

/** Gemeinsame Löschlogik für DELETE /profile und DELETE /admin/profile (inkl. Discord-Karte).
 * Index-Eintrag über die GESPEICHERTE (kanonische) World/Name-Schreibweise, mit der
 * addPlayerCardIndexEntry ihn angelegt hat - nicht über die Schreibweise der Anfrage-URL. */
async function deleteProfileRecord(env: Env, key: string, existing: StoredProfile, ctx: ExecutionContext): Promise<void> {
  await env.BLUNION_PROFILES.delete(key);

  if (existing.discordCard)
    ctx.waitUntil(removePlayerDiscordCard(env, existing.world, existing.characterName, existing.discordCard));
}

/** Ersteller-Nachweis für PUT /group (bei JEDEM PUT, auch Updates - sonst könnten später Fremde
 * nachgetragen werden): a) Ersteller-Profil existiert und X-Creator-Token passt zu dessen
 * editTokenHash, b) Ersteller ist selbst Mitglied, c) jedes Mitglied hat ein Profil (Sichtbarkeit
 * egal). Ohne Creator-Angaben: bei REQUIRE_GROUP_CREATOR="true" Fehler, sonst null (Altclient,
 * keine Prüfung). Rückgabe: geprüfter Ersteller, null oder eine fertige Fehler-Response. */
async function verifyGroupCreator(
  env: Env, request: Request, body: PutGroupRequestBody, members: GroupMember[],
): Promise<GroupMember | null | Response> {
  const creatorToken = request.headers.get("X-Creator-Token");
  const hasAnyCreatorInput = body.creatorWorld !== undefined || body.creatorCharacterName !== undefined
    || creatorToken !== null;

  if (!hasAnyCreatorInput) {
    return env.REQUIRE_GROUP_CREATOR === "true"
      ? errorResponse(
        400, "creatorWorld, creatorCharacterName und Header \"X-Creator-Token\" sind erforderlich.")
      : null;
  }

  if (typeof body.creatorWorld !== "string" || typeof body.creatorCharacterName !== "string")
    return errorResponse(400, "creatorWorld und creatorCharacterName müssen als String angegeben werden.");

  if (!isValidCharacterName(body.creatorCharacterName))
    return errorResponse(400, `creatorCharacterName: ${INVALID_CHARACTER_NAME_MESSAGE}`);

  const creatorDcLookup = lookupDataCenter(body.creatorWorld);
  if (!creatorDcLookup)
    return errorResponse(400, `Unbekannte World "${body.creatorWorld}" in creatorWorld.`);

  if (!creatorToken)
    return errorResponse(403, 'Header "X-Creator-Token" fehlt.');

  const creator: GroupMember = { world: creatorDcLookup.canonicalWorld, characterName: body.creatorCharacterName };

  // a) - bewusst dieselbe Meldung für "kein Profil" und "falscher Token" (kein Profil-Orakel).
  const creatorProfile = await env.BLUNION_PROFILES.get<StoredProfile>(
    kvKey(creator.world, creator.characterName), "json");
  if (!creatorProfile || await sha256Hex(creatorToken) !== creatorProfile.editTokenHash)
    return errorResponse(403, "Ersteller-Nachweis fehlgeschlagen - eigenes Live-Sync-Profil fehlt oder X-Creator-Token passt nicht.");

  // b) - Vergleich wie kvKey() case-insensitiv.
  const creatorKey = kvKey(creator.world, creator.characterName);
  if (!members.some((member) => kvKey(member.world, member.characterName) === creatorKey))
    return errorResponse(403, "Der Ersteller muss selbst in members[] enthalten sein.");

  // c)
  const memberProfiles = await Promise.all(members.map((member) =>
    env.BLUNION_PROFILES.get(kvKey(member.world, member.characterName))));
  const missingMembers = members.filter((_, index) => memberProfiles[index] === null);
  if (missingMembers.length > 0) {
    return jsonResponse({
      error: "Nicht alle Mitglieder haben ein Live-Sync-Profil: "
        + missingMembers.map((member) => `${member.characterName} (${member.world})`).join(", "),
      missingMembers,
    }, 400);
  }

  return creator;
}

/** Ablauf 1:1 wie handlePut, nur mit members[]-Validierung statt spellBitmaskBase64 (siehe
 * StoredGroupProfile-Doc zum editToken-Besitzmodell). */
async function handleGroupPut(env: Env, request: Request, groupId: string, ctx: ExecutionContext): Promise<Response> {
  const rateLimited = await enforceWriteRateLimit(env, request);
  if (rateLimited)
    return rateLimited;

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

  // dataCenter wird vom world des ERSTEN Mitglieds hergeleitet, nicht vom Client übergeben.
  const members: GroupMember[] = [];
  let dataCenter: string | undefined;

  for (const rawMember of body.members) {
    if (!isValidRawGroupMember(rawMember))
      return errorResponse(400, "Jedes members[]-Element benötigt world und characterName als nicht-leeren String.");

    if (!isValidCharacterName(rawMember.characterName))
      return errorResponse(400, `members[]: ${INVALID_CHARACTER_NAME_MESSAGE}`);

    const memberDcLookup = lookupDataCenter(rawMember.world);
    if (!memberDcLookup)
      return errorResponse(400, `Unbekannte World "${rawMember.world}" in members[].`);

    members.push({ world: memberDcLookup.canonicalWorld, characterName: rawMember.characterName });

    if (dataCenter === undefined)
      dataCenter = memberDcLookup.dataCenter;
  }

  const verifiedCreator = await verifyGroupCreator(env, request, body, members);
  if (verifiedCreator instanceof Response)
    return verifiedCreator;

  const key = groupKvKey(groupId);
  const existing = await env.BLUNION_PROFILES.get<StoredGroupProfile>(key, "json");
  const now = new Date().toISOString();

  const phase2Fields = resolvePhase2Fields(body, existing);
  if (phase2Fields instanceof Response)
    return phase2Fields;

  const { visibility, availabilityTags, note, wantedPlayerCount, targetSpellIds } = phase2Fields;

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
    targetSpellIds,
    dataCenter: dataCenter!,
    // Ohne Creator-Angaben (Altclient bei REQUIRE_GROUP_CREATOR != "true") bleibt ein bereits
    // gespeicherter Ersteller erhalten, statt still zu verschwinden.
    creator: verifiedCreator ?? existing?.creator,
    // Unverändert aus "existing" übernommen (siehe DiscordCard-Doc) - discordCard wird
    // AUSSCHLIESSLICH von syncGroupDiscordCard weiter unten geschrieben, NIE hier direkt gesetzt.
    // Würde man es hier stattdessen weglassen (bzw. auf undefined setzen), ginge die Referenz auf
    // eine bereits bestehende Karte für das kurze Zeitfenster bis zum Abschluss des
    // Hintergrund-Syncs verloren - bei einem (theoretisch) fehlschlagenden Sync sogar dauerhaft,
    // mit einer verwaisten Karte in Discord und einer doppelt angelegten beim nächsten Update als
    // Folge.
    discordCard: existing?.discordCard,
    createdAt,
    updatedAt: now,
  };

  // Anders als Profile (die bei jedem gelernten Spell erneut gepusht werden) hat eine Gruppe
  // keinen automatischen Refresh - sie läuft nach der TTL ab, wenn niemand erneut PUT aufruft.
  // In einer Variable, weil syncGroupDiscordCard denselben Wert für ihren eigenen Put braucht.
  const ttlSeconds = resolveTtlSeconds(body.ttlHours);
  await env.BLUNION_PROFILES.put(key, JSON.stringify(record), { expirationTtl: ttlSeconds });

  ctx.waitUntil(syncGroupDiscordCard(env, key, record, ttlSeconds));

  const responseBody: Record<string, unknown> = stripForGroupResponse(env, record);
  if (plaintextEditTokenForResponse)
    responseBody.editToken = plaintextEditTokenForResponse;

  return jsonResponse(responseBody, existing ? 200 : 201);
}

/** Kernlogik von GET /groups/browse, ohne das HTTP-Cache-Wrapping von handleGroupsBrowse - so kann
 * handleDiscordBrowse dieselbe Logik nutzen. */
async function computeGroupsBrowse(env: Env, dataCenter: string | null): Promise<Response> {
  const canonicalDataCenter = resolveBrowseDataCenter(dataCenter);
  if (canonicalDataCenter instanceof Response)
    return canonicalDataCenter;

  const normalizedDataCenter = canonicalDataCenter.toLowerCase();
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

      // Fehlt das Profil eines Mitglieds, wird es trotzdem gelistet, nur mit spellBitmaskBase64: null.
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
        targetSpellIds: stored.targetSpellIds ?? [],
      });
    }

    cursor = listResult.list_complete ? undefined : listResult.cursor;
  } while (cursor);

  return jsonResponse(results);
}

/** Gleiche Reihenfolge wie handleBrowse. */
async function handleGroupsBrowse(env: Env, request: Request, ctx: ExecutionContext): Promise<Response> {
  const rateLimited = await enforceReadRateLimit(env, request);
  if (rateLimited)
    return rateLimited;

  const canonicalDataCenter = resolveBrowseDataCenter(new URL(request.url).searchParams.get("dataCenter"));
  if (canonicalDataCenter instanceof Response)
    return canonicalDataCenter;

  return withCache(request, canonicalDataCenter, ctx, () => computeGroupsBrowse(env, canonicalDataCenter));
}

/** Löscht NUR den "group:"-Eintrag - die referenzierten "profile:"-Einträge der Mitglieder bleiben
 * unangetastet bestehen (Referenz-statt-Kopie-Prinzip). */
async function handleGroupDelete(
  env: Env, request: Request, groupId: string, ctx: ExecutionContext,
): Promise<Response> {
  const rateLimited = await enforceWriteRateLimit(env, request);
  if (rateLimited)
    return rateLimited;

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

  await deleteGroupRecord(env, key, existing, ctx);
  return jsonResponse({ deleted: true });
}

/** Gemeinsame Löschlogik für DELETE /group und DELETE /admin/group (inkl. Discord-Karte). */
async function deleteGroupRecord(env: Env, key: string, existing: StoredGroupProfile, ctx: ExecutionContext): Promise<void> {
  await env.BLUNION_PROFILES.delete(key);

  if (existing.discordCard)
    ctx.waitUntil(removeGroupDiscordCard(env, existing.groupId, existing.discordCard));
}

/** Antwort eines unbekannten Pfads - Admin-Endpunkte antworten bei fehlendem Secret, falschem/
 * fehlendem Token oder Rate-Limit EXAKT so, damit ihre Existenz nicht erkennbar ist. */
function unknownEndpointResponse(): Response {
  return errorResponse(
    404,
    'Unbekannter Endpoint - erwartet wird "/profile/:world/:characterName", "/profiles/browse", ' +
      '"/group/:groupId", "/groups/browse" oder "/discord/interactions".',
  );
}

/** Admin-Antworten bewusst OHNE CORS-Header - die Endpunkte sind nur per Skript nutzbar (siehe
 * tools/admin), ein Browser könnte die Antwort ohnehin nicht lesen. */
function adminJsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8" },
  });
}

async function sha256Bytes(value: string): Promise<ArrayBuffer> {
  return crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
}

/** "Authorization: Bearer <ADMIN_TOKEN>", zeitkonstant verglichen: beide Seiten werden zuerst
 * gehasht (gleiche Länge, timingSafeEqual verlangt das), dann per crypto.subtle.timingSafeEqual. */
async function isAdminAuthorized(env: Env, request: Request): Promise<boolean> {
  if (!env.ADMIN_TOKEN)
    return false;

  const header = request.headers.get("Authorization");
  if (!header || !header.startsWith("Bearer "))
    return false;

  const [provided, expected] = await Promise.all([
    sha256Bytes(header.slice("Bearer ".length)),
    sha256Bytes(env.ADMIN_TOKEN),
  ]);
  return crypto.subtle.timingSafeEqual(provided, expected);
}

/** Moderation: GET/DELETE /admin/profile/:world/:characterName, DELETE /admin/group/:groupId.
 * Rate-Limit über WRITE_RATE_LIMITER VOR der Token-Prüfung (bremst Raten); auch ein Limit-Treffer
 * antwortet mit dem generischen 404. Profilnamen werden bewusst NICHT per isValidCharacterName
 * geprüft - so lassen sich auch Altlasten mit ungültigen Namen entfernen. */
async function handleAdminRequest(env: Env, request: Request, url: URL, ctx: ExecutionContext): Promise<Response> {
  if (!env.ADMIN_TOKEN)
    return unknownEndpointResponse();

  if (await enforceWriteRateLimit(env, request))
    return unknownEndpointResponse();

  if (!await isAdminAuthorized(env, request))
    return unknownEndpointResponse();

  const profileMatch = url.pathname.match(ADMIN_PROFILE_PATH);
  if (profileMatch) {
    const world = decodeURIComponent(profileMatch[1]!);
    const characterName = decodeURIComponent(profileMatch[2]!);
    const key = kvKey(world, characterName);
    const existing = await env.BLUNION_PROFILES.get<StoredProfile>(key, "json");

    if (request.method === "GET") {
      if (!existing)
        return adminJsonResponse({ error: "Kein Profil unter diesem Key gefunden.", key }, 404);

      // Alles außer editTokenHash (Allowlist per Destructuring-Rest).
      const { editTokenHash: _editTokenHash, ...visible } = existing;
      return adminJsonResponse({ key, ...visible });
    }

    if (request.method === "DELETE") {
      if (!existing)
        return adminJsonResponse({ error: "Kein Profil unter diesem Key gefunden.", key }, 404);

      await deleteProfileRecord(env, key, existing, ctx);
      return adminJsonResponse({ deleted: true, key });
    }

    return adminJsonResponse({ error: `Methode "${request.method}" nicht unterstützt.` }, 405);
  }

  const groupMatch = url.pathname.match(ADMIN_GROUP_PATH);
  if (groupMatch) {
    // Keine UUID-Prüfung - auch Altlasten mit anderen groupIds sollen löschbar sein.
    const groupId = decodeURIComponent(groupMatch[1]!);
    const key = groupKvKey(groupId);

    if (request.method !== "DELETE")
      return adminJsonResponse({ error: `Methode "${request.method}" nicht unterstützt.` }, 405);

    const existing = await env.BLUNION_PROFILES.get<StoredGroupProfile>(key, "json");
    if (!existing)
      return adminJsonResponse({ error: "Keine Gruppe unter diesem Key gefunden.", key }, 404);

    await deleteGroupRecord(env, key, existing, ctx);
    return adminJsonResponse({ deleted: true, key });
  }

  return adminJsonResponse({ error: "Unbekannter Admin-Endpoint." }, 404);
}

/** Muss vor jeder weiteren Verarbeitung in handleDiscordInteractions laufen, sonst könnte ein
 * Angreifer beliebige Interactions einschleusen. Braucht den ROHEN, noch nicht geparsten Body -
 * die signierte Nachricht ist exakt "timestamp + rawBody", ein re-serialisierter JSON-Body würde
 * die Signatur nicht mehr treffen. Gibt bei jedem Fehler einheitlich false zurück (nie Wurf). */
async function verifyDiscordSignature(env: Env, request: Request, rawBody: string): Promise<boolean> {
  const signatureHeader = request.headers.get("X-Signature-Ed25519");
  const timestampHeader = request.headers.get("X-Signature-Timestamp");
  if (!signatureHeader || !timestampHeader || !env.DISCORD_PUBLIC_KEY)
    return false;

  try {
    const publicKey = await crypto.subtle.importKey(
      "raw",
      hexToBytes(env.DISCORD_PUBLIC_KEY),
      { name: "Ed25519" },
      false,
      ["verify"],
    );

    const message = new TextEncoder().encode(timestampHeader + rawBody);
    return await crypto.subtle.verify({ name: "Ed25519" }, publicKey, hexToBytes(signatureHeader), message);
  } catch {
    return false; // ungültiges Hex zählt als ungültige Signatur, nicht als 500
  }
}

/** Discord-Embed-Limits (harte API-Grenzen - ein überlanges Feld lässt Discord das GANZE Embed mit
 * 400 ablehnen, was bei den Karten nur still geloggt würde, siehe discordWebhook.ts). */
const DISCORD_EMBED_TITLE_MAX = 256;
const DISCORD_EMBED_FIELD_NAME_MAX = 256;
const DISCORD_EMBED_FIELD_VALUE_MAX = 1024;
const DISCORD_EMBED_DESCRIPTION_MAX = 4096;

const DISCORD_URL_PATTERN = /(?:https?:\/\/|www\.|discord(?:app)?\.com\/invite|discord\.gg\/)\S*/gi;
const DISCORD_MARKDOWN_PATTERN = /[\\*_~`|>[\]()#]/g;
const DISCORD_CONTROL_CHARACTER_PATTERN = /[\u0000-\u001f\u007f-\u009f\u2028\u2029]/g;

/** Für JEDEN nutzerkontrollierten Wert (note, characterName, world, ...), bevor er in ein Embed
 * wandert: Steuerzeichen/Zeilenumbrüche -> Leerzeichen (kein Layout-Spoofing über mehrere Zeilen),
 * URLs/Einladungen entfernen (kein Link-/Phishing-Spam über die offiziellen Kanal-Karten),
 * Markdown escapen (kein [Text](Link), keine Überschriften/Spoiler/Codeblöcke) und @everyone/@here
 * per Zero-Width-Space entschärfen. Die JSON-API-Antworten bleiben unverändert - Plugin und
 * Web-Companion escapen selbst. */
export function sanitizeForDiscord(text: string): string {
  return text
    .replace(DISCORD_CONTROL_CHARACTER_PATTERN, " ")
    .replace(DISCORD_URL_PATTERN, "")
    .replace(DISCORD_MARKDOWN_PATTERN, "\\$&")
    .replace(/@(everyone|here)/gi, "@\u200b$1")
    .trim();
}

/** Kürzt auf maxLength (inkl. "…"), ohne ein UTF-16-Surrogatpaar zu zerschneiden. */
export function truncateForDiscord(text: string, maxLength: number): string {
  if (text.length <= maxLength)
    return text;

  let cut = maxLength - 1;
  const lastKept = text.charCodeAt(cut - 1);
  if (lastKept >= 0xd800 && lastKept <= 0xdbff)
    cut -= 1;

  return `${text.slice(0, cut)}…`;
}

/** Formatiert targetSpellIds als sprachunabhängige order-Nummern (z.B. "#1, #3, #7"). Unbekannte
 * IDs werden übersprungen statt das Embed scheitern zu lassen; undefined, wenn nichts übrig bleibt
 * (Aufrufer lässt die Zeile dann weg). */
export function formatTargetSpellOrders(targetSpellIds: number[]): string | undefined {
  const orders = targetSpellIds
    .map((spellId) => SPELL_ORDER_BY_ID.get(spellId))
    .filter((order): order is number => order !== undefined)
    .sort((a, b) => a - b);

  return orders.length > 0 ? orders.map((order) => `#${order}`).join(", ") : undefined;
}

/** Baut EIN "field" für das Discord-Browse-Embed aus einer Gruppe - "note" übernimmt die Rolle des
 * (nicht existierenden) Gruppennamens als Feldtitel. */
function buildGroupEmbedField(group: DiscordBrowseGroup): { name: string; value: string; inline: boolean } {
  const memberList = group.members
    .map((member) => `${sanitizeForDiscord(member.world)} ${sanitizeForDiscord(member.characterName)}`)
    .join("\n");

  // 0 bedeutet "egal wie viele" - dann nur die Mitgliederzahl zeigen statt eines "3/0".
  const memberCountLabel = group.wantedPlayerCount > 0
    ? `${group.members.length}/${group.wantedPlayerCount}`
    : `${group.members.length}`;

  let value = `Members (${memberCountLabel}):\n${memberList}`;

  // note/wantedPlayerCount stecken hier schon anderswo (Feldname bzw. memberCountLabel), daher
  // "" und 0 statt der echten Werte.
  const extraLines = formatAvailabilityAndNoteLines("", group.availabilityTags, 0);
  if (extraLines.length > 0)
    value += `\n\n${extraLines}`;

  const targetSpellLabel = formatTargetSpellOrders(group.targetSpellIds);
  if (targetSpellLabel !== undefined)
    value += `\n\nTarget spells: ${targetSpellLabel}`;

  // Eine Notiz, die nur aus einer URL bestand, ist nach dem Sanitizing leer - dann Platzhalter.
  const safeNote = sanitizeForDiscord(group.note);

  return {
    name: truncateForDiscord(safeNote.length > 0 ? safeNote : "(no note)", DISCORD_EMBED_FIELD_NAME_MAX),
    value: truncateForDiscord(value, DISCORD_EMBED_FIELD_VALUE_MAX),
    inline: false,
  };
}

/** Wire-Wert -> lesbares englisches Label für die Discord-Embeds ("evening" -> "Evening").
 * Unbekannte Werte (nur aus Altdaten möglich, PUT validiert per Allowlist) werden sanitisiert
 * durchgereicht. */
const AVAILABILITY_TAG_LABELS: Readonly<Record<string, string>> = {
  morning: "Morning",
  afternoon: "Afternoon",
  evening: "Evening",
  weekend: "Weekend",
  flexible: "Flexible",
};

function formatAvailabilityTag(tag: string): string {
  return AVAILABILITY_TAG_LABELS[tag] ?? sanitizeForDiscord(tag);
}

/** Gemeinsam genutzt von buildGroupEmbedField und buildPlayerCardEmbed, damit sich das Format
 * nicht auseinanderentwickelt. Jede der drei Zeilen ist einzeln optional (leer/0 lässt sie weg);
 * buildGroupEmbedField übergibt note="" und wantedPlayerCount=0, da beide dort schon anderswo im
 * Embed stehen. */
export function formatAvailabilityAndNoteLines(
  note: string, availabilityTags: string[], wantedPlayerCount: number,
): string {
  const lines: string[] = [];

  const safeNote = sanitizeForDiscord(note);
  if (safeNote.length > 0)
    lines.push(safeNote);

  if (availabilityTags.length > 0)
    lines.push(`Availability: ${availabilityTags.map(formatAvailabilityTag).join(", ")}`);

  if (wantedPlayerCount > 0) // 0 = "egal wie viele", dann keine Zeile
    lines.push(`Looking for: ${wantedPlayerCount} ${wantedPlayerCount === 1 ? "player" : "players"}`);

  return lines.join("\n\n");
}

/** Wiederverwendet buildGroupEmbedField statt einer zweiten Notiz-/Mitglieder-/Verfügbarkeits-
 * Formatierung. Zeigt (anders als buildGroupsBrowseEmbed) genau eine Gruppe, daher das konkrete
 * Data Center im Titel statt außerhalb für alle Treffer gemeinsam. */
function buildGroupCardEmbed(record: StoredGroupProfile): Record<string, unknown> {
  const field = buildGroupEmbedField({
    groupId: record.groupId,
    members: record.members,
    availabilityTags: record.availabilityTags ?? [],
    note: record.note ?? "",
    wantedPlayerCount: record.wantedPlayerCount ?? 0,
    targetSpellIds: record.targetSpellIds ?? [],
  });

  return {
    title: truncateForDiscord(`Blue Mage group on ${sanitizeForDiscord(record.dataCenter)}`, DISCORD_EMBED_TITLE_MAX),
    color: 0x2b6cb0,
    fields: [field],
  };
}

/** Spieler-Pendant zu buildGroupCardEmbed - keine Mitgliederliste, daher description statt
 * fields[]. Grün statt Blau, damit Spieler-/Gruppen-Karten im selben Kanal unterscheidbar sind. */
export function buildPlayerCardEmbed(stored: StoredProfile): Record<string, unknown> {
  let lines = formatAvailabilityAndNoteLines(
    stored.note ?? "",
    stored.availabilityTags ?? [],
    stored.wantedPlayerCount ?? 0,
  );

  const targetSpellLabel = formatTargetSpellOrders(stored.targetSpellIds ?? []);
  if (targetSpellLabel !== undefined)
    lines += `${lines.length > 0 ? "\n\n" : ""}Target spells: ${targetSpellLabel}`;

  return {
    title: truncateForDiscord(
      `${sanitizeForDiscord(stored.characterName)} (${sanitizeForDiscord(stored.world)}) is looking for players`,
      DISCORD_EMBED_TITLE_MAX,
    ),
    color: 0x2f9e44,
    description: lines.length > 0 ? truncateForDiscord(lines, DISCORD_EMBED_DESCRIPTION_MAX) : undefined,
  };
}

/** Postet das Embed über den Webhook und pflegt bei Erfolg den Index-Eintrag mit; scheitert
 * createWebhookMessage (liefert null), bleibt der Index unangetastet und es gibt weiterhin keine
 * Karte. */
async function createDiscordCard(
  region: Region, webhookUrl: string, embed: Record<string, unknown>, addIndexEntry: (messageId: string) => Promise<void>,
): Promise<DiscordCard | undefined> {
  const messageId = await createWebhookMessage(webhookUrl, embed);
  if (!messageId)
    return undefined;

  await addIndexEntry(messageId);
  return { region, channelWebhookId: extractWebhookId(webhookUrl) ?? "", messageId };
}

/** Löscht die Nachricht + Index-Eintrag; wirft nie (siehe deleteWebhookMessage/removeCardIndexEntry). */
async function removeDiscordCard(env: Env, card: DiscordCard, removeIndexEntry: (region: Region) => Promise<void>): Promise<void> {
  const webhookUrl = getRegionWebhookUrl(env, card.region);
  if (webhookUrl)
    await deleteWebhookMessage(webhookUrl, card.messageId);

  await removeIndexEntry(card.region);
}

async function createGroupDiscordCard(
  env: Env, region: Region, webhookUrl: string, record: StoredGroupProfile,
): Promise<DiscordCard | undefined> {
  return createDiscordCard(region, webhookUrl, buildGroupCardEmbed(record),
    (messageId) => addDiscordCardIndexEntry(env, region, record.groupId, messageId));
}

async function removeGroupDiscordCard(env: Env, groupId: string, card: DiscordCard): Promise<void> {
  return removeDiscordCard(env, card, (region) => removeDiscordCardIndexEntry(env, region, groupId));
}

function discordCardsEqual(a: DiscordCard | undefined, b: DiscordCard | undefined): boolean {
  if (a === b)
    return true;
  if (!a || !b)
    return false;

  return a.region === b.region && a.channelWebhookId === b.channelWebhookId && a.messageId === b.messageId;
}

async function createPlayerDiscordCard(
  env: Env, region: Region, webhookUrl: string, stored: StoredProfile,
): Promise<DiscordCard | undefined> {
  return createDiscordCard(region, webhookUrl, buildPlayerCardEmbed(stored),
    (messageId) => addPlayerCardIndexEntry(env, region, stored.world, stored.characterName, messageId));
}

async function removePlayerDiscordCard(
  env: Env, world: string, characterName: string, card: DiscordCard,
): Promise<void> {
  return removeDiscordCard(env, card, (region) => removePlayerCardIndexEntry(env, region, world, characterName));
}

/** Befüllt von syncGroupDiscordCard/syncPlayerDiscordCard mit ihren jeweiligen Werten/Closures,
 * damit syncDiscordCard nicht wissen muss, ob es um eine Gruppe oder ein Spieler-Profil geht. */
interface DiscordCardSyncTarget {
  existingCard: DiscordCard | undefined;
  dataCenter: string;
  visibility: "listed" | "unlisted";
  buildEmbed: () => Record<string, unknown>;
  createCard: (region: Region, webhookUrl: string) => Promise<DiscordCard | undefined>;
  removeCard: (card: DiscordCard) => Promise<void>;
}

/** Legt/aktualisiert/entfernt die persistente Discord-Kanal-Karte je nach visibility/Region -
 * schreibt selbst nichts in KV, liefert nur die neue discordCard zurück. removeCard/createCard
 * nutzen jeweils die Region der ALTEN Karte zum Löschen, da eine Nachricht nur über ihren eigenen
 * Webhook editierbar/löschbar ist (daher bei Regionswechsel löschen+neu anlegen statt editieren). */
async function syncDiscordCard(env: Env, target: DiscordCardSyncTarget): Promise<DiscordCard | undefined> {
  const region = regionForDataCenter(target.dataCenter);
  const webhookUrl = region ? getRegionWebhookUrl(env, region) : undefined;

  if (target.visibility !== "listed") {
    if (target.existingCard)
      await target.removeCard(target.existingCard);

    return undefined;
  }

  if (!region || !webhookUrl)
    return target.existingCard;

  if (!target.existingCard)
    return target.createCard(region, webhookUrl);

  if (target.existingCard.region !== region) {
    await target.removeCard(target.existingCard);
    return target.createCard(region, webhookUrl);
  }

  await editWebhookMessage(webhookUrl, target.existingCard.messageId, target.buildEmbed());
  return target.existingCard; // region/channelWebhookId/messageId unverändert.
}

/** Aus handleGroupPut per ctx.waitUntil angestoßen, läuft komplett nach der bereits abgeschickten
 * Antwort - ein Discord-Fehler darf den PUT nie beeinflussen. Schreibt nur bei tatsächlicher
 * Änderung erneut nach KV. */
async function syncGroupDiscordCard(
  env: Env, key: string, record: StoredGroupProfile, ttlSeconds: number,
): Promise<void> {
  const nextCard = await syncDiscordCard(env, {
    existingCard: record.discordCard,
    dataCenter: record.dataCenter,
    visibility: record.visibility,
    buildEmbed: () => buildGroupCardEmbed(record),
    createCard: (region, webhookUrl) => createGroupDiscordCard(env, region, webhookUrl, record),
    removeCard: (card) => removeGroupDiscordCard(env, record.groupId, card),
  });

  if (!discordCardsEqual(record.discordCard, nextCard)) {
    const updated: StoredGroupProfile = { ...record, discordCard: nextCard };
    await env.BLUNION_PROFILES.put(key, JSON.stringify(updated), { expirationTtl: ttlSeconds });
  }
}

/** Spieler-Pendant zu syncGroupDiscordCard, aus handlePut angestoßen. Exportiert für einen
 * direkten Unit-Test des Regionswechsel-Zweigs, der über die öffentliche Route praktisch nicht
 * herstellbar ist (dataCenter hängt hier am world, das zugleich der KV-Key ist). */
export async function syncPlayerDiscordCard(
  env: Env, key: string, stored: StoredProfile, ttlSeconds: number,
): Promise<void> {
  const nextCard = await syncDiscordCard(env, {
    existingCard: stored.discordCard,
    dataCenter: stored.dataCenter,
    visibility: stored.visibility,
    buildEmbed: () => buildPlayerCardEmbed(stored),
    createCard: (region, webhookUrl) => createPlayerDiscordCard(env, region, webhookUrl, stored),
    removeCard: (card) => removePlayerDiscordCard(env, stored.world, stored.characterName, card),
  });

  if (!discordCardsEqual(stored.discordCard, nextCard)) {
    const updated: StoredProfile = { ...stored, discordCard: nextCard };
    await env.BLUNION_PROFILES.put(key, JSON.stringify(updated), { expirationTtl: ttlSeconds });
  }
}

/** Discord-Embeds erlauben höchstens 25 "fields" (harte API-Grenze, sonst wird das ganze Embed
 * abgelehnt) - mehr Treffer werden selbst gekappt statt sich darauf zu verlassen. */
const DISCORD_EMBED_MAX_FIELDS = 25;

/** Discord zählt title + description + alle field name/value + footer.text zusammen und lehnt ein
 * Embed über 6000 Zeichen komplett ab. Der Puffer deckt description/footer (werden erst nach der
 * Feldauswahl gebaut) und Zählunterschiede (Discord zählt Zeichen, hier UTF-16-Einheiten, die nie
 * weniger sind) ab. */
const DISCORD_EMBED_TOTAL_MAX = 6000;
const DISCORD_EMBED_TOTAL_BUFFER = 300;

interface DiscordEmbedField {
  name: string;
  value: string;
  inline: boolean;
}

/** Gemeinsam für beide Browse-Embeds: nimmt Felder in Reihenfolge auf, bis 25 Felder oder das
 * Gesamtlimit (mit Puffer) erreicht sind; der Rest wird per Fußzeile "+N weitere" angezeigt. */
function buildBrowseEmbed(
  title: string, fields: DiscordEmbedField[], describeShown: (shown: number, total: number) => string,
): Record<string, unknown> {
  const safeTitle = truncateForDiscord(title, DISCORD_EMBED_TITLE_MAX);
  let used = safeTitle.length;
  const shownFields: DiscordEmbedField[] = [];

  for (const field of fields) {
    const fieldLength = field.name.length + field.value.length;
    if (shownFields.length >= DISCORD_EMBED_MAX_FIELDS
        || used + fieldLength > DISCORD_EMBED_TOTAL_MAX - DISCORD_EMBED_TOTAL_BUFFER)
      break;

    shownFields.push(field);
    used += fieldLength;
  }

  const hiddenCount = fields.length - shownFields.length;

  return {
    title: safeTitle,
    description: hiddenCount > 0 ? describeShown(shownFields.length, fields.length) : undefined,
    color: 0x2b6cb0,
    fields: shownFields,
    footer: hiddenCount > 0 ? { text: `+${hiddenCount} more - see website` } : undefined,
  };
}

/** Exportiert für den Gesamtlimit-Test. */
export function buildGroupsBrowseEmbed(dataCenter: string, groups: DiscordBrowseGroup[]): Record<string, unknown> {
  return buildBrowseEmbed(
    `Blue Mage groups on ${sanitizeForDiscord(dataCenter)}`,
    groups.map(buildGroupEmbedField),
    (shown, total) => `Showing ${shown} of ${total} listed groups.`,
  );
}

/** Pendant zu buildGroupEmbedField, aber ohne Mitgliederliste - Feldname ist World+Charaktername
 * (statt note, das braucht ein Profil nicht als Ersatz-Kennung). Discord erlaubt keinen leeren
 * "value", daher der Platzhalter. */
function buildPlayerEmbedField(
  player: ReturnType<typeof stripForBrowseResponse>,
): { name: string; value: string; inline: boolean } {
  let lines = formatAvailabilityAndNoteLines(player.note, player.availabilityTags, player.wantedPlayerCount);

  const targetSpellLabel = formatTargetSpellOrders(player.targetSpellIds);
  if (targetSpellLabel !== undefined)
    lines += `${lines.length > 0 ? "\n\n" : ""}Target spells: ${targetSpellLabel}`;

  return {
    name: truncateForDiscord(
      `${sanitizeForDiscord(player.characterName)} (${sanitizeForDiscord(player.world)})`, DISCORD_EMBED_FIELD_NAME_MAX),
    value: truncateForDiscord(lines.length > 0 ? lines : "(no further details)", DISCORD_EMBED_FIELD_VALUE_MAX),
    inline: false,
  };
}

/** Pendant zu buildGroupsBrowseEmbed für "/blunion browse type:players". */
export function buildPlayersBrowseEmbed(
  dataCenter: string, players: ReturnType<typeof stripForBrowseResponse>[],
): Record<string, unknown> {
  return buildBrowseEmbed(
    `Blue Mages looking for groups on ${sanitizeForDiscord(dataCenter)}`,
    players.map(buildPlayerEmbedField),
    (shown, total) => `Showing ${shown} of ${total} listed players.`,
  );
}

/** Link-Button zur Web-Companion-Seite - style 5 ("Link") braucht keine custom_id. */
const DISCORD_WEBSITE_LINK_COMPONENTS = [
  {
    type: 1, // Action Row
    components: [
      {
        type: 2, // Button
        style: 5, // Link
        label: "Open companion website",
        url: COMPANION_WEBSITE_URL,
      },
    ],
  },
];

/** Reine Text-Antwort für Fehlermeldungen/leere Ergebnisse, wo ein leeres Embed nichts bringt. */
function discordMessageResponse(content: string): Response {
  return jsonResponse({
    type: DISCORD_RESPONSE_TYPE_CHANNEL_MESSAGE_WITH_SOURCE,
    data: { content, allowed_mentions: { parse: [] } },
  });
}

/** "/blunion browse [datacenter] [type]" - reicht computeGroupsBrowse/computePlayersBrowse durch
 * und formatiert das Ergebnis als Discord-Embed. Fehlendes dataCenter gibt bewusst eine normale
 * Chat-Antwort statt HTTP 400 - eine rohe 4xx-Response zeigt Discord nur als "App hat nicht
 * geantwortet" an. */
async function handleDiscordBrowse(
  env: Env, dataCenter: string | undefined, type: "groups" | "players",
): Promise<Response> {
  if (!dataCenter) {
    return discordMessageResponse(
      'Please specify a data center, e.g. "/blunion browse datacenter:Aether".',
    );
  }

  // Ab hier nur noch die kanonische Schreibweise - die rohe Slash-Command-Eingabe landet so weder
  // im Embed-Titel noch in einer Chat-Antwort.
  const canonicalDataCenter = lookupCanonicalDataCenter(dataCenter);
  if (!canonicalDataCenter)
    return discordMessageResponse('Unknown data center - e.g. "/blunion browse datacenter:Aether".');

  dataCenter = canonicalDataCenter;

  if (type === "players") {
    const browseResponse = await computePlayersBrowse(env, dataCenter);
    if (!browseResponse.ok) {
      return discordMessageResponse("Something went wrong while loading the players.");
    }

    const players = await browseResponse.json<ReturnType<typeof stripForBrowseResponse>[]>();
    if (players.length === 0)
      return discordMessageResponse(`No publicly listed players found on "${dataCenter}".`);

    return jsonResponse({
      type: DISCORD_RESPONSE_TYPE_CHANNEL_MESSAGE_WITH_SOURCE,
      data: {
        embeds: [buildPlayersBrowseEmbed(dataCenter, players)],
        components: DISCORD_WEBSITE_LINK_COMPONENTS,
      },
    });
  }

  const browseResponse = await computeGroupsBrowse(env, dataCenter);
  if (!browseResponse.ok) {
    return discordMessageResponse("Something went wrong while loading the groups.");
  }

  const groups = await browseResponse.json<DiscordBrowseGroup[]>();
  if (groups.length === 0)
    return discordMessageResponse(`No publicly listed groups found on "${dataCenter}".`);

  return jsonResponse({
    type: DISCORD_RESPONSE_TYPE_CHANNEL_MESSAGE_WITH_SOURCE,
    data: {
      embeds: [buildGroupsBrowseEmbed(dataCenter, groups)],
      components: DISCORD_WEBSITE_LINK_COMPONENTS,
    },
  });
}

function findDiscordSubcommand(
  data: DiscordInteractionCommandData | undefined,
  name: string,
): DiscordInteractionOption | undefined {
  return data?.options?.find((option) => option.type === DISCORD_OPTION_TYPE_SUB_COMMAND && option.name === name);
}

function findDiscordStringOption(options: DiscordInteractionOption[] | undefined, name: string): string | undefined {
  const option = options?.find((candidate) => candidate.name === name);
  return typeof option?.value === "string" ? option.value : undefined;
}

/** Phase 1 kennt nur "/blunion browse" - unbekannte Commands enden in einer Chat-Antwort statt
 * einem Fehlerstatus (kann bei geänderter Command-Registrierung mit noch gecachtem alten Command
 * auftreten). */
async function handleDiscordApplicationCommand(env: Env, interaction: DiscordInteraction): Promise<Response> {
  if (interaction.data?.name !== "blunion")
    return discordMessageResponse(`Unknown command "${sanitizeForDiscord(interaction.data?.name ?? "?")}".`);

  const browseSubcommand = findDiscordSubcommand(interaction.data, "browse");
  if (!browseSubcommand)
    return discordMessageResponse('Unknown subcommand - currently only "browse" is supported.');

  const dataCenter = findDiscordStringOption(browseSubcommand.options, "datacenter");

  // "type" ist optional (siehe register-discord-commands.mjs, required: false) und war zum
  // Zeitpunkt ihrer Einführung bei bereits registrierten Guild-Commands naturgemäß noch gar nicht
  // bekannt - fehlt sie ODER trägt sie (sollte bei einer über die Registrierung eingereichten
  // Interaction nie passieren, siehe findDiscordStringOption-Doc) einen unbekannten Wert, gilt
  // deshalb "groups" als Default, exakt das bisherige (einzige) Verhalten vor dieser Option -
  // bestehende Server, die den Command noch nicht neu registriert haben, sehen also weiterhin
  // Gruppen, keinen Fehler.
  const rawType = findDiscordStringOption(browseSubcommand.options, "type");
  const type: "groups" | "players" = rawType === "players" ? "players" : "groups";

  return handleDiscordBrowse(env, dataCenter, type);
}

/** Reihenfolge nicht vertauschbar: erst rawBody lesen (einmal, per request.text()), dann Signatur
 * verifizieren (jeder Fehlschlag -> sofort 401), ERST DANACH als JSON parsen/dispatchen. */
async function handleDiscordInteractions(env: Env, request: Request): Promise<Response> {
  const rawBody = await request.text();

  const isValidSignature = await verifyDiscordSignature(env, request, rawBody);
  if (!isValidSignature)
    return new Response("Ungültige Anfrage-Signatur.", { status: 401 });

  let interaction: DiscordInteraction;
  try {
    interaction = JSON.parse(rawBody);
  } catch {
    return errorResponse(400, "Ungültiger JSON-Body.");
  }

  switch (interaction.type) {
    case DISCORD_INTERACTION_TYPE_PING:
      return jsonResponse({ type: DISCORD_RESPONSE_TYPE_PONG }); // von Discord beim Setup gefordert

    case DISCORD_INTERACTION_TYPE_APPLICATION_COMMAND:
      return handleDiscordApplicationCommand(env, interaction);

    default:
      return errorResponse(400, `Unbekannter Interaction-Typ ${interaction.type}.`);
  }
}

/** Cron-Cleanup: prüft pro Region+Index-Eintrag, ob der zugehörige group:/profile:-Eintrag noch
 * existiert (siehe DiscordCardIndexEntry-Doc) - falls nicht (stiller TTL-Ablauf ohne DELETE), wird
 * die Karte über dieselbe remove*DiscordCard-Funktion entfernt wie beim expliziten DELETE-Pfad.
 * channelWebhookId ist hier "" (der Index speichert ihn nicht, wird beim Löschen nicht gebraucht). */
async function cleanupOrphanedDiscordCards(env: Env): Promise<void> {
  for (const region of ALL_REGIONS) {
    const groupEntries = await env.BLUNION_PROFILES.get<DiscordCardIndexEntry[]>(discordCardsIndexKey(region), "json");
    if (groupEntries && groupEntries.length > 0) {
      for (const entry of groupEntries) {
        const stillExists = await env.BLUNION_PROFILES.get(groupKvKey(entry.groupId));
        if (stillExists === null) {
          await removeGroupDiscordCard(env, entry.groupId, { region, channelWebhookId: "", messageId: entry.messageId });
        }
      }
    }

    const playerEntries = await env.BLUNION_PROFILES.get<PlayerCardIndexEntry[]>(playerCardsIndexKey(region), "json");
    if (playerEntries && playerEntries.length > 0) {
      for (const entry of playerEntries) {
        const stillExists = await env.BLUNION_PROFILES.get(kvKey(entry.world, entry.characterName));
        if (stillExists === null) {
          await removePlayerDiscordCard(
            env, entry.world, entry.characterName, { region, channelWebhookId: "", messageId: entry.messageId },
          );
        }
      }
    }
  }
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    if (request.method === "OPTIONS")
      return new Response(null, { status: 204, headers: CORS_HEADERS });

    const url = new URL(request.url);

    if (url.pathname.startsWith(ADMIN_PATH_PREFIX))
      return handleAdminRequest(env, request, url, ctx);

    if (BROWSE_PATH.test(url.pathname)) {
      if (request.method !== "GET")
        return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);

      return handleBrowse(env, request, ctx);
    }

    if (GROUPS_BROWSE_PATH.test(url.pathname)) {
      if (request.method !== "GET")
        return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);

      return handleGroupsBrowse(env, request, ctx);
    }

    if (DISCORD_INTERACTIONS_PATH.test(url.pathname)) {
      if (request.method !== "POST")
        return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);

      return handleDiscordInteractions(env, request);
    }

    const profileMatch = url.pathname.match(PROFILE_PATH);

    if (profileMatch) {
      // Charakternamen können Leer-/Sonderzeichen enthalten, siehe encodeURIComponent im Client.
      const world = decodeURIComponent(profileMatch[1]!);
      const characterName = decodeURIComponent(profileMatch[2]!);

      if (world.length === 0 || characterName.length === 0)
        return errorResponse(400, "world und characterName dürfen nicht leer sein.");

      // Vor dem Methoden-Switch: gilt für GET/PUT/DELETE gleichermaßen (und vor jedem KV-Zugriff).
      if (!isValidCharacterName(characterName))
        return errorResponse(400, INVALID_CHARACTER_NAME_MESSAGE);

      switch (request.method) {
        case "GET":
          return handleGet(env, request, world, characterName);
        case "PUT":
          return handlePut(env, request, world, characterName, ctx);
        case "DELETE":
          return handleDelete(env, request, world, characterName, ctx);
        default:
          return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);
      }
    }

    const groupMatch = url.pathname.match(GROUP_PATH);

    if (groupMatch) {
      const groupId = decodeURIComponent(groupMatch[1]!);

      if (groupId.length === 0)
        return errorResponse(400, "groupId darf nicht leer sein.");

      if (!isValidGroupId(groupId))
        return errorResponse(400, "groupId muss eine UUID sein.");

      switch (request.method) {
        case "PUT":
          return handleGroupPut(env, request, groupId, ctx);
        case "DELETE":
          return handleGroupDelete(env, request, groupId, ctx);
        default:
          return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);
      }
    }

    return unknownEndpointResponse();
  },

  /** Cron Trigger (siehe wrangler.toml) - räumt Karten von still per TTL abgelaufenen Einträgen auf. */
  async scheduled(_controller: ScheduledController, env: Env, ctx: ExecutionContext): Promise<void> {
    ctx.waitUntil(cleanupOrphanedDiscordCards(env));
  },
};
