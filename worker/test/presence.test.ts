import { env } from "cloudflare:workers";
import { createExecutionContext, createScheduledController, waitOnExecutionContext } from "cloudflare:test";
import { afterEach, describe, expect, it } from "vitest";
import worker, {
  buildPlayersBrowseEmbed, cleanupPresence, isPresenceOnline, PRESENCE_ONLINE_WINDOW_MS, PRESENCE_RETENTION_MS,
  roundPresenceLastSeen,
} from "../src/index";
import {
  browseProfiles, callWorker, deleteProfile, KNOWN_WORLD, KNOWN_WORLD_DATA_CENTER, putProfile, uniqueName,
  validBitmaskBase64,
} from "./helpers";

/**
 * Online-Status (POST /presence, D1-Binding PRESENCE, siehe handlePresencePost/computePlayersBrowse/
 * cleanupPresence in src/index.ts). Die D1-Migrationen wendet test/applyMigrations.ts an.
 */

const mutableEnv = env as unknown as Record<string, string | undefined>;
const ADMIN_TOKEN = "test-admin-token-with-enough-entropy";

afterEach(() => {
  mutableEnv.PRESENCE_ENABLED = "true";
  mutableEnv.ADMIN_TOKEN = undefined;
});

interface BrowseEntry {
  characterName: string;
  online?: boolean;
  lastSeen?: string | null;
}

async function createProfile(sharePresence: boolean | undefined, visibility = "listed"): Promise<{ name: string; token: string }> {
  const name = uniqueName("Presence");
  const body: Record<string, unknown> = { spellBitmaskBase64: validBitmaskBase64(), visibility };
  if (sharePresence !== undefined) body.sharePresence = sharePresence;

  const response = await putProfile(KNOWN_WORLD, name, body);
  expect(response.status).toBe(201);
  return { name, token: (await response.json<{ editToken: string }>()).editToken };
}

function postPresence(name: string, state: unknown, token?: string): Promise<Response> {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (token !== undefined) headers["X-Edit-Token"] = token;

  return callWorker(new Request(
    `http://example.com/presence/${encodeURIComponent(KNOWN_WORLD)}/${encodeURIComponent(name)}`,
    { method: "POST", headers, body: JSON.stringify({ state }) },
  ));
}

async function presenceRow(name: string): Promise<{ online: number; last_seen: number } | null> {
  return env.PRESENCE
    .prepare("SELECT online, last_seen FROM presence WHERE world = ? AND name_lower = ?")
    .bind(KNOWN_WORLD.toLowerCase(), name.toLowerCase())
    .first<{ online: number; last_seen: number }>();
}

async function setLastSeen(name: string, lastSeenMs: number): Promise<void> {
  await env.PRESENCE
    .prepare("UPDATE presence SET last_seen = ? WHERE world = ? AND name_lower = ?")
    .bind(lastSeenMs, KNOWN_WORLD.toLowerCase(), name.toLowerCase())
    .run();
}

async function browseEntry(name: string): Promise<BrowseEntry | undefined> {
  const response = await browseProfiles(KNOWN_WORLD_DATA_CENTER);
  expect(response.status).toBe(200);
  return (await response.json<BrowseEntry[]>()).find((entry) => entry.characterName === name);
}

describe("POST /presence", () => {
  it("accepts online/offline for a profile with sharePresence and stores the row", async () => {
    const { name, token } = await createProfile(true);

    const response = await postPresence(name, "online", token);
    expect(response.status).toBe(200);
    expect((await presenceRow(name))?.online).toBe(1);

    expect((await postPresence(name, "offline", token)).status).toBe(200);
    expect((await presenceRow(name))?.online).toBe(0);
  });

  it("rejects profiles without consent (default and explicit false) with 403 and stores nothing", async () => {
    for (const sharePresence of [undefined, false]) {
      const { name, token } = await createProfile(sharePresence);

      expect((await postPresence(name, "online", token)).status).toBe(403);
      expect(await presenceRow(name)).toBeNull();
    }
  });

  it("rejects a wrong or missing token with 403", async () => {
    const { name } = await createProfile(true);

    expect((await postPresence(name, "online", "wrong-token")).status).toBe(403);
    expect((await postPresence(name, "online")).status).toBe(403);
    expect(await presenceRow(name)).toBeNull();
  });

  it("returns 404 without profile and 400 for an invalid state", async () => {
    expect((await postPresence(uniqueName("Nobody"), "online", "token")).status).toBe(404);

    const { name, token } = await createProfile(true);
    expect((await postPresence(name, "away", token)).status).toBe(400);
  });

  it("only accepts POST", async () => {
    const response = await callWorker(new Request(`http://example.com/presence/${KNOWN_WORLD}/Some%20Body`, { method: "GET" }));
    expect(response.status).toBe(405);
  });
});

describe("presence in GET /profiles/browse", () => {
  it("adds online/lastSeen for consenting profiles, nothing for others", async () => {
    const consenting = await createProfile(true);
    await postPresence(consenting.name, "online", consenting.token);
    const other = await createProfile(false);

    const consentingEntry = await browseEntry(consenting.name);
    expect(consentingEntry?.online).toBe(true);
    expect(typeof consentingEntry?.lastSeen).toBe("string");

    const otherEntry = await browseEntry(other.name);
    expect(otherEntry).toBeDefined();
    expect(otherEntry).not.toHaveProperty("online");
    expect(otherEntry).not.toHaveProperty("lastSeen");
  });

  it("reports online: false and lastSeen: null for a consenting profile without any signal yet", async () => {
    const { name } = await createProfile(true);

    const entry = await browseEntry(name);
    expect(entry?.online).toBe(false);
    expect(entry?.lastSeen).toBeNull();
  });

  it("treats online as stale after 75 minutes", async () => {
    const { name, token } = await createProfile(true);
    await postPresence(name, "online", token);

    await setLastSeen(name, Date.now() - PRESENCE_ONLINE_WINDOW_MS + 60_000);
    expect((await browseEntry(name))?.online).toBe(true);

    await setLastSeen(name, Date.now() - PRESENCE_ONLINE_WINDOW_MS - 60_000);
    expect((await browseEntry(name))?.online).toBe(false);
  });

  it("an offline signal is never online, but keeps lastSeen", async () => {
    const { name, token } = await createProfile(true);
    await postPresence(name, "offline", token);

    const entry = await browseEntry(name);
    expect(entry?.online).toBe(false);
    expect(entry?.lastSeen).not.toBeNull();
  });

  it("outputs lastSeen rounded down to 15 minutes", async () => {
    const { name, token } = await createProfile(true);
    await postPresence(name, "online", token);
    const lastSeenMs = Date.UTC(2026, 8, 30, 12, 44, 59, 999);
    await setLastSeen(name, lastSeenMs);

    expect((await browseEntry(name))?.lastSeen).toBe("2026-09-30T12:30:00.000Z");
  });

  it("does not show unlisted consenting profiles at all", async () => {
    const { name, token } = await createProfile(true, "unlisted");
    await postPresence(name, "online", token);

    expect(await browseEntry(name)).toBeUndefined();
  });
});

describe("presence helpers", () => {
  it("isPresenceOnline: 75-minute boundary and offline state", () => {
    const now = 10_000_000_000;
    expect(isPresenceOnline(1, now - PRESENCE_ONLINE_WINDOW_MS + 1, now)).toBe(true);
    expect(isPresenceOnline(1, now - PRESENCE_ONLINE_WINDOW_MS, now)).toBe(false);
    expect(isPresenceOnline(0, now, now)).toBe(false);
  });

  it("roundPresenceLastSeen rounds down to full quarter hours", () => {
    expect(roundPresenceLastSeen(Date.UTC(2026, 0, 1, 8, 0, 0))).toBe("2026-01-01T08:00:00.000Z");
    expect(roundPresenceLastSeen(Date.UTC(2026, 0, 1, 8, 14, 59))).toBe("2026-01-01T08:00:00.000Z");
    expect(roundPresenceLastSeen(Date.UTC(2026, 0, 1, 8, 15, 0))).toBe("2026-01-01T08:15:00.000Z");
  });
});

describe("presence lifecycle", () => {
  it("PUT with sharePresence: false deletes the row, and further signals are rejected", async () => {
    const { name, token } = await createProfile(true);
    await postPresence(name, "online", token);
    expect(await presenceRow(name)).not.toBeNull();

    const response = await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64(), editToken: token, sharePresence: false });
    expect(response.status).toBe(200);
    expect((await response.json<{ sharePresence: boolean }>()).sharePresence).toBe(false);

    expect(await presenceRow(name)).toBeNull();
    expect((await postPresence(name, "online", token)).status).toBe(403);
  });

  it("a PUT without sharePresence keeps the previous consent", async () => {
    const { name, token } = await createProfile(true);

    const response = await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64(), editToken: token });
    expect((await response.json<{ sharePresence: boolean }>()).sharePresence).toBe(true);
  });

  it("rejects a non-boolean sharePresence with 400", async () => {
    const response = await putProfile(KNOWN_WORLD, uniqueName("Bad"), { spellBitmaskBase64: validBitmaskBase64(), sharePresence: "yes" });
    expect(response.status).toBe(400);
  });

  it("deleting the profile deletes the row", async () => {
    const { name, token } = await createProfile(true);
    await postPresence(name, "online", token);

    expect((await deleteProfile(KNOWN_WORLD, name, token)).status).toBe(200);
    expect(await presenceRow(name)).toBeNull();
  });

  it("admin delete of the profile deletes the row", async () => {
    const { name, token } = await createProfile(true);
    await postPresence(name, "online", token);
    mutableEnv.ADMIN_TOKEN = ADMIN_TOKEN;

    const response = await callWorker(new Request(
      `http://example.com/admin/profile/${encodeURIComponent(KNOWN_WORLD)}/${encodeURIComponent(name)}`,
      { method: "DELETE", headers: { Authorization: `Bearer ${ADMIN_TOKEN}` } },
    ));

    expect(response.status).toBe(200);
    expect(await presenceRow(name)).toBeNull();
  });

  it("the cron removes rows older than 30 days and rows without profile, keeps the rest", async () => {
    const fresh = await createProfile(true);
    await postPresence(fresh.name, "online", fresh.token);

    const old = await createProfile(true);
    await postPresence(old.name, "online", old.token);
    await setLastSeen(old.name, Date.now() - PRESENCE_RETENTION_MS - 60_000);

    const orphan = await createProfile(true);
    await postPresence(orphan.name, "online", orphan.token);
    await env.BLUNION_PROFILES.delete(`profile:${KNOWN_WORLD.toLowerCase()}:${orphan.name.toLowerCase()}`);

    const ctx = createExecutionContext();
    await worker.scheduled(createScheduledController(), env, ctx);
    await waitOnExecutionContext(ctx);

    expect(await presenceRow(fresh.name)).not.toBeNull();
    expect(await presenceRow(old.name)).toBeNull();
    expect(await presenceRow(orphan.name)).toBeNull();
  });

  it("cleanupPresence also removes rows whose profile no longer consents", async () => {
    const { name, token } = await createProfile(true);
    await postPresence(name, "online", token);
    // Zustimmung direkt in KV entfernen (wie ein Altbestand ohne Löschung der Zeile).
    const key = `profile:${KNOWN_WORLD.toLowerCase()}:${name.toLowerCase()}`;
    const stored = await env.BLUNION_PROFILES.get<Record<string, unknown>>(key, "json");
    await env.BLUNION_PROFILES.put(key, JSON.stringify({ ...stored, sharePresence: false }));

    await cleanupPresence(env);

    expect(await presenceRow(name)).toBeNull();
  });
});

describe('PRESENCE_ENABLED = "false"', () => {
  it("rejects POST /presence with 403 and omits presence fields in the browse", async () => {
    const { name, token } = await createProfile(true);
    await postPresence(name, "online", token);

    mutableEnv.PRESENCE_ENABLED = "false";

    const response = await postPresence(name, "online", token);
    expect(response.status).toBe(403);
    expect(await response.json()).toEqual({ error: "Presence is currently disabled" });

    const entry = await browseEntry(name);
    expect(entry).toBeDefined();
    expect(entry).not.toHaveProperty("online");
    expect(entry).not.toHaveProperty("lastSeen");
  });
});

describe('Discord "/blunion browse" players', () => {
  it("marks online players with a green dot, nobody else", () => {
    const base = {
      world: KNOWN_WORLD, spellBitmaskBase64: validBitmaskBase64(), availabilityTags: [], note: "",
      wantedPlayerCount: 0, targetSpellIds: [], updatedAt: new Date().toISOString(),
    };
    const embed = buildPlayersBrowseEmbed(KNOWN_WORLD_DATA_CENTER, [
      { ...base, characterName: "Online Player", online: true, lastSeen: "2026-09-30T12:00:00.000Z" },
      { ...base, characterName: "Offline Player", online: false, lastSeen: null },
      { ...base, characterName: "Quiet Player" },
    ]) as { fields: { name: string }[] };

    expect(embed.fields.map((field) => field.name)).toEqual([
      `🟢 Online Player (${KNOWN_WORLD})`,
      `Offline Player (${KNOWN_WORLD})`,
      `Quiet Player (${KNOWN_WORLD})`,
    ]);
  });
});
