import { env } from "cloudflare:workers";
import { createExecutionContext, createScheduledController, waitOnExecutionContext } from "cloudflare:test";
import { afterEach, describe, expect, it } from "vitest";
import worker, { backfillKvMetadata } from "../src/index";
import { setDiscordFetchForTests } from "../src/discordWebhook";
import {
  browseGroups, browseProfiles, KNOWN_WORLD, KNOWN_WORLD_DATA_CENTER, putGroup, putProfile, uniqueGroupId, uniqueName,
  validBitmaskBase64,
} from "./helpers";

/**
 * KV-Metadata an "profile:"/"group:"-Keys (siehe BrowseKvMetadata, computePlayersBrowse und
 * backfillKvMetadata in src/index.ts): jeder Schreibpfad setzt sie, das Browse filtert damit vor,
 * Bestands-Keys ohne Metadata werden weiter per get() geprüft und vom Cron-Backfill nachgetragen.
 */

const OTHER_WORLD = "Odin"; // Light
const OTHER_DATA_CENTER = "Light";

const mutableEnv = env as unknown as Record<string, string | undefined>;

afterEach(() => {
  mutableEnv.DISCORD_WEBHOOK_NA = undefined;
  setDiscordFetchForTests(fetch);
});

function profileKey(world: string, characterName: string): string {
  return `profile:${world.toLowerCase()}:${characterName.toLowerCase()}`;
}

async function listedKey(key: string): Promise<KVNamespaceListKey<unknown> | undefined> {
  const result = await env.BLUNION_PROFILES.list({ prefix: key });
  return result.keys.find((entry) => entry.name === key);
}

function storedProfile(world: string, dataCenter: string, characterName: string, visibility: "listed" | "unlisted") {
  const now = new Date().toISOString();
  return {
    characterName,
    world,
    dataCenter,
    spellBitmaskBase64: validBitmaskBase64(),
    editTokenHash: "0".repeat(64),
    visibility,
    availabilityTags: [],
    note: "",
    wantedPlayerCount: 0,
    targetSpellIds: [],
    createdAt: now,
    updatedAt: now,
  };
}

/** Bestandsprofil wie vor der Umstellung: put() OHNE metadata. */
async function putLegacyProfile(
  characterName: string, visibility: "listed" | "unlisted" = "listed", world = KNOWN_WORLD, dataCenter = KNOWN_WORLD_DATA_CENTER,
): Promise<string> {
  const key = profileKey(world, characterName);
  await env.BLUNION_PROFILES.put(key, JSON.stringify(storedProfile(world, dataCenter, characterName, visibility)),
    { expirationTtl: 3600 });
  return key;
}

async function browsedNames(dataCenter = KNOWN_WORLD_DATA_CENTER): Promise<string[]> {
  const response = await browseProfiles(dataCenter);
  expect(response.status).toBe(200);
  return (await response.json<{ characterName: string }[]>()).map((player) => player.characterName);
}

/** Wiederholt den Backfill, bis nichts mehr offen ist - KV ist zwischen Tests derselben Datei nicht
 * isoliert, es können also auch Bestands-Keys anderer Tests dabei sein. */
async function backfillUntilDone(): Promise<void> {
  for (let run = 0; run < 50; run++) {
    const result = await backfillKvMetadata(env);
    if (!result.limitReached)
      return;
  }
  throw new Error("Backfill wurde nicht fertig");
}

describe("KV metadata on every write path", () => {
  it("PUT /profile writes visibility and lowercase dataCenter as metadata, and updates it on change", async () => {
    const name = uniqueName("Meta");
    const created = await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64(), visibility: "listed" });
    expect(created.status).toBe(201);
    const { editToken } = await created.json<{ editToken: string }>();

    expect((await listedKey(profileKey(KNOWN_WORLD, name)))?.metadata)
      .toEqual({ visibility: "listed", dataCenter: KNOWN_WORLD_DATA_CENTER.toLowerCase() });

    const updated = await putProfile(KNOWN_WORLD, name, {
      spellBitmaskBase64: validBitmaskBase64(), visibility: "unlisted", editToken,
    });
    expect(updated.status).toBe(200);
    expect((await listedKey(profileKey(KNOWN_WORLD, name)))?.metadata)
      .toEqual({ visibility: "unlisted", dataCenter: KNOWN_WORLD_DATA_CENTER.toLowerCase() });
  });

  it("PUT /group writes metadata", async () => {
    const groupId = uniqueGroupId();
    const response = await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }], visibility: "unlisted",
    });
    expect(response.status).toBe(201);

    expect((await listedKey(`group:${groupId}`))?.metadata)
      .toEqual({ visibility: "unlisted", dataCenter: KNOWN_WORLD_DATA_CENTER.toLowerCase() });
  });

  it("keeps the metadata when the Discord card sync rewrites the profile", async () => {
    mutableEnv.DISCORD_WEBHOOK_NA = "https://discord.com/api/webhooks/100000000000000001/test-token-na";
    setDiscordFetchForTests((async () =>
      new Response(JSON.stringify({ id: "fake-message-1" }), { status: 200 })) as unknown as typeof fetch);

    const name = uniqueName("MetaCard");
    expect((await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64(), visibility: "listed" })).status)
      .toBe(201);

    // Der zweite put() aus syncPlayerDiscordCard hat die Karte gespeichert - Metadata muss bleiben.
    const key = profileKey(KNOWN_WORLD, name);
    const stored = await env.BLUNION_PROFILES.get<{ discordCard?: { messageId: string } }>(key, "json");
    expect(stored?.discordCard?.messageId).toBe("fake-message-1");
    expect((await listedKey(key))?.metadata)
      .toEqual({ visibility: "listed", dataCenter: KNOWN_WORLD_DATA_CENTER.toLowerCase() });
  });
});

describe("browse with KV metadata", () => {
  it("lists profiles written with metadata and leaves out unlisted ones and other data centers", async () => {
    const listed = uniqueName("MetaListed");
    const unlisted = uniqueName("MetaUnlisted");
    const otherDc = uniqueName("MetaOtherDc");
    await putProfile(KNOWN_WORLD, listed, { spellBitmaskBase64: validBitmaskBase64(), visibility: "listed" });
    await putProfile(KNOWN_WORLD, unlisted, { spellBitmaskBase64: validBitmaskBase64(), visibility: "unlisted" });
    await putProfile(OTHER_WORLD, otherDc, { spellBitmaskBase64: validBitmaskBase64(), visibility: "listed" });

    const names = await browsedNames();
    expect(names).toContain(listed);
    expect(names).not.toContain(unlisted);
    expect(names).not.toContain(otherDc);
    expect(await browsedNames(OTHER_DATA_CENTER)).toContain(otherDc);
  });

  it("skips a key by its metadata alone, without reading the value", async () => {
    // Metadata sagt "unlisted", der Wert "listed": taucht er nicht auf, wurde nur die Metadata
    // gelesen. (Kann im Betrieb nicht entstehen - beides wird im selben put() geschrieben.)
    const name = uniqueName("MetaOnly");
    const key = profileKey(KNOWN_WORLD, name);
    await env.BLUNION_PROFILES.put(key, JSON.stringify(storedProfile(KNOWN_WORLD, KNOWN_WORLD_DATA_CENTER, name, "listed")), {
      expirationTtl: 3600, metadata: { visibility: "unlisted", dataCenter: KNOWN_WORLD_DATA_CENTER.toLowerCase() },
    });

    expect(await browsedNames()).not.toContain(name);
  });

  it("still checks the stored value after a metadata match", async () => {
    const name = uniqueName("MetaStale");
    const key = profileKey(KNOWN_WORLD, name);
    await env.BLUNION_PROFILES.put(key, JSON.stringify(storedProfile(KNOWN_WORLD, KNOWN_WORLD_DATA_CENTER, name, "unlisted")), {
      expirationTtl: 3600, metadata: { visibility: "listed", dataCenter: KNOWN_WORLD_DATA_CENTER.toLowerCase() },
    });

    expect(await browsedNames()).not.toContain(name);
  });

  it("filters groups by metadata the same way", async () => {
    const listedGroup = uniqueGroupId();
    await putGroup(listedGroup, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }], visibility: "listed", note: "Meta-Gruppe",
    });
    const metadataOnlyGroup = uniqueGroupId();
    await env.BLUNION_PROFILES.put(`group:${metadataOnlyGroup}`, JSON.stringify({
      groupId: metadataOnlyGroup, members: [], editTokenHash: "0".repeat(64), visibility: "listed",
      dataCenter: KNOWN_WORLD_DATA_CENTER, createdAt: "", updatedAt: "",
    }), { expirationTtl: 3600, metadata: { visibility: "listed", dataCenter: "light" } });

    const groupIds = (await (await browseGroups(KNOWN_WORLD_DATA_CENTER)).json<{ groupId: string }[]>())
      .map((group) => group.groupId);
    expect(groupIds).toContain(listedGroup);
    expect(groupIds).not.toContain(metadataOnlyGroup);
  });
});

describe("browse with mixed legacy and new keys", () => {
  it("reads legacy keys without metadata via get() and combines them with new keys", async () => {
    const legacyListed = uniqueName("LegacyListed");
    const legacyUnlisted = uniqueName("LegacyUnlisted");
    const legacyOtherDc = uniqueName("LegacyOtherDc");
    const fresh = uniqueName("FreshListed");
    await putLegacyProfile(legacyListed, "listed");
    await putLegacyProfile(legacyUnlisted, "unlisted");
    await putLegacyProfile(legacyOtherDc, "listed", OTHER_WORLD, OTHER_DATA_CENTER);
    await putProfile(KNOWN_WORLD, fresh, { spellBitmaskBase64: validBitmaskBase64(), visibility: "listed" });

    expect((await listedKey(profileKey(KNOWN_WORLD, legacyListed)))?.metadata).toBeUndefined();

    const names = await browsedNames();
    expect(names).toEqual(expect.arrayContaining([legacyListed, fresh]));
    expect(names).not.toContain(legacyUnlisted);
    expect(names).not.toContain(legacyOtherDc);
  });

  it("keeps listing a legacy profile after the backfill added its metadata", async () => {
    const name = uniqueName("LegacyUpgrade");
    await putLegacyProfile(name, "listed");
    expect(await browsedNames()).toContain(name);

    await backfillUntilDone();
    expect((await listedKey(profileKey(KNOWN_WORLD, name)))?.metadata)
      .toEqual({ visibility: "listed", dataCenter: KNOWN_WORLD_DATA_CENTER.toLowerCase() });
    expect(await browsedNames()).toContain(name);
  });
});

describe("backfillKvMetadata", () => {
  it("adds metadata to legacy profile and group keys, keeping value and expiration", async () => {
    const name = uniqueName("Backfill");
    const key = await putLegacyProfile(name, "unlisted", OTHER_WORLD, OTHER_DATA_CENTER);
    const groupId = uniqueGroupId();
    const groupKey = `group:${groupId}`;
    const groupValue = JSON.stringify({
      groupId, members: [], editTokenHash: "0".repeat(64), visibility: "listed", dataCenter: KNOWN_WORLD_DATA_CENTER,
      createdAt: "", updatedAt: "",
    });
    await env.BLUNION_PROFILES.put(groupKey, groupValue, { expirationTtl: 7200 });

    const valueBefore = await env.BLUNION_PROFILES.get(key, "text");
    const expirationBefore = (await listedKey(key))?.expiration;
    const groupExpirationBefore = (await listedKey(groupKey))?.expiration;
    expect(expirationBefore).toBeDefined();

    await backfillUntilDone();

    const after = await listedKey(key);
    expect(after?.metadata).toEqual({ visibility: "unlisted", dataCenter: "light" });
    expect(after?.expiration).toBe(expirationBefore);
    expect(await env.BLUNION_PROFILES.get(key, "text")).toBe(valueBefore);

    const groupAfter = await listedKey(groupKey);
    expect(groupAfter?.metadata).toEqual({ visibility: "listed", dataCenter: KNOWN_WORLD_DATA_CENTER.toLowerCase() });
    expect(groupAfter?.expiration).toBe(groupExpirationBefore);
    expect(await env.BLUNION_PROFILES.get(groupKey, "text")).toBe(groupValue);
  });

  it("respects the per-run limit and continues on the next run", async () => {
    await backfillUntilDone(); // Ausgangslage: nichts offen
    const keys = await Promise.all([1, 2, 3].map(() => putLegacyProfile(uniqueName("BackfillLimit"))));

    const first = await backfillKvMetadata(env, 2);
    expect(first).toEqual({ updated: 2, skipped: 0, limitReached: true });

    const second = await backfillKvMetadata(env, 2);
    expect(second).toEqual({ updated: 1, skipped: 0, limitReached: false });

    for (const key of keys)
      expect(readMetadataVisibility(await listedKey(key))).toBe("listed");
  });

  it("is idempotent - a second run writes nothing", async () => {
    await putLegacyProfile(uniqueName("BackfillIdem"));
    await backfillUntilDone();

    expect(await backfillKvMetadata(env)).toEqual({ updated: 0, skipped: 0, limitReached: false });
  });

  it("skips values it cannot read and keys that expire within a minute", async () => {
    await backfillUntilDone();
    const brokenKey = profileKey(KNOWN_WORLD, uniqueName("BackfillBroken"));
    await env.BLUNION_PROFILES.put(brokenKey, "kein json", { expirationTtl: 3600 });
    const soonKey = await putLegacyProfile(uniqueName("BackfillSoon"));
    const soonExpiration = (await listedKey(soonKey))!.expiration!;

    // "Jetzt" kurz vor dem Ablauf von soonKey.
    const result = await backfillKvMetadata(env, 100, (soonExpiration - 30) * 1000);
    expect(result.updated).toBe(0);
    expect(result.skipped).toBe(2);
    expect((await listedKey(brokenKey))?.metadata).toBeUndefined();
    expect(await env.BLUNION_PROFILES.get(brokenKey, "text")).toBe("kein json");

    await env.BLUNION_PROFILES.delete(brokenKey); // sonst bliebe er für folgende Tests "offen"
    await backfillUntilDone();
  });

  it("runs as part of the daily cron", async () => {
    const key = await putLegacyProfile(uniqueName("BackfillCron"));

    const ctx = createExecutionContext();
    await worker.scheduled(createScheduledController(), env, ctx);
    await waitOnExecutionContext(ctx);

    expect(readMetadataVisibility(await listedKey(key))).toBe("listed");
  });
});

function readMetadataVisibility(entry: KVNamespaceListKey<unknown> | undefined): unknown {
  return (entry?.metadata as { visibility?: unknown } | undefined)?.visibility;
}
