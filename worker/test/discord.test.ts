import { describe, expect, it } from "vitest";
import { formatTargetSpellOrders } from "../src/index";
import {
  browseProfiles, callWorker, KNOWN_SPELL_IDS_SAMPLE, KNOWN_WORLD, KNOWN_WORLD_DATA_CENTER, putGroup, putProfile,
  uniqueName, validBitmaskBase64,
} from "./helpers";

/**
 * Tests für die neue, rein lesende Discord-Slash-Command-Integration Phase 1 (POST
 * /discord/interactions, siehe handleDiscordInteractions in src/index.ts und
 * DISCORD_INTEGRATION.md). Signiert Test-Requests mit einem eigens für DIESES Repo generierten
 * Ed25519-TEST-Keypair (siehe DISCORD_TEST_KEY_PAIR_JWK unten) - der zugehörige Public Key steht
 * in worker/.dev.vars als DISCORD_PUBLIC_KEY (siehe .dev.vars.example), automatisch geladen von
 * @cloudflare/vitest-plugin über dieselbe wrangler.toml wie "wrangler dev". Dieses Keypair
 * schützt NICHTS in Produktion - dort gilt ausschließlich der per "wrangler secret put
 * DISCORD_PUBLIC_KEY" gesetzte, echte Discord-Public-Key (siehe Env-Doc in src/index.ts).
 */

/** Privater Schlüssel zum in .dev.vars hinterlegten Public Key (siehe Datei-Doc oben) - als JWK,
 * weil crypto.subtle.importKey("raw", ...) für Ed25519-PRIVATE-Keys nicht unterstützt wird (nur
 * für Public Keys, siehe verifyDiscordSignature in src/index.ts, das genau deshalb "raw" nutzt). */
const DISCORD_TEST_KEY_PAIR_JWK: JsonWebKey = {
  key_ops: ["sign"],
  ext: true,
  crv: "Ed25519",
  d: "oba0_FT40H6Klg1rljhnQFfBzxoTBfEUyxrJCdEBtXo",
  x: "bG9XWsvgtmJs1i0sDb3JF9IlWL2GWaWkJBVYZdlk0Hs",
  kty: "OKP",
};

function bytesToHex(bytes: Uint8Array): string {
  return Array.from(bytes).map((b) => b.toString(16).padStart(2, "0")).join("");
}

/** Signiert einen Discord-Interaction-Request exakt wie ein echter Discord-Server (siehe
 * verifyDiscordSignature-Doc in src/index.ts: Nachricht = timestamp + rawBody, Ed25519 über
 * SubtleCrypto) - baut daraus direkt das fertige Request-Objekt inkl. beider Signatur-Header. */
async function signedDiscordRequest(bodyText: string, timestamp = `${Math.floor(Date.now() / 1000)}`) {
  const privateKey = await crypto.subtle.importKey(
    "jwk", DISCORD_TEST_KEY_PAIR_JWK, { name: "Ed25519" }, false, ["sign"]);
  const message = new TextEncoder().encode(timestamp + bodyText);
  const signature = await crypto.subtle.sign({ name: "Ed25519" }, privateKey, message);

  return new Request("http://example.com/discord/interactions", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Signature-Ed25519": bytesToHex(new Uint8Array(signature)),
      "X-Signature-Timestamp": timestamp,
    },
    body: bodyText,
  });
}

/** `type` bewusst optional (siehe handleDiscordApplicationCommand-Doc "Default groups") - Tests,
 * die keinen `type` übergeben, decken damit gleichzeitig das Verhalten bereits registrierter
 * Guild-Commands ohne diese (neuere) Option ab. */
function browseCommandInteraction(datacenter?: string, type?: string) {
  const options: { name: string; type: number; value: string }[] = [];
  if (datacenter !== undefined) options.push({ name: "datacenter", type: 3, value: datacenter });
  if (type !== undefined) options.push({ name: "type", type: 3, value: type });

  return {
    type: 2, // APPLICATION_COMMAND
    data: {
      name: "blunion",
      options: [
        {
          name: "browse",
          type: 1, // SUB_COMMAND
          options,
        },
      ],
    },
  };
}

describe("POST /discord/interactions - signature verification", () => {
  it("rejects a request without signature headers with 401", async () => {
    const response = await callWorker(new Request("http://example.com/discord/interactions", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ type: 1 }),
    }));

    expect(response.status).toBe(401);
  });

  it("rejects a request with a tampered signature with 401", async () => {
    const bodyText = JSON.stringify({ type: 1 });
    const request = await signedDiscordRequest(bodyText);
    const timestamp = request.headers.get("X-Signature-Timestamp")!;

    // Letztes Zeichen der gültigen Signatur flippen - simuliert einen Angreifer, der eine
    // falsche/erratene Signatur mitschickt.
    const validSignature = request.headers.get("X-Signature-Ed25519")!;
    const tamperedSignature = validSignature.replace(/.$/, (c) => (c === "0" ? "1" : "0"));

    const tamperedRequest = new Request("http://example.com/discord/interactions", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Signature-Ed25519": tamperedSignature,
        "X-Signature-Timestamp": timestamp,
      },
      body: bodyText,
    });

    const response = await callWorker(tamperedRequest);
    expect(response.status).toBe(401);
  });

  it("rejects a request with a valid signature over a DIFFERENT body with 401", async () => {
    const request = await signedDiscordRequest(JSON.stringify({ type: 1 }));
    const timestamp = request.headers.get("X-Signature-Timestamp")!;
    const signature = request.headers.get("X-Signature-Ed25519")!;

    // Gleiche (gültige) Signatur/Timestamp, aber ein anderer Body als der signierte - darf NICHT
    // akzeptiert werden (siehe verifyDiscordSignature: Nachricht ist timestamp+rawBody).
    const mismatchedRequest = new Request("http://example.com/discord/interactions", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Signature-Ed25519": signature,
        "X-Signature-Timestamp": timestamp,
      },
      body: JSON.stringify({ type: 2 }),
    });

    const response = await callWorker(mismatchedRequest);
    expect(response.status).toBe(401);
  });

  it("accepts a validly signed request", async () => {
    const request = await signedDiscordRequest(JSON.stringify({ type: 1 }));
    const response = await callWorker(request);

    expect(response.status).toBe(200);
  });
});

describe("POST /discord/interactions - PING", () => {
  it("responds to a PING interaction with a PONG", async () => {
    const request = await signedDiscordRequest(JSON.stringify({ type: 1 }));
    const response = await callWorker(request);

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ type: 1 });
  });
});

describe("POST /discord/interactions - /blunion browse", () => {
  it("returns a friendly chat message when no datacenter option was given", async () => {
    const request = await signedDiscordRequest(JSON.stringify(browseCommandInteraction()));
    const response = await callWorker(request);
    const json = await response.json<{ type: number; data: { content: string } }>();

    expect(response.status).toBe(200);
    expect(json.type).toBe(4); // CHANNEL_MESSAGE_WITH_SOURCE
    expect(json.data.content).toMatch(/Data Center/);
  });

  it("returns a friendly chat message (no empty embed) when no groups are listed", async () => {
    const request = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(uniqueName("KeinSolchesDC"))));
    const response = await callWorker(request);
    const json = await response.json<{ type: number; data: { content: string; embeds?: unknown[] } }>();

    expect(response.status).toBe(200);
    expect(json.data.embeds).toBeUndefined();
    expect(json.data.content).toMatch(/[Kk]eine.*Gruppen/);
  });

  it("returns an embed with the group's note/members/availability for a listed group, without editToken", async () => {
    const groupId = uniqueName("discord-group-");
    const memberName = uniqueName("DiscordMitglied");
    await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: memberName }],
      visibility: "listed",
      note: "Testgruppe für Discord",
      availabilityTags: ["evening", "weekend"],
      wantedPlayerCount: 4,
    });

    const request = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(KNOWN_WORLD_DATA_CENTER)));
    const response = await callWorker(request);
    const json = await response.json<{
      type: number;
      data: { embeds: { title: string; fields: { name: string; value: string }[] }[]; components: unknown[] };
    }>();

    expect(response.status).toBe(200);
    expect(json.data.embeds[0]!.title).toContain(KNOWN_WORLD_DATA_CENTER);

    const field = json.data.embeds[0]!.fields.find((f) => f.name === "Testgruppe für Discord");
    expect(field).toBeDefined();
    expect(field!.value).toContain(KNOWN_WORLD);
    expect(field!.value).toContain(memberName);
    expect(field!.value).toContain("evening");
    expect(field!.value).toContain("1/4");

    // Ohne editToken/interne Felder im Embed (siehe Aufgabenstellung).
    const embedJson = JSON.stringify(json.data.embeds);
    expect(embedJson).not.toMatch(/editToken/i);

    // Link-Button zur Companion-Website vorhanden (siehe COMPANION_WEBSITE_URL).
    expect(JSON.stringify(json.data.components)).toContain("letsi-ma.github.io");
  });

  it("shows targetSpellIds as sorted, language-independent order numbers (e.g. '#25, #26')", async () => {
    const groupId = uniqueName("discord-group-spells-");
    await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("SpellMitglied") }],
      visibility: "listed",
      note: "Ziel-Spell-Testgruppe",
      // absichtlich absteigend übergeben - das Embed muss trotzdem aufsteigend zeigen.
      targetSpellIds: [KNOWN_SPELL_IDS_SAMPLE[1], KNOWN_SPELL_IDS_SAMPLE[0]],
    });

    const request = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(KNOWN_WORLD_DATA_CENTER)));
    const response = await callWorker(request);
    const json = await response.json<{
      data: { embeds: { fields: { name: string; value: string }[] }[] };
    }>();

    const field = json.data.embeds[0]!.fields.find((f) => f.name === "Ziel-Spell-Testgruppe");
    expect(field).toBeDefined();
    // KNOWN_SPELL_IDS_SAMPLE = [11383, 11384], deren order-Werte sind 25 und 26 (siehe
    // src/spellOrder.ts) - aufsteigend sortiert also #25 vor #26.
    expect(field!.value).toContain("Ziel-Spells: #25, #26");
  });

  it("omits the 'Ziel-Spells' line entirely when the group has no targetSpellIds", async () => {
    const groupId = uniqueName("discord-group-nospells-");
    await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      visibility: "listed",
      note: "Ohne Ziel-Spells",
    });

    const request = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(KNOWN_WORLD_DATA_CENTER)));
    const response = await callWorker(request);
    const json = await response.json<{
      data: { embeds: { fields: { name: string; value: string }[] }[] };
    }>();

    const field = json.data.embeds[0]!.fields.find((f) => f.name === "Ohne Ziel-Spells");
    expect(field).toBeDefined();
    expect(field!.value).not.toContain("Ziel-Spells");
  });

  it("omits the 'Verfügbarkeit' line entirely (no '-' placeholder) when availabilityTags is empty", async () => {
    const groupId = uniqueName("discord-group-noavail-");
    await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      visibility: "listed",
      note: "Ohne Verfügbarkeit",
    });

    const request = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(KNOWN_WORLD_DATA_CENTER)));
    const response = await callWorker(request);
    const json = await response.json<{
      data: { embeds: { fields: { name: string; value: string }[] }[] };
    }>();

    const field = json.data.embeds[0]!.fields.find((f) => f.name === "Ohne Verfügbarkeit");
    expect(field).toBeDefined();
    expect(field!.value).not.toContain("Verfügbarkeit");
    expect(field!.value).not.toContain(" - ");
  });

  it("does not surface unlisted groups", async () => {
    const groupId = uniqueName("discord-unlisted-");
    await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      visibility: "unlisted",
    });

    const request = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(KNOWN_WORLD_DATA_CENTER)));
    const response = await callWorker(request);
    const json = await response.json<{ data: { embeds?: { fields: { name: string }[] }[] } }>();

    if (json.data.embeds) {
      const groupIds = JSON.stringify(json.data.embeds);
      expect(groupIds).not.toContain(groupId);
    }
  });
});

describe("POST /discord/interactions - /blunion browse type:players", () => {
  it("shows the players embed for type:players and the groups embed without a type option, on the same data center", async () => {
    // Ein Data Center mit sowohl einer gelisteten Gruppe ALS AUCH einem gelisteten Einzelprofil -
    // zeigt in einem Test direkt, dass die "type"-Option tatsächlich zwischen den beiden Embeds
    // umschaltet, statt nur zufällig das jeweils einzige vorhandene Ergebnis zu zeigen.
    const groupId = uniqueName("discord-switch-group-");
    await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      visibility: "listed",
      note: "Umschalt-Testgruppe",
    });

    const characterName = uniqueName("UmschaltSpieler");
    await putProfile(KNOWN_WORLD, characterName, {
      spellBitmaskBase64: validBitmaskBase64(),
      visibility: "listed",
      note: "Umschalt-Testspieler",
    });

    // "type:players" -> Spieler-Embed, KEIN Gruppen-Feld.
    const playersRequest = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(KNOWN_WORLD_DATA_CENTER, "players")));
    const playersJson = await callWorker(playersRequest).then((r) => r.json<{
      data: { embeds: { title: string; fields: { name: string }[] }[] };
    }>());
    const playerFieldNames = playersJson.data.embeds[0]!.fields.map((f) => f.name);
    expect(playerFieldNames).toContain(`${characterName} (${KNOWN_WORLD})`);
    expect(playerFieldNames).not.toContain("Umschalt-Testgruppe");
    expect(playersJson.data.embeds[0]!.title).toContain("Spieler");

    // KEINE "type"-Option -> weiterhin das bisherige Gruppen-Embed (Rückwärtskompatibilität für
    // bereits registrierte Guild-Commands ohne diese neuere Option, siehe
    // handleDiscordApplicationCommand-Doc).
    const groupsRequest = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(KNOWN_WORLD_DATA_CENTER)));
    const groupsJson = await callWorker(groupsRequest).then((r) => r.json<{
      data: { embeds: { title: string; fields: { name: string }[] }[] };
    }>());
    const groupFieldNames = groupsJson.data.embeds[0]!.fields.map((f) => f.name);
    expect(groupFieldNames).toContain("Umschalt-Testgruppe");
    expect(groupFieldNames).not.toContain(`${characterName} (${KNOWN_WORLD})`);
    expect(groupsJson.data.embeds[0]!.title).toContain("Gruppen");
  });

  it("falls back to groups for an unknown/unregistered type value", async () => {
    // Simuliert eine Interaction von einem Server, dessen Command-Registrierung noch die
    // choices-Liste einer künftigen Discord-Version kennt, die dieser Wrangler-Stand nicht
    // (siehe handleDiscordApplicationCommand-Doc) - darf NICHT abstürzen, sondern zeigt Gruppen.
    const groupId = uniqueName("discord-group-fallback-");
    await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      visibility: "listed",
      note: "Fallback-Testgruppe",
    });

    const request = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(KNOWN_WORLD_DATA_CENTER, "irgendwas-unbekanntes")));
    const response = await callWorker(request);
    const json = await response.json<{ data: { embeds: { fields: { name: string }[] }[] } }>();

    expect(json.data.embeds[0]!.fields.some((field) => field.name === "Fallback-Testgruppe")).toBe(true);
  });

  it("returns a friendly chat message when no datacenter option was given", async () => {
    const request = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(undefined, "players")));
    const response = await callWorker(request);
    const json = await response.json<{ type: number; data: { content: string } }>();

    expect(response.status).toBe(200);
    expect(json.type).toBe(4); // CHANNEL_MESSAGE_WITH_SOURCE
    expect(json.data.content).toMatch(/Data Center/);
  });

  it("returns a friendly chat message (no empty embed) when no players are listed", async () => {
    const request = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(uniqueName("KeinSolchesDC"), "players")));
    const response = await callWorker(request);
    const json = await response.json<{ type: number; data: { content: string; embeds?: unknown[] } }>();

    expect(response.status).toBe(200);
    expect(json.data.embeds).toBeUndefined();
    expect(json.data.content).toMatch(/[Kk]eine.*Spieler/);
  });

  it("returns an embed with the player's note/availability/wantedPlayerCount for a listed profile", async () => {
    const characterName = uniqueName("DiscordSpieler");
    await putProfile(KNOWN_WORLD, characterName, {
      spellBitmaskBase64: validBitmaskBase64(),
      visibility: "listed",
      note: "Testspieler für Discord",
      availabilityTags: ["evening", "weekend"],
      wantedPlayerCount: 4,
    });

    const request = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(KNOWN_WORLD_DATA_CENTER, "players")));
    const response = await callWorker(request);
    const json = await response.json<{
      type: number;
      data: { embeds: { title: string; fields: { name: string; value: string }[] }[]; components: unknown[] };
    }>();

    expect(response.status).toBe(200);
    expect(json.data.embeds[0]!.title).toContain(KNOWN_WORLD_DATA_CENTER);

    const field = json.data.embeds[0]!.fields.find((f) => f.name === `${characterName} (${KNOWN_WORLD})`);
    expect(field).toBeDefined();
    expect(field!.value).toContain("Testspieler für Discord");
    expect(field!.value).toContain("evening");
    expect(field!.value).toContain("Gesucht: 4 Mitspieler");

    // Ohne editToken/interne Felder im Embed (siehe Aufgabenstellung, gilt hier analog).
    const embedJson = JSON.stringify(json.data.embeds);
    expect(embedJson).not.toMatch(/editToken/i);

    // Link-Button zur Companion-Website vorhanden (siehe COMPANION_WEBSITE_URL).
    expect(JSON.stringify(json.data.components)).toContain("letsi-ma.github.io");
  });

  it("shows targetSpellIds as sorted, language-independent order numbers (e.g. '#25, #26')", async () => {
    const characterName = uniqueName("DiscordSpellSpieler");
    await putProfile(KNOWN_WORLD, characterName, {
      spellBitmaskBase64: validBitmaskBase64(),
      visibility: "listed",
      note: "Ziel-Spell-Testspieler",
      // absichtlich absteigend übergeben - das Embed muss trotzdem aufsteigend zeigen.
      targetSpellIds: [KNOWN_SPELL_IDS_SAMPLE[1], KNOWN_SPELL_IDS_SAMPLE[0]],
    });

    const request = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(KNOWN_WORLD_DATA_CENTER, "players")));
    const response = await callWorker(request);
    const json = await response.json<{
      data: { embeds: { fields: { name: string; value: string }[] }[] };
    }>();

    const field = json.data.embeds[0]!.fields.find((f) => f.name === `${characterName} (${KNOWN_WORLD})`);
    expect(field).toBeDefined();
    // KNOWN_SPELL_IDS_SAMPLE = [11383, 11384], deren order-Werte sind 25 und 26 (siehe
    // src/spellOrder.ts) - aufsteigend sortiert also #25 vor #26.
    expect(field!.value).toContain("Ziel-Spells: #25, #26");
  });

  it("omits the 'Ziel-Spells' line entirely when the profile has no targetSpellIds", async () => {
    const characterName = uniqueName("DiscordNoSpellSpieler");
    await putProfile(KNOWN_WORLD, characterName, {
      spellBitmaskBase64: validBitmaskBase64(),
      visibility: "listed",
      note: "Testnotiz ohne Auswahl",
    });

    const request = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(KNOWN_WORLD_DATA_CENTER, "players")));
    const response = await callWorker(request);
    const json = await response.json<{
      data: { embeds: { fields: { name: string; value: string }[] }[] };
    }>();

    const field = json.data.embeds[0]!.fields.find((f) => f.name === `${characterName} (${KNOWN_WORLD})`);
    expect(field).toBeDefined();
    expect(field!.value).not.toContain("Ziel-Spells");
  });

  it("does not surface unlisted profiles", async () => {
    const characterName = uniqueName("discord-unlisted-spieler-");
    await putProfile(KNOWN_WORLD, characterName, {
      spellBitmaskBase64: validBitmaskBase64(),
      visibility: "unlisted",
    });

    const request = await signedDiscordRequest(
      JSON.stringify(browseCommandInteraction(KNOWN_WORLD_DATA_CENTER, "players")));
    const response = await callWorker(request);
    const json = await response.json<{ data: { embeds?: { fields: { name: string }[] }[] } }>();

    if (json.data.embeds) {
      const names = JSON.stringify(json.data.embeds);
      expect(names).not.toContain(characterName);
    }
  });
});

describe("existing endpoints keep working unchanged alongside /discord/interactions", () => {
  it("GET /profiles/browse still requires a dataCenter query param", async () => {
    const response = await browseProfiles(null);
    expect(response.status).toBe(400);
  });
});

describe("formatTargetSpellOrders", () => {
  // Direkter Unit-Test statt über den HTTP-Endpunkt (siehe formatTargetSpellOrders-Doc in
  // src/index.ts): eine SPELL_ORDER_BY_ID unbekannte, aber KNOWN_SPELL_IDS bekannte ID lässt sich
  // aktuell gar nicht künstlich herstellen (beide Tabellen sind deckungsgleich) - eine wirklich
  // unbekannte ID würde bereits isValidTargetSpellIds/handleGroupPut ablehnen.
  it("skips an ID unknown to SPELL_ORDER_BY_ID instead of throwing", () => {
    expect(formatTargetSpellOrders([KNOWN_SPELL_IDS_SAMPLE[0]!, 999999999])).toBe("#25");
  });

  it("returns undefined for an empty list", () => {
    expect(formatTargetSpellOrders([])).toBeUndefined();
  });

  it("returns undefined when every ID is unknown", () => {
    expect(formatTargetSpellOrders([999999999])).toBeUndefined();
  });

  it("sorts ascending by order regardless of input order", () => {
    expect(formatTargetSpellOrders([KNOWN_SPELL_IDS_SAMPLE[1]!, KNOWN_SPELL_IDS_SAMPLE[0]!])).toBe("#25, #26");
  });
});
