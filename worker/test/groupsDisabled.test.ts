import { env } from "cloudflare:workers";
import { createExecutionContext, createScheduledController, waitOnExecutionContext } from "cloudflare:test";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import worker, { GROUPS_DISABLED_MESSAGE, groupsEnabled } from "../src/index";
import type { Env } from "../src/index";
import { setDiscordFetchForTests } from "../src/discordWebhook";
import {
  browseGroups, callWorker, deleteGroup, KNOWN_WORLD, KNOWN_WORLD_DATA_CENTER, putGroup, putProfile, uniqueGroupId,
  uniqueBrowseOrigin, uniqueName, validBitmaskBase64,
} from "./helpers";

/**
 * GROUPS_ENABLED = "false" (siehe Env.GROUPS_ENABLED in src/index.ts). Der Testlauf startet mit "true"
 * (vitest.config.mts) - hier wird pro Test gezielt umgeschaltet: Gruppen werden bei Bedarf noch MIT
 * eingeschaltetem Feature angelegt (wie Bestandsdaten aus der Zeit davor) und erst danach abgeschaltet.
 */

const mutableEnv = env as unknown as Record<string, string | undefined>;
const FAKE_WEBHOOK_URL_NA = "https://discord.com/api/webhooks/100000000000000001/test-token-na";
const ADMIN_TOKEN = "test-admin-token-with-enough-entropy";

function disableGroups(): void {
  mutableEnv.GROUPS_ENABLED = "false";
}

let originalAdminToken: string | undefined;

beforeEach(() => {
  originalAdminToken = mutableEnv.ADMIN_TOKEN;
});

afterEach(() => {
  mutableEnv.GROUPS_ENABLED = "true";
  mutableEnv.DISCORD_WEBHOOK_NA = undefined;
  mutableEnv.ADMIN_TOKEN = originalAdminToken;
  setDiscordFetchForTests(fetch);
});

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

async function putListedGroup(groupId: string): Promise<string> {
  const response = await putGroup(groupId, {
    members: [{ world: KNOWN_WORLD, characterName: uniqueName("Member") }],
    visibility: "listed",
    note: "Disabled-Testgruppe",
  });
  expect(response.status).toBe(201);
  return (await response.json<{ editToken: string }>()).editToken;
}

describe("groupsEnabled", () => {
  it('is only false for exactly "false"', () => {
    expect(groupsEnabled({ GROUPS_ENABLED: "false" } as Env)).toBe(false);
    expect(groupsEnabled({ GROUPS_ENABLED: "true" } as Env)).toBe(true);
    expect(groupsEnabled({} as Env)).toBe(true);
  });
});

describe('GROUPS_ENABLED = "false"', () => {
  it("GET /groups/browse returns 200 with an empty list, even if listed groups exist", async () => {
    await putListedGroup(uniqueGroupId());
    const enabledJson = await (await browseGroups(KNOWN_WORLD_DATA_CENTER)).json<unknown[]>();
    expect(enabledJson.length).toBeGreaterThan(0);

    disableGroups();
    const response = await browseGroups(KNOWN_WORLD_DATA_CENTER);

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual([]);
  });

  it("PUT /group is rejected with 403 and a clear message, nothing is stored", async () => {
    disableGroups();
    const groupId = uniqueGroupId();

    const response = await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("Member") }],
      visibility: "listed",
    });

    expect(response.status).toBe(403);
    expect(await response.json()).toEqual({ error: GROUPS_DISABLED_MESSAGE });
    expect(await env.BLUNION_PROFILES.get(`group:${groupId}`)).toBeNull();
  });

  it("DELETE /group stays allowed so existing groups can be cleaned up", async () => {
    const groupId = uniqueGroupId();
    const editToken = await putListedGroup(groupId);

    disableGroups();
    const response = await deleteGroup(groupId, editToken);

    expect(response.status).toBe(200);
    expect(await env.BLUNION_PROFILES.get(`group:${groupId}`)).toBeNull();
  });

  it("admin DELETE /admin/group works independently of the switch", async () => {
    const groupId = uniqueGroupId();
    await putListedGroup(groupId);

    disableGroups();
    mutableEnv.ADMIN_TOKEN = ADMIN_TOKEN;
    const response = await callWorker(new Request(`http://example.com/admin/group/${groupId}`, {
      method: "DELETE",
      headers: { Authorization: `Bearer ${ADMIN_TOKEN}` },
    }));

    expect(response.status).toBe(200);
    expect(await env.BLUNION_PROFILES.get(`group:${groupId}`)).toBeNull();
  });

  it("the cron removes every group card from Discord but keeps the KV records", async () => {
    mutableEnv.DISCORD_WEBHOOK_NA = FAKE_WEBHOOK_URL_NA;
    const calls = recordDiscordCalls();

    const groupId = uniqueGroupId();
    await putListedGroup(groupId);
    const postCalls = calls.filter((call) => call.method === "POST");
    expect(postCalls).toHaveLength(1);
    const recordBefore = await env.BLUNION_PROFILES.get(`group:${groupId}`);
    expect(recordBefore).not.toBeNull();

    disableGroups();
    calls.length = 0;
    const ctx = createExecutionContext();
    await worker.scheduled(createScheduledController(), env, ctx);
    await waitOnExecutionContext(ctx);

    expect(calls.some((call) => call.method === "DELETE" && call.url.includes("/messages/"))).toBe(true);
    expect(await env.BLUNION_PROFILES.get(`group:${groupId}`)).toBe(recordBefore);
    const index = await env.BLUNION_PROFILES.get<{ groupId: string }[]>("discordcards:NA", "json");
    expect(index?.some((entry) => entry.groupId === groupId) ?? false).toBe(false);
  });
});

/** Gleiches TEST-Keypair wie in discord.test.ts (Public Key in worker/.dev.vars, siehe dort). */
const DISCORD_TEST_KEY_PAIR_JWK: JsonWebKey = {
  key_ops: ["sign"],
  ext: true,
  crv: "Ed25519",
  d: "oba0_FT40H6Klg1rljhnQFfBzxoTBfEUyxrJCdEBtXo",
  x: "bG9XWsvgtmJs1i0sDb3JF9IlWL2GWaWkJBVYZdlk0Hs",
  kty: "OKP",
};

async function discordBrowse(type?: string): Promise<{ data: { content?: string; embeds?: { title: string; fields: { name: string }[] }[] } }> {
  const options: { name: string; type: number; value: string }[] = [
    { name: "datacenter", type: 3, value: KNOWN_WORLD_DATA_CENTER },
  ];
  if (type !== undefined) options.push({ name: "type", type: 3, value: type });

  const bodyText = JSON.stringify({
    type: 2,
    data: { name: "blunion", options: [{ name: "browse", type: 1, options }] },
  });
  const timestamp = `${Math.floor(Date.now() / 1000)}`;
  const privateKey = await crypto.subtle.importKey("jwk", DISCORD_TEST_KEY_PAIR_JWK, { name: "Ed25519" }, false, ["sign"]);
  const signature = await crypto.subtle.sign({ name: "Ed25519" }, privateKey, new TextEncoder().encode(timestamp + bodyText));
  const hex = Array.from(new Uint8Array(signature)).map((b) => b.toString(16).padStart(2, "0")).join("");

  // Eigene origin: "/blunion browse" nutzt den 20-s-Browse-Cache (siehe uniqueBrowseOrigin).
  const response = await callWorker(new Request(`${uniqueBrowseOrigin("discord")}/discord/interactions`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Signature-Ed25519": hex, "X-Signature-Timestamp": timestamp },
    body: bodyText,
  }));
  return response.json();
}

describe('GROUPS_ENABLED = "false" - Discord "/blunion browse"', () => {
  it("shows players instead of groups when no type is given", async () => {
    await putListedGroup(uniqueGroupId());
    const characterName = uniqueName("DisabledPlayer");
    await putProfile(KNOWN_WORLD, characterName, { spellBitmaskBase64: validBitmaskBase64(), visibility: "listed" });

    disableGroups();
    const json = await discordBrowse();

    const embed = json.data.embeds![0]!;
    expect(embed.title).toContain("looking for groups");
    const fieldNames = embed.fields.map((field) => field.name);
    expect(fieldNames).toContain(`${characterName} (${KNOWN_WORLD})`);
    expect(fieldNames).not.toContain("Disabled-Testgruppe");
  });

  it('answers an explicit "type:groups" with a short hint instead of groups', async () => {
    await putListedGroup(uniqueGroupId());

    disableGroups();
    const json = await discordBrowse("groups");

    expect(json.data.embeds).toBeUndefined();
    expect(json.data.content).toContain("Groups are currently disabled");
  });
});
