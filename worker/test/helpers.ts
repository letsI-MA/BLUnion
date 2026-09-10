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

/** "_cacheBust" ist ein für den Handler bedeutungsloser Zusatzparameter (siehe handleBrowse/
 * handleGroupsBrowse - nur "dataCenter" wird ausgelesen), aber Teil der Request-URL und damit Teil
 * des withCache-Cache-Keys (siehe src/index.ts, caches.default ist PER URL, nicht per Test
 * isoliert - anders als KV, das laut @cloudflare/vitest-plugin-Doku pro Test zurückgesetzt wird).
 * OHNE das würden mehrere Tests, die denselben dataCenter browsen, den 20-Sekunden-Cache-Eintrag
 * eines VORHERIGEN Tests treffen und dessen (zu dem Zeitpunkt noch unvollständige) Ergebnisliste
 * sehen - kein Worker-Bug, sondern genau das beabsichtigte Server-Cache-Verhalten (siehe
 * BROWSE_CACHE_TTL_SECONDS-Doc), das hier bewusst umgangen wird, weil Integrationstests frische
 * Ergebnisse pro Aufruf brauchen. */
function cacheBustParam(): string {
  return `_cacheBust=${Date.now()}_${Math.random().toString(36).slice(2)}`;
}

export async function browseProfiles(dataCenter: string | null): Promise<Response> {
  const url = dataCenter
    ? `http://example.com/profiles/browse?dataCenter=${encodeURIComponent(dataCenter)}&${cacheBustParam()}`
    : `http://example.com/profiles/browse?${cacheBustParam()}`;
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

export async function browseGroups(dataCenter: string | null): Promise<Response> {
  const url = dataCenter
    ? `http://example.com/groups/browse?dataCenter=${encodeURIComponent(dataCenter)}&${cacheBustParam()}`
    : `http://example.com/groups/browse?${cacheBustParam()}`;
  return callWorker(new Request(url, { method: "GET" }));
}

/** Eindeutiger Charaktername pro Test - vermeidet Kollisionen zwischen Tests über denselben
 * (persistenten, siehe cloudflare:test "reset()"-Doku - hier bewusst NICHT genutzt, um dem
 * bestehenden KV-Persistenzverhalten möglichst nahezukommen) KV-Namespace hinweg. */
let uniqueCounter = 0;
export function uniqueName(prefix: string): string {
  uniqueCounter += 1;
  return `${prefix}${uniqueCounter}_${Date.now()}`;
}
