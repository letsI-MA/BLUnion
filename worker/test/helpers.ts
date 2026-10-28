import { env } from "cloudflare:workers";
import { createExecutionContext, waitOnExecutionContext } from "cloudflare:test";
import worker from "../src/index";
import { base64UrlEncode } from "../src/crypto";

/**
 * Ruft worker.fetch(...) exakt so auf, wie es die offizielle Cloudflare-Vitest-Doku für
 * Direktaufrufe (statt über den deprecateten "SELF"-Fetcher) empfiehlt - inkl.
 * waitOnExecutionContext, damit auch ctx.waitUntil()-Promises (siehe withCache in src/index.ts)
 * VOR der Assertion abgeschlossen sind, nicht nur der direkt zurückgegebene Response.
 */
export async function callWorker(request: Request): Promise<Response> {
  const ctx = createExecutionContext();
  const response = await worker.fetch(request, env, ctx);
  await waitOnExecutionContext(ctx);
  return response;
}

/** 16 Null-Bytes, URL-safe Base64 - eine strukturell GÜLTIGE (richtige Länge), aber inhaltlich
 * leere Spell-Bitmaske (siehe BITMASK_BYTES in src/index.ts). Für Tests, denen der tatsächliche
 * Bitmask-Inhalt egal ist. */
export function validBitmaskBase64(): string {
  return base64UrlEncode(new Uint8Array(16));
}

/** Eine bekannte, in worlds.ts hinterlegte World (Aether-DC) - für Tests, die eine GÜLTIGE World
 * brauchen, ohne sich um die konkrete DC-Zuordnung zu kümmern. */
export const KNOWN_WORLD = "Gilgamesh";
export const KNOWN_WORLD_DATA_CENTER = "Aether";

/** Zwei tatsächlich in KNOWN_SPELL_IDS (src/spellIds.ts) enthaltene IDs - für targetSpellIds-Tests. */
export const KNOWN_SPELL_IDS_SAMPLE = [11383, 11384];

export function profileUrl(world: string, characterName: string): string {
  return `http://example.com/profile/${encodeURIComponent(world)}/${encodeURIComponent(characterName)}`;
}

export function groupUrl(groupId: string): string {
  return `http://example.com/group/${encodeURIComponent(groupId)}`;
}

export async function putProfile(
  world: string,
  characterName: string,
  body: Record<string, unknown>,
): Promise<Response> {
  return callWorker(
    new Request(profileUrl(world, characterName), {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    }),
  );
}

export async function getProfile(world: string, characterName: string): Promise<Response> {
  return callWorker(new Request(profileUrl(world, characterName), { method: "GET" }));
}

export async function deleteProfile(
  world: string,
  characterName: string,
  editToken?: string,
): Promise<Response> {
  const headers: Record<string, string> = {};
  if (editToken !== undefined) headers["X-Edit-Token"] = editToken;

  return callWorker(new Request(profileUrl(world, characterName), { method: "DELETE", headers }));
}

/** Eindeutiger Hostname pro Browse-Aufruf. withCache (src/index.ts) bildet den Cache-Key bewusst
 * nur aus origin + pathname + kanonischem dataCenter und ignoriert alle anderen Query-Parameter
 * (Schutz gegen Cache-Busting) - ein Zusatzparameter reicht deshalb NICHT mehr, um in Tests den
 * 20-Sekunden-Cache-Eintrag eines vorherigen Tests zu umgehen (caches.default ist nicht pro Test
 * isoliert, anders als KV). Ein anderer Host ist eine andere origin und damit ein anderer Key. */
export function uniqueBrowseOrigin(prefix = "browse"): string {
  return `http://${prefix}-${Date.now()}-${Math.random().toString(36).slice(2)}.example.com`;
}

/** origin optional - nur Tests, die den Cache bewusst teilen wollen (z.B. Plugin-Browse und
 * Discord-Browse auf denselben Eintrag), geben eine eigene mit. */
export async function browseProfiles(dataCenter: string | null, origin = uniqueBrowseOrigin()): Promise<Response> {
  const url = dataCenter
    ? `${origin}/profiles/browse?dataCenter=${encodeURIComponent(dataCenter)}`
    : `${origin}/profiles/browse`;
  return callWorker(new Request(url, { method: "GET" }));
}

export async function putGroup(groupId: string, body: Record<string, unknown>): Promise<Response> {
  return callWorker(
    new Request(groupUrl(groupId), {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    }),
  );
}

export async function deleteGroup(groupId: string, editToken?: string): Promise<Response> {
  const headers: Record<string, string> = {};
  if (editToken !== undefined) headers["X-Edit-Token"] = editToken;

  return callWorker(new Request(groupUrl(groupId), { method: "DELETE", headers }));
}

export async function browseGroups(dataCenter: string | null, origin = uniqueBrowseOrigin()): Promise<Response> {
  const url = dataCenter
    ? `${origin}/groups/browse?dataCenter=${encodeURIComponent(dataCenter)}`
    : `${origin}/groups/browse`;
  return callWorker(new Request(url, { method: "GET" }));
}

/** Eindeutiger, nach FFXIV-Regeln GÜLTIGER Charaktername pro Test (siehe isValidCharacterName in
 * src/index.ts: "Vorname Nachname", je 2-15 Buchstaben, max. 21 Zeichen gesamt) - vermeidet
 * Kollisionen zwischen Tests über denselben (persistenten, siehe cloudflare:test "reset()"-Doku -
 * hier bewusst NICHT genutzt, um dem bestehenden KV-Persistenzverhalten möglichst nahezukommen)
 * KV-Namespace hinweg. Der Vorname kommt aus dem (auf Buchstaben reduzierten, auf 12 Zeichen
 * gekürzten) Präfix, der Nachname aus Zähler + Zufall, beides nur Buchstaben. */
let uniqueCounter = 0;
export function uniqueName(prefix: string): string {
  uniqueCounter += 1;

  const letters = prefix.replace(/[^A-Za-z]/g, "");
  const firstName = (letters.charAt(0).toUpperCase() + letters.slice(1) + "xx").slice(0, Math.max(2, Math.min(12, letters.length)));
  const lastName = "Q" + toLetters(uniqueCounter, 3) + toLetters(Math.floor(Math.random() * 26 ** 4), 4);

  return `${firstName} ${lastName}`;
}

function toLetters(value: number, length: number): string {
  let result = "";
  for (let i = 0; i < length; i++) {
    result = String.fromCharCode(97 + (value % 26)) + result;
    value = Math.floor(value / 26);
  }
  return result;
}

/** Gültige groupId (siehe isValidGroupId in src/index.ts - Plugin/Web-Companion erzeugen UUIDs). */
export function uniqueGroupId(): string {
  return crypto.randomUUID();
}
