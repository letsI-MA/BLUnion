import { env } from "cloudflare:workers";
import { afterEach, describe, expect, it } from "vitest";
import { buildOnlinePlayersEmbed, countLearnedSpells, PRESENCE_ONLINE_WINDOW_MS } from "../src/index";
import { base64UrlEncode } from "../src/crypto";
import { KNOWN_SPELL_IDS } from "../src/spellIds";
import { callWorker, KNOWN_WORLD, KNOWN_WORLD_DATA_CENTER, putProfile, uniqueName, validBitmaskBase64 } from "./helpers";

/**
 * "/blunion online" (siehe handleDiscordOnline in src/index.ts): gelistete Spieler mit
 * sharePresence, die laut Presence gerade online sind - ephemeral, gleiche Embed-Limits wie browse.
 * Signiert mit demselben Ed25519-TEST-Keypair wie discord.test.ts (Public Key in .dev.vars).
 */

const DISCORD_TEST_KEY_PAIR_JWK: JsonWebKey = {
  key_ops: ["sign"],
  ext: true,
  crv: "Ed25519",
  d: "oba0_FT40H6Klg1rljhnQFfBzxoTBfEUyxrJCdEBtXo",
  x: "bG9XWsvgtmJs1i0sDb3JF9IlWL2GWaWkJBVYZdlk0Hs",
  kty: "OKP",
};

const EPHEMERAL = 64;
const mutableEnv = env as unknown as Record<string, string | undefined>;

afterEach(() => {
  mutableEnv.PRESENCE_ENABLED = "true";
});

interface OnlineResponse {
  type: number;
  data: {
    content?: string;
    flags?: number;
    allowed_mentions?: { parse: string[] };
    embeds?: { title: string; description?: string; fields: { name: string; value: string }[]; footer?: { text: string } }[];
  };
}

function bytesToHex(bytes: Uint8Array): string {
  return Array.from(bytes).map((b) => b.toString(16).padStart(2, "0")).join("");
}

/** Eigene origin pro Aufruf - der Online-Befehl nutzt den 20-s-Browse-Cache (Key aus origin +
 * kanonischem Data Center), siehe uniqueBrowseOrigin in helpers.ts. */
async function runOnline(datacenter: string | undefined, userId = `user-${Math.random()}`): Promise<OnlineResponse> {
  const body = JSON.stringify({
    type: 2,
    member: { user: { id: userId } },
    data: {
      name: "blunion",
      options: [{
        name: "online",
        type: 1,
        options: datacenter === undefined ? [] : [{ name: "datacenter", type: 3, value: datacenter }],
      }],
    },
  });

  const timestamp = `${Math.floor(Date.now() / 1000)}`;
  const privateKey = await crypto.subtle.importKey("jwk", DISCORD_TEST_KEY_PAIR_JWK, { name: "Ed25519" }, false, ["sign"]);
  const signature = await crypto.subtle.sign({ name: "Ed25519" }, privateKey, new TextEncoder().encode(timestamp + body));
  const origin = `http://online-${Date.now()}-${Math.random().toString(36).slice(2)}.example.com`;

  const response = await callWorker(new Request(`${origin}/discord/interactions`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Signature-Ed25519": bytesToHex(new Uint8Array(signature)),
      "X-Signature-Timestamp": timestamp,
    },
    body,
  }));
  expect(response.status).toBe(200);
  return response.json<OnlineResponse>();
}

/** Bitmaske mit den ersten `learned` Bits gesetzt. */
function bitmaskWithLearned(learned: number): string {
  const bytes = new Uint8Array(16);
  for (let index = 0; index < learned; index++)
    bytes[index >> 3]! |= 1 << (index % 8);
  return base64UrlEncode(bytes);
}

async function createPlayer(options: {
  sharePresence?: boolean; visibility?: string; note?: string; bitmask?: string; online?: boolean;
}): Promise<string> {
  const name = uniqueName("Online");
  const body: Record<string, unknown> = {
    spellBitmaskBase64: options.bitmask ?? validBitmaskBase64(),
    visibility: options.visibility ?? "listed",
    sharePresence: options.sharePresence ?? true,
  };
  if (options.note !== undefined) body.note = options.note;

  const response = await putProfile(KNOWN_WORLD, name, body);
  expect(response.status).toBe(201);

  // Presence direkt in D1 - unabhängig von der Zustimmung (POST /presence würde ohne sie 403 liefern).
  if (options.online !== false) {
    await env.PRESENCE
      .prepare("INSERT OR REPLACE INTO presence (world, name_lower, data_center, online, last_seen) VALUES (?, ?, ?, 1, ?)")
      .bind(KNOWN_WORLD.toLowerCase(), name.toLowerCase(), KNOWN_WORLD_DATA_CENTER.toLowerCase(), Date.now())
      .run();
  }

  return name;
}

async function setLastSeen(name: string, lastSeenMs: number): Promise<void> {
  await env.PRESENCE
    .prepare("UPDATE presence SET last_seen = ? WHERE world = ? AND name_lower = ?")
    .bind(lastSeenMs, KNOWN_WORLD.toLowerCase(), name.toLowerCase())
    .run();
}

function fieldNames(result: OnlineResponse): string[] {
  return result.data.embeds?.[0]?.fields.map((field) => field.name) ?? [];
}

describe("POST /discord/interactions - /blunion online", () => {
  it("lists an online player with Name@World, learned spell count and note - ephemeral, no mentions", async () => {
    const name = await createPlayer({ note: "LF Carnivale", bitmask: bitmaskWithLearned(2) });

    const result = await runOnline("aether");

    expect(result.type).toBe(4);
    expect(result.data.flags).toBe(EPHEMERAL);
    expect(result.data.allowed_mentions).toEqual({ parse: [] });
    const embed = result.data.embeds![0]!;
    expect(embed.title).toBe("Blue Mages online on Aether");
    const field = embed.fields.find((candidate) => candidate.name === `${name}@${KNOWN_WORLD}`);
    expect(field?.value).toBe(`Spells: 2/${KNOWN_SPELL_IDS.size}\nLF Carnivale`);
  });

  it("answers with a short message when nobody is online, in the canonical data center spelling", async () => {
    const result = await runOnline("lIgHt");

    expect(result.data.content).toBe("Nobody is online on Light right now.");
    expect(result.data.embeds).toBeUndefined();
    expect(result.data.flags).toBe(EPHEMERAL);
    expect(result.data.allowed_mentions).toEqual({ parse: [] });
  });

  it("omits players without sharePresence, unlisted players, offline players and stale signals (> 75 min)", async () => {
    const online = await createPlayer({});
    const noConsent = await createPlayer({ sharePresence: false });
    const unlisted = await createPlayer({ visibility: "unlisted" });
    const withoutSignal = await createPlayer({ online: false });
    const stale = await createPlayer({});
    await setLastSeen(stale, Date.now() - PRESENCE_ONLINE_WINDOW_MS - 60_000);

    const names = fieldNames(await runOnline(KNOWN_WORLD_DATA_CENTER));

    expect(names).toContain(`${online}@${KNOWN_WORLD}`);
    for (const hidden of [noConsent, unlisted, withoutSignal, stale])
      expect(names.some((fieldName) => fieldName.includes(hidden))).toBe(false);
  });

  it("still shows a signal just inside the 75-minute window", async () => {
    const name = await createPlayer({});
    await setLastSeen(name, Date.now() - PRESENCE_ONLINE_WINDOW_MS + 60_000);

    expect(fieldNames(await runOnline(KNOWN_WORLD_DATA_CENTER))).toContain(`${name}@${KNOWN_WORLD}`);
  });

  it("rejects an unknown data center with an ephemeral message", async () => {
    const result = await runOnline("KeinSolchesDC");

    expect(result.data.content).toBe('Unknown data center - e.g. "/blunion online datacenter:Aether".');
    expect(result.data.flags).toBe(EPHEMERAL);
  });

  it("asks for a data center when the option is missing", async () => {
    const result = await runOnline(undefined);

    expect(result.data.content).toContain("Please specify a data center");
    expect(result.data.flags).toBe(EPHEMERAL);
  });

  it('answers "disabled" when PRESENCE_ENABLED = "false"', async () => {
    await createPlayer({});
    mutableEnv.PRESENCE_ENABLED = "false";

    const result = await runOnline(KNOWN_WORLD_DATA_CENTER);

    expect(result.data.content).toBe("Online status is currently disabled.");
    expect(result.data.embeds).toBeUndefined();
    expect(result.data.flags).toBe(EPHEMERAL);
  });

  it("sanitizes the note (markdown, links, @everyone)", async () => {
    const name = await createPlayer({ note: "**join** https://evil.example @everyone [x](y)" });

    const field = (await runOnline(KNOWN_WORLD_DATA_CENTER)).data.embeds![0]!.fields
      .find((candidate) => candidate.name === `${name}@${KNOWN_WORLD}`)!;

    expect(field.value).toContain("\\*\\*join\\*\\*");
    expect(field.value).not.toContain("https://");
    expect(field.value).not.toContain("evil.example");
    expect(field.value).toContain("@​everyone");
    expect(field.value).toContain("\\[x\\]\\(y\\)");
  });

  it("rate-limits per Discord user with the read limiter", async () => {
    const userId = `limited-${Math.random()}`;
    const contents: (string | undefined)[] = [];
    for (let i = 0; i < 121; i++) // 2 * limit + 1, siehe security.test.ts (Fenstergrenzen)
      contents.push((await runOnline("KeinSolchesDC", userId)).data.content);

    expect(contents).toContain("Too many requests - please wait a moment and try again.");
  });
});

describe("buildOnlinePlayersEmbed / countLearnedSpells", () => {
  const player = (index: number, note: string) => ({
    characterName: `Player ${index}`, world: KNOWN_WORLD, spellBitmaskBase64: validBitmaskBase64(), availabilityTags: [],
    note, wantedPlayerCount: 0, targetSpellIds: [], updatedAt: new Date().toISOString(), online: true,
  });

  it("keeps the browse limits: max 25 fields, 6000 characters in total, '+N more - see website'", () => {
    const players = Array.from({ length: 40 }, (_, index) => player(index, "x".repeat(400)));

    const embed = buildOnlinePlayersEmbed("Aether", players) as {
      title: string; description?: string; fields: { name: string; value: string }[]; footer?: { text: string };
    };

    const total = embed.title.length + (embed.description?.length ?? 0) + (embed.footer?.text.length ?? 0)
      + embed.fields.reduce((sum, field) => sum + field.name.length + field.value.length, 0);
    expect(embed.fields.length).toBeLessThanOrEqual(25);
    expect(total).toBeLessThanOrEqual(6000);
    expect(embed.footer?.text).toBe(`+${40 - embed.fields.length} more - see website`);
  });

  it("omits the note line when the note is empty after sanitizing", () => {
    const embed = buildOnlinePlayersEmbed("Aether", [player(1, "https://only-a-link.example")]) as {
      fields: { value: string }[];
    };

    expect(embed.fields[0]!.value).toBe(`Spells: 0/${KNOWN_SPELL_IDS.size}`);
  });

  it("counts only the bits of known spells", () => {
    const allBits = base64UrlEncode(new Uint8Array(16).fill(0xff));

    expect(countLearnedSpells(bitmaskWithLearned(5))).toBe(5);
    expect(countLearnedSpells(allBits)).toBe(KNOWN_SPELL_IDS.size);
    expect(countLearnedSpells("nicht base64!")).toBeUndefined();
  });
});
