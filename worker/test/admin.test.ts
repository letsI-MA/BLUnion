import { env } from "cloudflare:workers";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { playerCardsIndexKey } from "../src/index";
import { setDiscordFetchForTests } from "../src/discordWebhook";
import {
  callWorker, getProfile, KNOWN_WORLD, putGroup, putProfile, uniqueGroupId, uniqueName, validBitmaskBase64,
} from "./helpers";

/**
 * Moderation (/admin/*, siehe handleAdminRequest in src/index.ts), 409-reason bei PUT /profile und
 * die englischen Discord-Texte.
 */

const ADMIN_TOKEN = "test-admin-token-with-enough-entropy";
const FAKE_WEBHOOK_URL_NA = "https://discord.com/api/webhooks/100000000000000001/test-token-na";

const mutableEnv = env as unknown as Record<string, string | undefined>;
let originalAdminToken: string | undefined;

beforeEach(() => {
  originalAdminToken = mutableEnv.ADMIN_TOKEN;
  mutableEnv.ADMIN_TOKEN = ADMIN_TOKEN;
});

afterEach(() => {
  mutableEnv.ADMIN_TOKEN = originalAdminToken;
  mutableEnv.DISCORD_WEBHOOK_NA = undefined;
  setDiscordFetchForTests(fetch);
});

function adminRequest(method: string, path: string, token: string | null = ADMIN_TOKEN, extraHeaders: Record<string, string> = {}) {
  const headers: Record<string, string> = { ...extraHeaders };
  if (token !== null) headers.Authorization = `Bearer ${token}`;
  return callWorker(new Request(`http://example.com${path}`, { method, headers }));
}

function adminProfilePath(world: string, characterName: string): string {
  return `/admin/profile/${encodeURIComponent(world)}/${encodeURIComponent(characterName)}`;
}

async function unknownRouteBody(): Promise<unknown> {
  return (await callWorker(new Request("http://example.com/nope", { method: "GET" }))).json();
}

function recordDiscordCalls(): { method: string; url: string }[] {
  const calls: { method: string; url: string }[] = [];
  setDiscordFetchForTests((async (input: RequestInfo | URL, init?: RequestInit) => {
    const method = init?.method ?? "GET";
    calls.push({ method, url: input instanceof Request ? input.url : input.toString() });
    if (method === "POST")
      return new Response(JSON.stringify({ id: `fake-message-${calls.length}` }), { status: 200 });
    return method === "DELETE" ? new Response(null, { status: 204 }) : new Response("{}", { status: 200 });
  }) as typeof fetch);
  return calls;
}

describe("admin endpoints - hidden unless authorized", () => {
  it("answers exactly like an unknown route when ADMIN_TOKEN is not configured", async () => {
    mutableEnv.ADMIN_TOKEN = undefined;

    const response = await adminRequest("GET", adminProfilePath(KNOWN_WORLD, "Any Body"));

    expect(response.status).toBe(404);
    expect(await response.json()).toEqual(await unknownRouteBody());
  });

  it.each([
    ["a wrong token", "wrong-token", {}],
    ["no Authorization header", null, {}],
    ["a non-Bearer scheme", null, { Authorization: `Basic ${ADMIN_TOKEN}` }],
  ] as const)("answers like an unknown route for %s", async (_label, token, extraHeaders) => {
    const name = uniqueName("Victim");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });

    const response = await adminRequest("DELETE", adminProfilePath(KNOWN_WORLD, name), token, extraHeaders);

    expect(response.status).toBe(404);
    expect(await response.json()).toEqual(await unknownRouteBody());
    expect((await getProfile(KNOWN_WORLD, name)).status).toBe(200); // nichts gelöscht
  });

  it("does not add Authorization or admin routes to CORS", async () => {
    const response = await callWorker(new Request(`http://example.com${adminProfilePath(KNOWN_WORLD, "Any Body")}`, {
      method: "OPTIONS",
    }));

    expect(response.headers.get("Access-Control-Allow-Headers")).not.toMatch(/Authorization/i);
    expect(response.headers.get("Access-Control-Allow-Methods")).toBe("GET, PUT, DELETE, OPTIONS");
  });

  it("sends no CORS headers on authorized admin responses", async () => {
    const response = await adminRequest("GET", adminProfilePath(KNOWN_WORLD, uniqueName("Nobody")));

    expect(response.status).toBe(404);
    expect(response.headers.get("Access-Control-Allow-Origin")).toBeNull();
  });

  it("rate-limits per IP and then answers like an unknown route (no 429)", async () => {
    const ip = "192.0.2." + Math.floor(Math.random() * 255);
    const statuses: number[] = [];
    // WRITE_RATE_LIMITER: 20/60 s - 2 * limit + 1, siehe security.test.ts (feste Zeitfenster).
    for (let i = 0; i < 41; i++) {
      const response = await adminRequest("GET", adminProfilePath(KNOWN_WORLD, "Some Body"), ADMIN_TOKEN, {
        "CF-Connecting-IP": ip,
      });
      statuses.push(response.status);
    }

    expect(statuses).not.toContain(429);
    // Das Profil existiert nicht -> auch autorisiert 404, aber mit Admin-Body; limitiert kommt der
    // generische Body. Beides 404 - geprüft wird, dass nie 429 die Existenz verrät.
    expect(statuses.every((status) => status === 404)).toBe(true);
  });
});

describe("admin endpoints - authorized", () => {
  it("GET shows the stored profile including createdAt/updatedAt but without editTokenHash", async () => {
    const name = uniqueName("Shown");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64(), note: "Hallo" });

    const response = await adminRequest("GET", adminProfilePath(KNOWN_WORLD.toLowerCase(), name));
    const json = await response.json<Record<string, unknown>>();

    expect(response.status).toBe(200);
    expect(json.characterName).toBe(name);
    expect(json.note).toBe("Hallo");
    expect(json.createdAt).toBeTruthy();
    expect(json.updatedAt).toBeTruthy();
    expect(json).not.toHaveProperty("editTokenHash");
    expect(JSON.stringify(json)).not.toMatch(/editTokenHash/);
  });

  it("DELETE removes the profile and its Discord card (message + index entry)", async () => {
    mutableEnv.DISCORD_WEBHOOK_NA = FAKE_WEBHOOK_URL_NA;
    const calls = recordDiscordCalls();

    const name = uniqueName("Fake");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64(), visibility: "listed" });
    const index = await env.BLUNION_PROFILES.get<{ characterName: string }[]>(playerCardsIndexKey("NA"), "json");
    expect(index?.some((entry) => entry.characterName === name)).toBe(true);

    // bewusst kleingeschriebene World - gelöscht wird trotzdem der kanonische Index-Eintrag
    const response = await adminRequest("DELETE", adminProfilePath(KNOWN_WORLD.toLowerCase(), name));

    expect(response.status).toBe(200);
    expect(await response.json()).toMatchObject({ deleted: true });
    expect((await getProfile(KNOWN_WORLD, name)).status).toBe(404);
    expect(calls.some((call) => call.method === "DELETE" && call.url.endsWith("/messages/fake-message-1"))).toBe(true);

    const indexAfter = await env.BLUNION_PROFILES.get<{ characterName: string }[]>(playerCardsIndexKey("NA"), "json");
    expect(indexAfter?.some((entry) => entry.characterName === name)).toBe(false);
  });

  it("DELETE also removes legacy profiles whose names fail isValidCharacterName", async () => {
    const legacyKey = "profile:gilgamesh:alice";
    await env.BLUNION_PROFILES.put(legacyKey, JSON.stringify({
      characterName: "Alice", world: KNOWN_WORLD, dataCenter: "Aether", spellBitmaskBase64: validBitmaskBase64(),
      editTokenHash: "x", visibility: "unlisted", createdAt: "2026-01-01T00:00:00.000Z", updatedAt: "2026-01-01T00:00:00.000Z",
    }));

    // Normale Route lehnt den Namen ab ...
    expect((await getProfile(KNOWN_WORLD, "Alice")).status).toBe(400);

    // ... die Admin-Route löscht ihn trotzdem.
    const response = await adminRequest("DELETE", adminProfilePath(KNOWN_WORLD, "Alice"));
    expect(response.status).toBe(200);
    expect(await env.BLUNION_PROFILES.get(legacyKey)).toBeNull();
  });

  it("DELETE on a missing profile returns 404 with an admin body", async () => {
    const response = await adminRequest("DELETE", adminProfilePath(KNOWN_WORLD, uniqueName("Missing")));

    expect(response.status).toBe(404);
    expect(await response.json()).toMatchObject({ error: expect.stringMatching(/Kein Profil/) });
  });

  it("DELETE /admin/group removes the group and its Discord card", async () => {
    mutableEnv.DISCORD_WEBHOOK_NA = FAKE_WEBHOOK_URL_NA;
    const calls = recordDiscordCalls();

    const groupId = uniqueGroupId();
    await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("Member") }],
      visibility: "listed",
    });
    expect(calls.filter((call) => call.method === "POST")).toHaveLength(1);

    const response = await adminRequest("DELETE", `/admin/group/${groupId}`);

    expect(response.status).toBe(200);
    expect(await env.BLUNION_PROFILES.get(`group:${groupId}`)).toBeNull();
    expect(calls.some((call) => call.method === "DELETE" && call.url.includes("/messages/"))).toBe(true);
  });

  it("DELETE /admin/group also accepts legacy non-UUID groupIds", async () => {
    await env.BLUNION_PROFILES.put("group:altlast-123", JSON.stringify({
      groupId: "altlast-123", members: [], editTokenHash: "x", visibility: "unlisted", dataCenter: "Aether",
      createdAt: "2026-01-01T00:00:00.000Z", updatedAt: "2026-01-01T00:00:00.000Z",
    }));

    const response = await adminRequest("DELETE", "/admin/group/altlast-123");

    expect(response.status).toBe(200);
    expect(await env.BLUNION_PROFILES.get("group:altlast-123")).toBeNull();
  });
});

describe("PUT /profile 409 reason", () => {
  it("reports name_taken when an existing profile is claimed without editToken", async () => {
    const name = uniqueName("Taken");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });

    const response = await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });

    expect(response.status).toBe(409);
    expect((await response.json<{ reason: string }>()).reason).toBe("name_taken");
  });

  it("reports token_mismatch for a wrong editToken", async () => {
    const name = uniqueName("Mismatch");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });

    const response = await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64(), editToken: "falsch" });

    expect(response.status).toBe(409);
    expect((await response.json<{ reason: string }>()).reason).toBe("token_mismatch");
  });
});

describe("English Discord texts", () => {
  it("renders group cards in English with readable availability tags", async () => {
    mutableEnv.DISCORD_WEBHOOK_NA = FAKE_WEBHOOK_URL_NA;
    const bodies: string[] = [];
    setDiscordFetchForTests((async (_input: RequestInfo | URL, init?: RequestInit) => {
      if (typeof init?.body === "string") bodies.push(init.body);
      return new Response(JSON.stringify({ id: "fake-message" }), { status: 200 });
    }) as typeof fetch);

    await putGroup(uniqueGroupId(), {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("Member") }],
      visibility: "listed",
      availabilityTags: ["morning", "weekend"],
      wantedPlayerCount: 1,
    });

    const embed = JSON.parse(bodies[0]!).embeds[0];
    expect(embed.title).toBe("Blue Mage group on Aether");
    expect(embed.fields[0].name).toBe("(no note)");
    expect(embed.fields[0].value).toMatch(/^Members \(1\/1\):/);
    expect(embed.fields[0].value).toContain("Availability: Morning, Weekend");
    expect(JSON.stringify(embed)).not.toMatch(/Mitglieder|Verfügbarkeit|Ziel-Spells|Gesucht|Notiz|sucht/);
  });

  it("uses the singular for one wanted player on player cards", async () => {
    mutableEnv.DISCORD_WEBHOOK_NA = FAKE_WEBHOOK_URL_NA;
    const bodies: string[] = [];
    setDiscordFetchForTests((async (_input: RequestInfo | URL, init?: RequestInit) => {
      if (typeof init?.body === "string") bodies.push(init.body);
      return new Response(JSON.stringify({ id: "fake-message" }), { status: 200 });
    }) as typeof fetch);

    const name = uniqueName("Single");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64(), visibility: "listed", wantedPlayerCount: 1 });

    const embed = JSON.parse(bodies[0]!).embeds[0];
    expect(embed.title).toBe(`${name} (${KNOWN_WORLD}) is looking for players`);
    expect(embed.description).toBe("Looking for: 1 player");
  });
});
