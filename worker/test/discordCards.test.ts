import { env } from "cloudflare:workers";
import { createExecutionContext, createScheduledController, waitOnExecutionContext } from "cloudflare:test";
import { afterEach, describe, expect, it } from "vitest";
import worker from "../src/index";
import { regionForDataCenter } from "../src/index";
import { setDiscordFetchForTests } from "../src/discordWebhook";
import { deleteGroup, KNOWN_SPELL_IDS_SAMPLE, putGroup, uniqueName } from "./helpers";

/**
 * Tests für die persistenten Discord-Gruppen-Karten (Phase 1.5, siehe DISCORD_INTEGRATION.md und
 * syncGroupDiscordCard/cleanupOrphanedDiscordCards in src/index.ts).
 *
 * Discord-Webhook-Aufrufe werden NIE echt ausgeführt (siehe setDiscordFetchForTests in
 * discordWebhook.ts - ein Versuch, stattdessen einen lokalen Test-HTTP-Server zu nutzen, scheitert
 * innerhalb der workerd-Testlaufzeit an "Network connection lost", ganz abgesehen davon, dass ein
 * echter Netzwerkzugriff in automatisierten Tests ohnehin unerwünscht wäre) - jeder Test, der
 * Kartenverhalten prüft, installiert stattdessen eine In-Memory-Fake-fetch-Implementierung, die
 * Requests aufzeichnet und kontrollierte Antworten zurückgibt, und setzt sowohl diese als auch die
 * per env.DISCORD_WEBHOOK_*-Mutation gesetzten Test-Webhook-URLs in afterEach wieder zurück, um
 * andere Tests (in dieser wie in anderen Dateien) nicht zu beeinflussen.
 */

const FAKE_WEBHOOK_URLS: Record<"NA" | "EU" | "JP" | "OC", string> = {
  NA: "https://discord.com/api/webhooks/100000000000000001/test-token-na",
  EU: "https://discord.com/api/webhooks/100000000000000002/test-token-eu",
  JP: "https://discord.com/api/webhooks/100000000000000003/test-token-jp",
  OC: "https://discord.com/api/webhooks/100000000000000004/test-token-oc",
};

/** Je eine bekannte World pro Region (siehe worlds.ts) - für Tests, die die Region-abhängige
 * Webhook-Auswahl end-to-end über den öffentlichen /group/:groupId-PUT-Pfad prüfen wollen. */
const WORLD_BY_REGION: Record<"NA" | "EU" | "JP" | "OC", string> = {
  NA: "Gilgamesh", // Aether
  EU: "Cerberus", // Chaos
  JP: "Aegis", // Elemental
  OC: "Bismarck", // Materia
};

interface RecordedWebhookCall {
  method: string;
  url: string;
  body: { embeds?: { title?: string; fields?: { name: string; value: string }[] }[] } | undefined;
}

/** Baut eine Fake-fetch-Implementierung, die JEDEN Aufruf aufzeichnet (Methode/URL/Body) und -
 * sofern nicht "fail" gesetzt ist - so antwortet, wie es ein echter Discord-Webhook laut Doku tun
 * würde (POST -> 200 mit {id}, PATCH -> 200, DELETE -> 204). Mit "fail: true" simuliert sie
 * stattdessen einen durchgängigen Discord-Ausfall (HTTP 500) - für die "bricht den Gruppen-PUT/
 * -DELETE nicht ab"-Tests weiter unten. */
function createRecordingDiscordFetch(options: { fail?: boolean } = {}) {
  const calls: RecordedWebhookCall[] = [];
  let nextMessageId = 1;

  const fetchImpl = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = input instanceof Request ? input.url : input.toString();
    const method = init?.method ?? "GET";
    const body = typeof init?.body === "string" ? JSON.parse(init.body) : undefined;
    calls.push({ method, url, body });

    if (options.fail)
      return new Response("simulated Discord outage", { status: 500 });

    if (method === "POST")
      return new Response(JSON.stringify({ id: `fake-message-${nextMessageId++}` }), { status: 200 });
    if (method === "PATCH")
      return new Response(JSON.stringify({ ok: true }), { status: 200 });
    if (method === "DELETE")
      return new Response(null, { status: 204 });

    throw new Error(`unexpected method in fake Discord fetch: ${method}`);
  }) as typeof fetch;

  return { fetchImpl, calls };
}

/** Setzt genau die übergebenen Regionen-Webhooks (Rest bleibt/wird undefined) - siehe afterEach
 * unten fürs Zurücksetzen. Direkte Mutation von env.DISCORD_WEBHOOK_* statt eines Eintrags in
 * .dev.vars: die vier Secrets sind absichtlich für den GESAMTEN restlichen Testlauf (siehe
 * index.test.ts/discord.test.ts) unkonfiguriert (siehe .dev.vars.example-Doku "optional") - würde
 * man sie global setzen, würde JEDE bestehende "listed"-Gruppen-PUT/DELETE-Testfallzeile in
 * index.test.ts plötzlich (unnötig) einen Kartenaufbau im Hintergrund anstoßen. */
function setRegionWebhooks(regions: Partial<Record<"NA" | "EU" | "JP" | "OC", string>>): void {
  const mutableEnv = env as unknown as Record<string, string | undefined>;
  mutableEnv.DISCORD_WEBHOOK_NA = regions.NA;
  mutableEnv.DISCORD_WEBHOOK_EU = regions.EU;
  mutableEnv.DISCORD_WEBHOOK_JP = regions.JP;
  mutableEnv.DISCORD_WEBHOOK_OC = regions.OC;
}

afterEach(() => {
  setRegionWebhooks({});
  setDiscordFetchForTests(fetch);
});

describe("regionForDataCenter", () => {
  // Deckt alle 11 in DATA_CENTER_TO_REGION hinterlegten Data Center ab (siehe dortige Doku, warum
  // es bewusst 11 statt 12 sind - "Shadow"/EU war ein 2024 wieder geschlossenes, temporäres Data
  // Center und wird von lookupDataCenter/worlds.ts nie zurückgegeben) - direkt gegen die
  // exportierte Funktion getestet (siehe crypto.test.ts für dasselbe Muster bei anderen reinen
  // Helfern).
  it.each([
    ["Aether", "NA"], ["Crystal", "NA"], ["Dynamis", "NA"], ["Primal", "NA"],
    ["Chaos", "EU"], ["Light", "EU"],
    ["Elemental", "JP"], ["Gaia", "JP"], ["Mana", "JP"], ["Meteor", "JP"],
    ["Materia", "OC"],
  ] as const)("maps data center %s to region %s", (dataCenter, expectedRegion) => {
    expect(regionForDataCenter(dataCenter)).toBe(expectedRegion);
  });

  it("is case-insensitive, like lookupDataCenter", () => {
    expect(regionForDataCenter("aether")).toBe("NA");
    expect(regionForDataCenter("CHAOS")).toBe("EU");
  });

  it("returns null for an unknown data center", () => {
    expect(regionForDataCenter("NotARealDataCenter")).toBeNull();
  });

  it("returns null for the retired temporary 'Shadow' data center", () => {
    // Shadow war ein 2024 eingeführtes, im selben Jahr wieder geschlossenes temporäres
    // Überlauf-Data-Center (siehe DATA_CENTER_TO_REGION-Doc in src/index.ts) - absichtlich NICHT
    // in DATA_CENTER_TO_REGION hinterlegt.
    expect(regionForDataCenter("Shadow")).toBeNull();
  });
});

describe("persistent Discord group cards - creation and updates", () => {
  it("creates a card in the correct region's webhook when a group is published as listed", async () => {
    setRegionWebhooks({ NA: FAKE_WEBHOOK_URLS.NA });
    const { fetchImpl, calls } = createRecordingDiscordFetch();
    setDiscordFetchForTests(fetchImpl);

    const groupId = uniqueName("discordcard-create-");
    const memberName = uniqueName("KartenMitglied");
    const response = await putGroup(groupId, {
      members: [{ world: WORLD_BY_REGION.NA, characterName: memberName }],
      visibility: "listed",
      note: "Karten-Testgruppe",
      availabilityTags: ["evening"],
      wantedPlayerCount: 3,
    });

    // Die Antwort an den Aufrufer (Plugin/Website) bleibt unverändert vom Kartenaufbau unberührt.
    expect(response.status).toBe(201);

    const postCalls = calls.filter((call) => call.method === "POST");
    expect(postCalls).toHaveLength(1);
    expect(postCalls[0]!.url).toBe(`${FAKE_WEBHOOK_URLS.NA}?wait=true`);

    const embed = postCalls[0]!.body!.embeds![0]!;
    expect(embed.title).toContain("Aether"); // konkretes Data Center von WORLD_BY_REGION.NA ("Gilgamesh")
    const field = embed.fields![0]!;
    expect(field.name).toBe("Karten-Testgruppe");
    expect(field.value).toContain(WORLD_BY_REGION.NA);
    expect(field.value).toContain(memberName);
    expect(field.value).toContain("evening");
    expect(field.value).toContain("1/3");
  });

  it("shows targetSpellIds as sorted order numbers on the card, and omits the line when empty", async () => {
    setRegionWebhooks({ NA: FAKE_WEBHOOK_URLS.NA });
    const { fetchImpl, calls } = createRecordingDiscordFetch();
    setDiscordFetchForTests(fetchImpl);

    await putGroup(uniqueName("discordcard-spells-"), {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      visibility: "listed",
      note: "Karte mit Ziel-Spells",
      targetSpellIds: [KNOWN_SPELL_IDS_SAMPLE[1], KNOWN_SPELL_IDS_SAMPLE[0]],
    });

    const postCalls = calls.filter((call) => call.method === "POST");
    const field = postCalls[0]!.body!.embeds![0]!.fields![0]!;
    expect(field.value).toContain("Ziel-Spells: #25, #26");

    await putGroup(uniqueName("discordcard-nospells-"), {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      visibility: "listed",
      note: "Karte ohne Ziel-Spells",
    });

    const secondField = calls.filter((call) => call.method === "POST")[1]!.body!.embeds![0]!.fields![0]!;
    expect(secondField.value).not.toContain("Ziel-Spells");
  });

  it.each(["NA", "EU", "JP", "OC"] as const)(
    "picks the %s webhook for a group on a %s data center",
    async (region) => {
      setRegionWebhooks({ [region]: FAKE_WEBHOOK_URLS[region] });
      const { fetchImpl, calls } = createRecordingDiscordFetch();
      setDiscordFetchForTests(fetchImpl);

      await putGroup(uniqueName(`discordcard-${region}-`), {
        members: [{ world: WORLD_BY_REGION[region], characterName: uniqueName("M") }],
        visibility: "listed",
      });

      const postCalls = calls.filter((call) => call.method === "POST");
      expect(postCalls).toHaveLength(1);
      expect(postCalls[0]!.url).toBe(`${FAKE_WEBHOOK_URLS[region]}?wait=true`);
    },
  );

  it("edits the existing card instead of creating a duplicate when the group is updated", async () => {
    setRegionWebhooks({ NA: FAKE_WEBHOOK_URLS.NA });
    const { fetchImpl, calls } = createRecordingDiscordFetch();
    setDiscordFetchForTests(fetchImpl);

    const groupId = uniqueName("discordcard-edit-");
    const createResponse = await putGroup(groupId, {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      visibility: "listed",
      note: "Vorher",
    });
    const { editToken } = await createResponse.json<{ editToken: string }>();

    const updateResponse = await putGroup(groupId, {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      editToken,
      visibility: "listed",
      note: "Nachher",
    });
    expect(updateResponse.status).toBe(200);

    expect(calls.filter((call) => call.method === "POST")).toHaveLength(1); // keine zweite Karte
    const patchCalls = calls.filter((call) => call.method === "PATCH");
    expect(patchCalls).toHaveLength(1);
    expect(patchCalls[0]!.url).toBe(`${FAKE_WEBHOOK_URLS.NA}/messages/fake-message-1`);
    expect(patchCalls[0]!.body!.embeds![0]!.fields![0]!.name).toBe("Nachher");
  });

  it("moves the card to the new region's webhook and removes the old one when the group's data center changes", async () => {
    setRegionWebhooks({ NA: FAKE_WEBHOOK_URLS.NA, EU: FAKE_WEBHOOK_URLS.EU });
    const { fetchImpl, calls } = createRecordingDiscordFetch();
    setDiscordFetchForTests(fetchImpl);

    const groupId = uniqueName("discordcard-move-");
    const createResponse = await putGroup(groupId, {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      visibility: "listed",
    });
    const { editToken } = await createResponse.json<{ editToken: string }>();

    await putGroup(groupId, {
      members: [{ world: WORLD_BY_REGION.EU, characterName: uniqueName("M") }],
      editToken,
      visibility: "listed",
    });

    const postCalls = calls.filter((call) => call.method === "POST");
    const deleteCalls = calls.filter((call) => call.method === "DELETE");
    expect(postCalls).toHaveLength(2); // eine NA-, eine EU-Karte
    expect(postCalls[0]!.url).toBe(`${FAKE_WEBHOOK_URLS.NA}?wait=true`);
    expect(postCalls[1]!.url).toBe(`${FAKE_WEBHOOK_URLS.EU}?wait=true`);
    expect(deleteCalls).toHaveLength(1); // die alte NA-Karte wird entfernt, nicht editiert
    expect(deleteCalls[0]!.url).toBe(`${FAKE_WEBHOOK_URLS.NA}/messages/fake-message-1`);
  });
});

describe("persistent Discord group cards - removal", () => {
  it("removes the card when a group is set back to unlisted", async () => {
    setRegionWebhooks({ NA: FAKE_WEBHOOK_URLS.NA });
    const { fetchImpl, calls } = createRecordingDiscordFetch();
    setDiscordFetchForTests(fetchImpl);

    const groupId = uniqueName("discordcard-unlist-");
    const createResponse = await putGroup(groupId, {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      visibility: "listed",
    });
    const { editToken } = await createResponse.json<{ editToken: string }>();

    const unlistResponse = await putGroup(groupId, {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      editToken,
      visibility: "unlisted",
    });
    expect(unlistResponse.status).toBe(200);

    const deleteCalls = calls.filter((call) => call.method === "DELETE");
    expect(deleteCalls).toHaveLength(1);
    expect(deleteCalls[0]!.url).toBe(`${FAKE_WEBHOOK_URLS.NA}/messages/fake-message-1`);

    // Ein erneutes Zurückschalten auf "listed" legt eine NEUE Karte an, statt die alte (gelöschte)
    // wiederzubeleben.
    await putGroup(groupId, {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      editToken,
      visibility: "listed",
    });
    expect(calls.filter((call) => call.method === "POST")).toHaveLength(2);
  });

  it("removes the card when a group is deleted", async () => {
    setRegionWebhooks({ NA: FAKE_WEBHOOK_URLS.NA });
    const { fetchImpl, calls } = createRecordingDiscordFetch();
    setDiscordFetchForTests(fetchImpl);

    const groupId = uniqueName("discordcard-delete-");
    const createResponse = await putGroup(groupId, {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      visibility: "listed",
    });
    const { editToken } = await createResponse.json<{ editToken: string }>();

    const deleteResponse = await deleteGroup(groupId, editToken);
    expect(deleteResponse.status).toBe(200);
    expect(await deleteResponse.json()).toEqual({ deleted: true });

    const deleteCalls = calls.filter((call) => call.method === "DELETE");
    expect(deleteCalls).toHaveLength(1);
    expect(deleteCalls[0]!.url).toBe(`${FAKE_WEBHOOK_URLS.NA}/messages/fake-message-1`);
  });

  it("does not attempt to delete anything for a group that never had a card", async () => {
    // Kein Webhook für die Region konfiguriert -> nie eine Karte angelegt (siehe eigene
    // describe-Gruppe unten) - DELETE muss trotzdem klaglos funktionieren.
    const { fetchImpl, calls } = createRecordingDiscordFetch();
    setDiscordFetchForTests(fetchImpl);

    const groupId = uniqueName("discordcard-nocard-");
    const createResponse = await putGroup(groupId, {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      visibility: "listed",
    });
    const { editToken } = await createResponse.json<{ editToken: string }>();

    const deleteResponse = await deleteGroup(groupId, editToken);
    expect(deleteResponse.status).toBe(200);
    expect(calls).toHaveLength(0);
  });
});

describe("persistent Discord group cards - Discord failures never break the group endpoint", () => {
  it("still publishes the group successfully when Discord's webhook API is down", async () => {
    setRegionWebhooks({ NA: FAKE_WEBHOOK_URLS.NA });
    const { fetchImpl } = createRecordingDiscordFetch({ fail: true });
    setDiscordFetchForTests(fetchImpl);

    const response = await putGroup(uniqueName("discordcard-outage-put-"), {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      visibility: "listed",
    });

    expect(response.status).toBe(201);
    const json = await response.json<{ editToken?: string }>();
    expect(json.editToken).toBeTruthy();
  });

  it("still deletes the group successfully when Discord's webhook API is down", async () => {
    setRegionWebhooks({ NA: FAKE_WEBHOOK_URLS.NA });
    const { fetchImpl: workingFetch } = createRecordingDiscordFetch();
    setDiscordFetchForTests(workingFetch);

    const groupId = uniqueName("discordcard-outage-delete-");
    const createResponse = await putGroup(groupId, {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      visibility: "listed",
    });
    const { editToken } = await createResponse.json<{ editToken: string }>();

    // Discord fällt jetzt aus, NACHDEM die Karte schon existiert - der DELETE-Aufruf für die
    // Nachricht selbst schlägt fehl, das eigentliche Gruppen-DELETE darf das nicht merken.
    const { fetchImpl: failingFetch } = createRecordingDiscordFetch({ fail: true });
    setDiscordFetchForTests(failingFetch);

    const deleteResponse = await deleteGroup(groupId, editToken);
    expect(deleteResponse.status).toBe(200);
    expect(await deleteResponse.json()).toEqual({ deleted: true });
  });
});

describe("persistent Discord group cards - no region configured", () => {
  it("does not attempt any Discord call when no webhook is configured for the group's region", async () => {
    // Absichtlich KEIN setRegionWebhooks() - alle vier bleiben unconfigured (siehe afterEach).
    const { fetchImpl, calls } = createRecordingDiscordFetch();
    setDiscordFetchForTests(fetchImpl);

    const response = await putGroup(uniqueName("discordcard-noregion-"), {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      visibility: "listed",
    });

    expect(response.status).toBe(201);
    expect(calls).toHaveLength(0);
  });
});

describe("cron cleanup of orphaned Discord cards", () => {
  async function callScheduled(): Promise<void> {
    const ctx = createExecutionContext();
    const controller = createScheduledController();
    await worker.scheduled(controller, env, ctx);
    await waitOnExecutionContext(ctx);
  }

  it("removes the card of a group whose KV entry silently expired (no prior DELETE)", async () => {
    setRegionWebhooks({ NA: FAKE_WEBHOOK_URLS.NA });
    const { fetchImpl, calls } = createRecordingDiscordFetch();
    setDiscordFetchForTests(fetchImpl);

    const groupId = uniqueName("discordcard-orphan-");
    await putGroup(groupId, {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      visibility: "listed",
    });
    expect(calls.filter((call) => call.method === "POST")).toHaveLength(1);

    // Simuliert den stillen TTL-Ablauf (siehe Klassendoc am Dateianfang von src/index.ts): der
    // "group:<groupId>"-Eintrag verschwindet, OHNE dass DELETE /group/:groupId aufgerufen wird
    // (das würde die Karte ja bereits selbst aufräumen) - der "discordcards:NA"-Index bleibt dabei
    // unverändert bestehen, siehe DiscordCardIndexEntry-Doc in src/index.ts.
    await env.BLUNION_PROFILES.delete(`group:${groupId}`);

    await callScheduled();

    const deleteCalls = calls.filter((call) => call.method === "DELETE");
    expect(deleteCalls).toHaveLength(1);
    expect(deleteCalls[0]!.url).toBe(`${FAKE_WEBHOOK_URLS.NA}/messages/fake-message-1`);
  });

  it("does not touch cards of groups that still exist", async () => {
    setRegionWebhooks({ NA: FAKE_WEBHOOK_URLS.NA });
    const { fetchImpl, calls } = createRecordingDiscordFetch();
    setDiscordFetchForTests(fetchImpl);

    await putGroup(uniqueName("discordcard-still-alive-"), {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      visibility: "listed",
    });
    calls.length = 0; // nur die Cron-Aufrufe unten interessieren.

    await callScheduled();

    expect(calls).toHaveLength(0);
  });

  it("does not attempt to remove the same orphaned card twice on a second run", async () => {
    setRegionWebhooks({ NA: FAKE_WEBHOOK_URLS.NA });
    const { fetchImpl, calls } = createRecordingDiscordFetch();
    setDiscordFetchForTests(fetchImpl);

    const groupId = uniqueName("discordcard-orphan-twice-");
    await putGroup(groupId, {
      members: [{ world: WORLD_BY_REGION.NA, characterName: uniqueName("M") }],
      visibility: "listed",
    });
    await env.BLUNION_PROFILES.delete(`group:${groupId}`);

    await callScheduled();
    await callScheduled();

    expect(calls.filter((call) => call.method === "DELETE")).toHaveLength(1);
  });
});
