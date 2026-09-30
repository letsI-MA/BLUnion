import { env } from "cloudflare:workers";
import { afterEach, describe, expect, it } from "vitest";
import {
  buildPlayerCardEmbed, isValidCharacterName, isValidGroupId, sanitizeForDiscord, truncateForDiscord,
} from "../src/index";
import { setDiscordFetchForTests } from "../src/discordWebhook";
import { lookupCanonicalDataCenter } from "../src/worlds";
import {
  browseGroups, browseProfiles, callWorker, deleteGroup, deleteProfile, getProfile, groupUrl, KNOWN_WORLD,
  KNOWN_WORLD_DATA_CENTER, profileUrl, putGroup, putProfile, uniqueGroupId, uniqueName, validBitmaskBase64,
} from "./helpers";

/**
 * Security-Härtung A1-A4 (siehe worker/src/index.ts): Namens-/groupId-Validierung, Discord-
 * Sanitizing inkl. allowed_mentions, dataCenter-Allowlist, normalisierter Cache-Key und
 * Lese-Rate-Limit.
 */

const FAKE_WEBHOOK_URL_NA = "https://discord.com/api/webhooks/100000000000000001/test-token-na";

interface RecordedDiscordBody {
  embeds?: { title?: string; description?: string; fields?: { name: string; value: string }[] }[];
  allowed_mentions?: { parse: string[] };
}

function recordDiscordCalls(): { method: string; body: RecordedDiscordBody | undefined }[] {
  const calls: { method: string; body: RecordedDiscordBody | undefined }[] = [];
  setDiscordFetchForTests((async (_input: RequestInfo | URL, init?: RequestInit) => {
    const method = init?.method ?? "GET";
    calls.push({ method, body: typeof init?.body === "string" ? JSON.parse(init.body) : undefined });
    return method === "POST"
      ? new Response(JSON.stringify({ id: `fake-message-${calls.length}` }), { status: 200 })
      : new Response(JSON.stringify({ ok: true }), { status: 200 });
  }) as typeof fetch);
  return calls;
}

afterEach(() => {
  (env as unknown as Record<string, string | undefined>).DISCORD_WEBHOOK_NA = undefined;
  setDiscordFetchForTests(fetch);
});

describe("A1 isValidCharacterName", () => {
  it.each(["Alice Test", "Y'shtola Rhul", "Jean-Luc Picard", "Ab Cd", "Abcdefghij Abcdefghij"])(
    "accepts %s", (name) => {
      expect(isValidCharacterName(name)).toBe(true);
    });

  it.each([
    ["only one part", "Alice"],
    ["three parts", "Alice Bob Test"],
    ["two spaces", "Alice  Test"],
    ["leading space", " Alice Test"],
    ["part too short", "A Test"],
    ["part too long (16)", "Abcdefghijklmnop Test"],
    ["total length 22", "Abcdefghijklmno Abcdef"],
    ["starts with apostrophe", "'Alice Test"],
    ["starts with hyphen", "Alice -Test"],
    ["digit", "Al1ce Test"],
    ["underscore", "Alice_x Test"],
    ["non-ASCII letter", "Älice Test"],
    ["markdown", "Alice [Test]"],
    ["empty", ""],
  ])("rejects %s (%s)", (_label, name) => {
    expect(isValidCharacterName(name)).toBe(false);
  });

  it("rejects PUT/GET/DELETE /profile with an invalid name with 400", async () => {
    const invalid = "Not_A Valid Name";

    const put = await putProfile(KNOWN_WORLD, invalid, { spellBitmaskBase64: validBitmaskBase64() });
    const get = await getProfile(KNOWN_WORLD, invalid);
    const del = await deleteProfile(KNOWN_WORLD, invalid, "irgendein-token");

    for (const response of [put, get, del]) {
      expect(response.status).toBe(400);
      expect((await response.json<{ error: string }>()).error).toMatch(/Charaktername/);
    }
  });

  it("rejects PUT /group when any members[].characterName is invalid with 400", async () => {
    const response = await putGroup(uniqueGroupId(), {
      members: [
        { world: KNOWN_WORLD, characterName: uniqueName("Valid") },
        { world: KNOWN_WORLD, characterName: "[Klick](https://evil.example)" },
      ],
    });

    expect(response.status).toBe(400);
    expect((await response.json<{ error: string }>()).error).toMatch(/Charaktername/);
  });
});

describe("A2 groupId validation", () => {
  it("accepts UUIDs in any case", () => {
    expect(isValidGroupId("0f8fad5b-d9cb-469f-a165-70867728950e")).toBe(true);
    expect(isValidGroupId("0F8FAD5B-D9CB-469F-A165-70867728950E")).toBe(true);
  });

  it.each(["not-a-uuid", "0f8fad5b-d9cb-469f-a165-70867728950", "0f8fad5bd9cb469fa16570867728950e", "../profile:x"])(
    "rejects %s", (groupId) => {
      expect(isValidGroupId(groupId)).toBe(false);
    });

  it("rejects PUT and DELETE /group/:groupId with a non-UUID groupId with 400", async () => {
    const put = await putGroup("group-kein-uuid", {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("Member") }],
    });
    const del = await deleteGroup("group-kein-uuid", "irgendein-token");

    expect(put.status).toBe(400);
    expect(del.status).toBe(400);
  });
});

describe("A3 sanitizeForDiscord / truncateForDiscord", () => {
  it("removes markdown links and URLs and neutralizes @everyone", () => {
    const sanitized = sanitizeForDiscord("[Gratis Gil](https://evil.example/login) @everyone");

    expect(sanitized).not.toMatch(/https?:/);
    expect(sanitized).not.toContain("evil.example");
    expect(sanitized).toContain("\\[Gratis Gil\\]");
    expect(sanitized).not.toMatch(/(^|[^\\])[[\]()]/); // jede Klammer ist escaped
    expect(sanitized).toContain("@\u200beveryone");
    expect(sanitized).not.toContain("@everyone");
  });

  it("removes www./discord.gg/discord.com/invite links and neutralizes @here", () => {
    const sanitized = sanitizeForDiscord("join www.evil.example discord.gg/abc discord.com/invite/xyz @here");

    expect(sanitized).not.toMatch(/www\.|discord\.gg|discord\.com/);
    expect(sanitized).toContain("@\u200bhere");
  });

  it("replaces control characters and line breaks with spaces", () => {
    expect(sanitizeForDiscord("a\nb\r\nc\td\u2028e")).toBe("a b  c d e");
  });

  it("escapes every markdown character", () => {
    expect(sanitizeForDiscord("\\*_~`|>[]()#")).toBe("\\\\\\*\\_\\~\\`\\|\\>\\[\\]\\(\\)\\#");
  });

  it("truncates to the limit with an ellipsis and never splits a surrogate pair", () => {
    expect(truncateForDiscord("abcdef", 4)).toBe("abc…");
    expect(truncateForDiscord("abc", 4)).toBe("abc");

    const truncated = truncateForDiscord("ab😀cd", 4); // 😀 = 2 UTF-16-Einheiten an Index 2/3
    expect(truncated).toBe("ab…");
  });

  it("keeps player card title/description within Discord's limits", () => {
    const embed = buildPlayerCardEmbed({
      characterName: "A".repeat(300),
      world: KNOWN_WORLD,
      dataCenter: KNOWN_WORLD_DATA_CENTER,
      spellBitmaskBase64: validBitmaskBase64(),
      editTokenHash: "x",
      visibility: "listed",
      note: "x".repeat(5000),
      createdAt: "2026-01-01T00:00:00.000Z",
      updatedAt: "2026-01-01T00:00:00.000Z",
    });

    expect((embed.title as string).length).toBeLessThanOrEqual(256);
    expect((embed.description as string).length).toBeLessThanOrEqual(4096);
  });

  it("sanitizes the note of a real group card and sends allowed_mentions: { parse: [] } on create and edit", async () => {
    (env as unknown as Record<string, string | undefined>).DISCORD_WEBHOOK_NA = FAKE_WEBHOOK_URL_NA;
    const calls = recordDiscordCalls();

    const groupId = uniqueGroupId();
    const members = [{ world: KNOWN_WORLD, characterName: uniqueName("Member") }];
    const created = await putGroup(groupId, {
      members,
      visibility: "listed",
      note: "[Link](https://evil.example) @everyone",
    });
    const { editToken } = await created.json<{ editToken: string }>();

    // Die JSON-API-Antwort bleibt unverändert (Clients escapen selbst).
    await putGroup(groupId, { members, editToken, note: "update @here" });

    const post = calls.find((call) => call.method === "POST")!;
    const patch = calls.find((call) => call.method === "PATCH")!;

    const fieldName = post.body!.embeds![0]!.fields![0]!.name;
    expect(fieldName).not.toContain("https://");
    expect(fieldName).not.toContain("@everyone");
    expect(fieldName).toContain("\\[Link\\]");

    expect(patch.body!.embeds![0]!.fields![0]!.name).toContain("@\u200bhere");

    expect(post.body!.allowed_mentions).toEqual({ parse: [] });
    expect(patch.body!.allowed_mentions).toEqual({ parse: [] });
  });

  it("does not alter the note in the JSON API response", async () => {
    const note = "[Link](https://x.example) @everyone";
    const response = await putGroup(uniqueGroupId(), {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("Member") }],
      note,
    });

    expect((await response.json<{ note: string }>()).note).toBe(note);
  });
});

describe("A4 dataCenter allowlist, cache key and read rate limit", () => {
  it("lookupCanonicalDataCenter is case-insensitive and rejects unknown values", () => {
    expect(lookupCanonicalDataCenter("aEtHeR")).toBe("Aether");
    expect(lookupCanonicalDataCenter("Materia")).toBe("Materia");
    expect(lookupCanonicalDataCenter("Shadow")).toBeNull();
    expect(lookupCanonicalDataCenter("Aether2")).toBeNull();
  });

  it("rejects an unknown dataCenter on both browse endpoints with 400", async () => {
    const profiles = await browseProfiles("KeinSolchesDC");
    const groups = await browseGroups("KeinSolchesDC");

    expect(profiles.status).toBe(400);
    expect(groups.status).toBe(400);
  });

  it("accepts a dataCenter in any case", async () => {
    const response = await browseProfiles(KNOWN_WORLD_DATA_CENTER.toLowerCase());
    expect(response.status).toBe(200);
  });

  it("uses a normalized cache key that ignores extra query parameters and dataCenter casing", async () => {
    const origin = `http://cachekey-${Date.now()}-${Math.random().toString(36).slice(2)}.example.com`;
    const browse = (query: string) => callWorker(new Request(`${origin}/profiles/browse?${query}`, { method: "GET" }));

    const first = uniqueName("Cached");
    await putProfile(KNOWN_WORLD, first, { spellBitmaskBase64: validBitmaskBase64(), visibility: "listed" });
    const firstResult = await (await browse("dataCenter=Aether&bust=1")).json<{ characterName: string }[]>();
    expect(firstResult.map((p) => p.characterName)).toContain(first);

    // Neues Profil NACH dem ersten Browse - ein anderer Zusatzparameter und andere Schreibweise
    // dürfen den Cache NICHT umgehen, das neue Profil darf also noch nicht auftauchen.
    const second = uniqueName("Uncached");
    await putProfile(KNOWN_WORLD, second, { spellBitmaskBase64: validBitmaskBase64(), visibility: "listed" });
    const secondResult = await (await browse("bust=2&dataCenter=aether&foo=bar")).json<{ characterName: string }[]>();

    expect(secondResult.map((p) => p.characterName)).toContain(first);
    expect(secondResult.map((p) => p.characterName)).not.toContain(second);
  });

  it("returns 429 on read endpoints after exceeding the read limit for a single IP", async () => {
    const ip = "198.51.100." + Math.floor(Math.random() * 255);
    const name = uniqueName("ReadLimited");

    // wrangler.toml READ_RATE_LIMITER: limit = 60, period = 60. 2 * limit + 1 Anfragen statt
    // limit + 1: der Limiter zählt in festen Zeitfenstern - fällt eine Fenstergrenze mitten in den
    // Test, würden 61 Anfragen auf zwei Fenster verteilt nie 429 auslösen (lokal beobachtet).
    const statuses: number[] = [];
    for (let i = 0; i < 121; i++) {
      const response = await callWorker(new Request(profileUrl(KNOWN_WORLD, name), {
        method: "GET",
        headers: { "CF-Connecting-IP": ip },
      }));
      statuses.push(response.status);
    }

    expect(statuses).toContain(429);
  });

  it("rate-limits the browse endpoints with the read limiter", async () => {
    const ip = "198.51.100." + Math.floor(Math.random() * 255);
    const statuses: number[] = [];
    for (let i = 0; i < 121; i++) { // 2 * limit + 1, siehe Test oben
      const response = await callWorker(new Request(
        `http://example.com/groups/browse?dataCenter=${KNOWN_WORLD_DATA_CENTER}`,
        { method: "GET", headers: { "CF-Connecting-IP": ip } },
      ));
      statuses.push(response.status);
    }

    expect(statuses).toContain(429);
  });

  it("does not rate-limit reads without a CF-Connecting-IP header", async () => {
    const response = await callWorker(new Request(groupUrl(uniqueGroupId()), { method: "OPTIONS" }));
    expect(response.status).toBe(204);
    expect((await getProfile(KNOWN_WORLD, uniqueName("NoIp"))).status).toBe(404);
  });
});
