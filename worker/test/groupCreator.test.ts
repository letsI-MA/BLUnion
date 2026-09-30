import { env } from "cloudflare:workers";
import { afterEach, describe, expect, it } from "vitest";
import { buildGroupsBrowseEmbed, buildPlayersBrowseEmbed } from "../src/index";
import { setDiscordFetchForTests } from "../src/discordWebhook";
import {
  browseGroups, callWorker, groupUrl, KNOWN_WORLD, KNOWN_WORLD_DATA_CENTER, putGroup, putProfile, uniqueGroupId,
  uniqueName, validBitmaskBase64,
} from "./helpers";

/**
 * B1 (Gruppen nur mit echten Live-Sync-Profilen, siehe verifyGroupCreator in src/index.ts) und A5
 * (Discord-Embed-Gesamtlimit für die Browse-Embeds).
 */

const mutableEnv = env as unknown as Record<string, string | undefined>;

function setRequireGroupCreator(value: "true" | "false"): void {
  mutableEnv.REQUIRE_GROUP_CREATOR = value;
}

afterEach(() => {
  setRequireGroupCreator("false");
  mutableEnv.DISCORD_WEBHOOK_NA = undefined;
  setDiscordFetchForTests(fetch);
});

/** Legt ein echtes Profil an und liefert Name + Edit-Token. */
async function createProfile(prefix: string, visibility: "listed" | "unlisted" = "unlisted") {
  const characterName = uniqueName(prefix);
  const response = await putProfile(KNOWN_WORLD, characterName, { spellBitmaskBase64: validBitmaskBase64(), visibility });
  const { editToken } = await response.json<{ editToken: string }>();
  return { world: KNOWN_WORLD, characterName, editToken };
}

async function putGroupAsCreator(
  groupId: string,
  body: Record<string, unknown>,
  creator: { world: string; characterName: string } | null,
  creatorToken: string | null,
): Promise<Response> {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (creatorToken !== null) headers["X-Creator-Token"] = creatorToken;

  const fullBody = creator
    ? { ...body, creatorWorld: creator.world, creatorCharacterName: creator.characterName }
    : body;

  return callWorker(new Request(groupUrl(groupId), { method: "PUT", headers, body: JSON.stringify(fullBody) }));
}

async function readStoredGroup(groupId: string): Promise<Record<string, unknown> | null> {
  return env.BLUNION_PROFILES.get<Record<string, unknown>>(`group:${groupId}`, "json");
}

describe("B1 PUT /group - creator verification with REQUIRE_GROUP_CREATOR=false", () => {
  it("still accepts old clients without any creator fields", async () => {
    const response = await putGroup(uniqueGroupId(), {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("Legacy") }],
    });

    expect(response.status).toBe(201);
  });

  it("verifies and stores a valid creator, but never returns it", async () => {
    const creator = await createProfile("Creator");
    const other = await createProfile("Other");
    const groupId = uniqueGroupId();

    const response = await putGroupAsCreator(groupId, {
      members: [
        { world: creator.world, characterName: creator.characterName },
        { world: other.world, characterName: other.characterName },
      ],
    }, creator, creator.editToken);

    expect(response.status).toBe(201);
    const json = await response.json<Record<string, unknown>>();
    expect(json).not.toHaveProperty("creator");
    expect(JSON.stringify(json)).not.toMatch(/"creator(World|CharacterName)?":/i); // kein creator-/creatorWorld-Schlüssel

    const stored = await readStoredGroup(groupId);
    expect(stored?.creator).toEqual({ world: KNOWN_WORLD, characterName: creator.characterName });
  });

  it("checks creator fields even when the flag is off (wrong token -> 403)", async () => {
    const creator = await createProfile("Creator");

    const response = await putGroupAsCreator(uniqueGroupId(), {
      members: [{ world: creator.world, characterName: creator.characterName }],
    }, creator, "falscher-token");

    expect(response.status).toBe(403);
  });

  it("keeps an already stored creator when an old client updates without creator fields", async () => {
    const creator = await createProfile("Creator");
    const groupId = uniqueGroupId();
    const members = [{ world: creator.world, characterName: creator.characterName }];

    const created = await putGroupAsCreator(groupId, { members }, creator, creator.editToken);
    const { editToken } = await created.json<{ editToken: string }>();

    const updated = await putGroup(groupId, { members, editToken, note: "Update" });
    expect(updated.status).toBe(200);
    expect((await readStoredGroup(groupId))?.creator).toEqual({ world: KNOWN_WORLD, characterName: creator.characterName });
  });
});

describe("B1 PUT /group - creator verification with REQUIRE_GROUP_CREATOR=true", () => {
  it("rejects a request without creator fields with 400", async () => {
    setRequireGroupCreator("true");

    const response = await putGroup(uniqueGroupId(), {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("Legacy") }],
    });

    expect(response.status).toBe(400);
  });

  it("accepts a valid creator whose members all have profiles", async () => {
    setRequireGroupCreator("true");
    const creator = await createProfile("Creator");
    const other = await createProfile("Other", "listed");

    const response = await putGroupAsCreator(uniqueGroupId(), {
      members: [
        { world: creator.world, characterName: creator.characterName },
        // Groß-/Kleinschreibung egal wie beim KV-Key
        { world: other.world.toLowerCase(), characterName: other.characterName },
      ],
    }, { world: creator.world.toUpperCase(), characterName: creator.characterName }, creator.editToken);

    expect(response.status).toBe(201);
  });

  it("rejects a wrong X-Creator-Token with 403", async () => {
    setRequireGroupCreator("true");
    const creator = await createProfile("Creator");
    const other = await createProfile("Other");

    const response = await putGroupAsCreator(uniqueGroupId(), {
      members: [{ world: creator.world, characterName: creator.characterName }],
    }, creator, other.editToken); // Token eines ANDEREN Profils

    expect(response.status).toBe(403);
  });

  it("rejects a missing X-Creator-Token header with 403", async () => {
    setRequireGroupCreator("true");
    const creator = await createProfile("Creator");

    const response = await putGroupAsCreator(uniqueGroupId(), {
      members: [{ world: creator.world, characterName: creator.characterName }],
    }, creator, null);

    expect(response.status).toBe(403);
  });

  it("rejects a creator without an own profile with 403", async () => {
    setRequireGroupCreator("true");
    const ghost = { world: KNOWN_WORLD, characterName: uniqueName("Ghost") };

    const response = await putGroupAsCreator(uniqueGroupId(), { members: [ghost] }, ghost, "irgendein-token");

    expect(response.status).toBe(403);
  });

  it("rejects a creator who is not in members[] with 403", async () => {
    setRequireGroupCreator("true");
    const creator = await createProfile("Creator");
    const other = await createProfile("Other");

    const response = await putGroupAsCreator(uniqueGroupId(), {
      members: [{ world: other.world, characterName: other.characterName }],
    }, creator, creator.editToken);

    expect(response.status).toBe(403);
    expect((await response.json<{ error: string }>()).error).toMatch(/members/);
  });

  it("rejects members without a profile with 400 and a machine-readable missingMembers list", async () => {
    setRequireGroupCreator("true");
    const creator = await createProfile("Creator");
    const strangerA = { world: KNOWN_WORLD, characterName: uniqueName("Stranger") };
    const strangerB = { world: "Cerberus", characterName: uniqueName("Fremder") };

    const response = await putGroupAsCreator(uniqueGroupId(), {
      members: [{ world: creator.world, characterName: creator.characterName }, strangerA, strangerB],
    }, creator, creator.editToken);

    expect(response.status).toBe(400);
    const json = await response.json<{ error: string; missingMembers: { world: string; characterName: string }[] }>();
    expect(json.missingMembers).toEqual([strangerA, strangerB]);
    expect(json.error).toContain(strangerA.characterName);
  });

  it("checks every PUT, so an update cannot add a member without profile", async () => {
    setRequireGroupCreator("true");
    const creator = await createProfile("Creator");
    const groupId = uniqueGroupId();
    const members = [{ world: creator.world, characterName: creator.characterName }];

    const created = await putGroupAsCreator(groupId, { members }, creator, creator.editToken);
    const { editToken } = await created.json<{ editToken: string }>();

    const stranger = { world: KNOWN_WORLD, characterName: uniqueName("Stranger") };
    const updated = await putGroupAsCreator(groupId, { members: [...members, stranger], editToken }, creator, creator.editToken);

    expect(updated.status).toBe(400);
    expect((await updated.json<{ missingMembers: unknown[] }>()).missingMembers).toEqual([stranger]);
  });

  it("rejects an update with a wrong creator token even if the group editToken is correct", async () => {
    setRequireGroupCreator("true");
    const creator = await createProfile("Creator");
    const groupId = uniqueGroupId();
    const members = [{ world: creator.world, characterName: creator.characterName }];

    const created = await putGroupAsCreator(groupId, { members }, creator, creator.editToken);
    const { editToken } = await created.json<{ editToken: string }>();

    const updated = await putGroupAsCreator(groupId, { members, editToken }, creator, "falsch");
    expect(updated.status).toBe(403);
  });
});

describe("B1 creator is never exposed", () => {
  it("is not part of GET /groups/browse or the Discord card embed", async () => {
    mutableEnv.DISCORD_WEBHOOK_NA = "https://discord.com/api/webhooks/100000000000000001/test-token-na";
    const discordBodies: string[] = [];
    setDiscordFetchForTests((async (_input: RequestInfo | URL, init?: RequestInit) => {
      if (typeof init?.body === "string") discordBodies.push(init.body);
      return new Response(JSON.stringify({ id: "fake-message" }), { status: 200 });
    }) as typeof fetch);

    const creator = await createProfile("Creator");
    const groupId = uniqueGroupId();
    const created = await putGroupAsCreator(groupId, {
      members: [{ world: creator.world, characterName: creator.characterName }],
      visibility: "listed",
    }, creator, creator.editToken);
    expect(created.status).toBe(201);

    const browse = await browseGroups(KNOWN_WORLD_DATA_CENTER);
    const entry = (await browse.json<Record<string, unknown>[]>()).find((group) => group.groupId === groupId);
    expect(entry).toBeDefined();
    expect(entry).not.toHaveProperty("creator");
    expect(JSON.stringify(entry)).not.toMatch(/"creator(World|CharacterName)?":/i);

    expect(discordBodies.length).toBeGreaterThan(0);
    for (const body of discordBodies)
      expect(body).not.toMatch(/"creator(World|CharacterName)?":|Ersteller/i);
  });

  it("allows the X-Creator-Token header in CORS", async () => {
    const response = await callWorker(new Request(groupUrl(uniqueGroupId()), { method: "OPTIONS" }));
    expect(response.headers.get("Access-Control-Allow-Headers")).toContain("X-Creator-Token");
  });
});

describe("A5 Discord browse embeds stay within the 6000-character total", () => {
  function embedLength(embed: Record<string, unknown>): number {
    const fields = (embed.fields as { name: string; value: string }[]) ?? [];
    return String(embed.title ?? "").length
      + String(embed.description ?? "").length
      + String((embed.footer as { text?: string } | undefined)?.text ?? "").length
      + fields.reduce((sum, field) => sum + field.name.length + field.value.length, 0);
  }

  const longGroup = (index: number) => ({
    groupId: `group-${index}`,
    members: Array.from({ length: 8 }, (_, member) => ({
      world: "Midgardsormr",
      characterName: `Abcdefghijklmn${String.fromCharCode(97 + member)} Xyzxyz`,
    })),
    availabilityTags: ["morning", "afternoon", "evening", "weekend", "flexible"],
    note: `Gruppe ${index} `.padEnd(60, "x"),
    wantedPlayerCount: 8,
    targetSpellIds: [11383, 11384],
  });

  it("stops adding group fields before 6000 characters and adds a '+N more' footer", () => {
    const groups = Array.from({ length: 25 }, (_, index) => longGroup(index));

    const embed = buildGroupsBrowseEmbed("Aether", groups);
    const fields = embed.fields as unknown[];

    expect(embedLength(embed)).toBeLessThanOrEqual(6000);
    expect(fields.length).toBeLessThan(25);
    expect(fields.length).toBeGreaterThan(0);
    expect((embed.footer as { text: string }).text).toBe(`+${25 - fields.length} more - see website`);
    expect(embed.description).toBe(`Showing ${fields.length} of 25 listed groups.`);
  });

  it("caps player fields at 25 with a footer for the rest", () => {
    const players = Array.from({ length: 30 }, (_, index) => ({
      characterName: `Player ${String.fromCharCode(97 + (index % 26))}${index}`,
      world: KNOWN_WORLD,
      spellBitmaskBase64: validBitmaskBase64(),
      availabilityTags: ["evening"],
      note: "kurz",
      wantedPlayerCount: 1,
      targetSpellIds: [],
      updatedAt: "2026-01-01T00:00:00.000Z",
    }));

    const embed = buildPlayersBrowseEmbed("Aether", players);

    expect((embed.fields as unknown[]).length).toBe(25);
    expect((embed.footer as { text: string }).text).toBe("+5 more - see website");
    expect(embedLength(embed)).toBeLessThanOrEqual(6000);
  });

  it("shows everything without footer/description when it fits", () => {
    const embed = buildGroupsBrowseEmbed("Aether", [longGroup(1), longGroup(2)]);

    expect((embed.fields as unknown[]).length).toBe(2);
    expect(embed.footer).toBeUndefined();
    expect(embed.description).toBeUndefined();
  });
});
