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
import { lookupDataCenter } from "./worlds";
import { KNOWN_SPELL_IDS } from "./spellIds";
import { SPELL_ORDER_BY_ID } from "./spellOrder";

export interface Env {
  BLUNION_PROFILES: KVNamespace;
  /** Rate-Limiting-Binding für die vier schreibenden Endpunkte (siehe enforceWriteRateLimit). */
  WRITE_RATE_LIMITER: RateLimit;
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

const CORS_HEADERS: Record<string, string> = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, PUT, DELETE, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, X-Edit-Token",
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
 * und cache.put()/die zurückgegebene Response würden sich einen bereits konsumierten Stream teilen. */
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

/** IP-basiertes Rate-Limiting für die schreibenden Endpunkte über das native Workers-Binding
 * WRITE_RATE_LIMITER (siehe wrangler.toml) statt Dashboard-"Rate Limiting Rules" - die sind ein
 * Zonen-/WAF-Feature und setzen eine eigene Zone voraus, dieser Worker läuft aber zonenlos unter
 * *.workers.dev. Fehlt der CF-Connecting-IP-Header (z.B. lokal ohne echten Edge davor), wird NICHT
 * limitiert statt mit 500 zu antworten. Rückgabe: fertige 429-Response oder null (weitermachen). */
async function enforceWriteRateLimit(env: Env, request: Request): Promise<Response | null> {
  const ip = request.headers.get("CF-Connecting-IP");
  if (!ip)
    return null;

  const { success } = await env.WRITE_RATE_LIMITER.limit({ key: ip });
  if (!success)
    return errorResponse(429, "Zu viele Anfragen von dieser IP - bitte kurz warten und erneut versuchen.");

  return null;
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

async function handleGet(env: Env, world: string, characterName: string): Promise<Response> {
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
  if (!dataCenter)
    return errorResponse(400, 'Query-Parameter "dataCenter" fehlt.');

  const normalizedDataCenter = dataCenter.toLowerCase();
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

async function handleBrowse(env: Env, request: Request, ctx: ExecutionContext): Promise<Response> {
  return withCache(request, ctx, () => {
    const url = new URL(request.url);
    return computePlayersBrowse(env, url.searchParams.get("dataCenter"));
  });
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

  await env.BLUNION_PROFILES.delete(key);

  if (existing.discordCard)
    ctx.waitUntil(removePlayerDiscordCard(env, world, characterName, existing.discordCard));

  return jsonResponse({ deleted: true });
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

async function handleGroupsBrowse(env: Env, request: Request, ctx: ExecutionContext): Promise<Response> {
  return withCache(request, ctx, () => {
    const url = new URL(request.url);
    return computeGroupsBrowse(env, url.searchParams.get("dataCenter"));
  });
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

  await env.BLUNION_PROFILES.delete(key);

  if (existing.discordCard)
    ctx.waitUntil(removeGroupDiscordCard(env, groupId, existing.discordCard));

  return jsonResponse({ deleted: true });
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
    .map((member) => `${member.world} ${member.characterName}`)
    .join("\n");

  // 0 bedeutet "egal wie viele" - dann nur die Mitgliederzahl zeigen statt eines "3/0".
  const memberCountLabel = group.wantedPlayerCount > 0
    ? `${group.members.length}/${group.wantedPlayerCount}`
    : `${group.members.length}`;

  let value = `Mitglieder (${memberCountLabel}):\n${memberList}`;

  // note/wantedPlayerCount stecken hier schon anderswo (Feldname bzw. memberCountLabel), daher
  // "" und 0 statt der echten Werte.
  const extraLines = formatAvailabilityAndNoteLines("", group.availabilityTags, 0);
  if (extraLines.length > 0)
    value += `\n\n${extraLines}`;

  const targetSpellLabel = formatTargetSpellOrders(group.targetSpellIds);
  if (targetSpellLabel !== undefined)
    value += `\n\nZiel-Spells: ${targetSpellLabel}`;

  return {
    name: group.note.length > 0 ? group.note : "(ohne Notiz)",
    value,
    inline: false,
  };
}

/** Gemeinsam genutzt von buildGroupEmbedField und buildPlayerCardEmbed, damit sich das Format
 * nicht auseinanderentwickelt. Jede der drei Zeilen ist einzeln optional (leer/0 lässt sie weg);
 * buildGroupEmbedField übergibt note="" und wantedPlayerCount=0, da beide dort schon anderswo im
 * Embed stehen. */
export function formatAvailabilityAndNoteLines(
  note: string, availabilityTags: string[], wantedPlayerCount: number,
): string {
  const lines: string[] = [];

  if (note.length > 0)
    lines.push(note);

  if (availabilityTags.length > 0)
    lines.push(`Verfügbarkeit: ${availabilityTags.join(", ")}`);

  if (wantedPlayerCount > 0) // 0 = "egal wie viele", dann keine Zeile
    lines.push(`Gesucht: ${wantedPlayerCount} Mitspieler`);

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
    title: `Blue Mage Gruppe auf ${record.dataCenter}`,
    color: 0x2b6cb0,
    fields: [field],
  };
}

/** Spieler-Pendant zu buildGroupCardEmbed - keine Mitgliederliste, daher description statt
 * fields[]. Grün statt Blau, damit Spieler-/Gruppen-Karten im selben Kanal unterscheidbar sind. */
export function buildPlayerCardEmbed(stored: StoredProfile): Record<string, unknown> {
  const lines = formatAvailabilityAndNoteLines(
    stored.note ?? "",
    stored.availabilityTags ?? [],
    stored.wantedPlayerCount ?? 0,
  );

  return {
    title: `${stored.characterName} (${stored.world}) sucht Mitspieler`,
    color: 0x2f9e44,
    description: lines.length > 0 ? lines : undefined,
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

function buildGroupsBrowseEmbed(dataCenter: string, groups: DiscordBrowseGroup[]): Record<string, unknown> {
  const shownGroups = groups.slice(0, DISCORD_EMBED_MAX_FIELDS);

  return {
    title: `Blue Mage Gruppen auf ${dataCenter}`,
    description: groups.length > shownGroups.length
      ? `Zeige ${shownGroups.length} von ${groups.length} gelisteten Gruppen.`
      : undefined,
    color: 0x2b6cb0,
    fields: shownGroups.map(buildGroupEmbedField),
  };
}

/** Pendant zu buildGroupEmbedField, aber ohne Mitgliederliste - Feldname ist World+Charaktername
 * (statt note, das braucht ein Profil nicht als Ersatz-Kennung). Discord erlaubt keinen leeren
 * "value", daher der Platzhalter. */
function buildPlayerEmbedField(
  player: ReturnType<typeof stripForBrowseResponse>,
): { name: string; value: string; inline: boolean } {
  const lines = formatAvailabilityAndNoteLines(player.note, player.availabilityTags, player.wantedPlayerCount);

  return {
    name: `${player.characterName} (${player.world})`,
    value: lines.length > 0 ? lines : "(keine weiteren Angaben)",
    inline: false,
  };
}

/** Pendant zu buildGroupsBrowseEmbed für "/blunion browse type:players". */
function buildPlayersBrowseEmbed(
  dataCenter: string, players: ReturnType<typeof stripForBrowseResponse>[],
): Record<string, unknown> {
  const shownPlayers = players.slice(0, DISCORD_EMBED_MAX_FIELDS);

  return {
    title: `Blue Mage Spieler-Gesuche auf ${dataCenter}`,
    description: players.length > shownPlayers.length
      ? `Zeige ${shownPlayers.length} von ${players.length} gelisteten Spielern.`
      : undefined,
    color: 0x2b6cb0,
    fields: shownPlayers.map(buildPlayerEmbedField),
  };
}

/** Link-Button zur Web-Companion-Seite - style 5 ("Link") braucht keine custom_id. */
const DISCORD_WEBSITE_LINK_COMPONENTS = [
  {
    type: 1, // Action Row
    components: [
      {
        type: 2, // Button
        style: 5, // Link
        label: "Companion-Website öffnen",
        url: COMPANION_WEBSITE_URL,
      },
    ],
  },
];

/** Reine Text-Antwort für Fehlermeldungen/leere Ergebnisse, wo ein leeres Embed nichts bringt. */
function discordMessageResponse(content: string): Response {
  return jsonResponse({
    type: DISCORD_RESPONSE_TYPE_CHANNEL_MESSAGE_WITH_SOURCE,
    data: { content },
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
      'Bitte ein Data Center angeben, z.B. "/blunion browse datacenter:Aether".',
    );
  }

  if (type === "players") {
    const browseResponse = await computePlayersBrowse(env, dataCenter);
    if (!browseResponse.ok) {
      const errorBody = await browseResponse.json().catch(() => null) as { error?: string } | null;
      return discordMessageResponse(errorBody?.error ?? "Beim Abrufen der Spieler ist ein Fehler aufgetreten.");
    }

    const players = await browseResponse.json<ReturnType<typeof stripForBrowseResponse>[]>();
    if (players.length === 0)
      return discordMessageResponse(`Keine öffentlich gelisteten Spieler auf "${dataCenter}" gefunden.`);

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
    const errorBody = await browseResponse.json().catch(() => null) as { error?: string } | null;
    return discordMessageResponse(errorBody?.error ?? "Beim Abrufen der Gruppen ist ein Fehler aufgetreten.");
  }

  const groups = await browseResponse.json<DiscordBrowseGroup[]>();
  if (groups.length === 0)
    return discordMessageResponse(`Keine öffentlich gelisteten Gruppen auf "${dataCenter}" gefunden.`);

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
    return discordMessageResponse(`Unbekannter Command "${interaction.data?.name ?? "?"}".`);

  const browseSubcommand = findDiscordSubcommand(interaction.data, "browse");
  if (!browseSubcommand)
    return discordMessageResponse('Unbekannter Sub-Command - aktuell wird nur "browse" unterstützt.');

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

      switch (request.method) {
        case "GET":
          return handleGet(env, world, characterName);
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

      switch (request.method) {
        case "PUT":
          return handleGroupPut(env, request, groupId, ctx);
        case "DELETE":
          return handleGroupDelete(env, request, groupId, ctx);
        default:
          return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);
      }
    }

    return errorResponse(
      404,
      'Unbekannter Endpoint - erwartet wird "/profile/:world/:characterName", "/profiles/browse", ' +
        '"/group/:groupId", "/groups/browse" oder "/discord/interactions".',
    );
  },

  /** Cron Trigger (siehe wrangler.toml) - räumt Karten von still per TTL abgelaufenen Einträgen auf. */
  async scheduled(_controller: ScheduledController, env: Env, ctx: ExecutionContext): Promise<void> {
    ctx.waitUntil(cleanupOrphanedDiscordCards(env));
  },
};
