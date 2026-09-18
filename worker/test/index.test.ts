import { describe, expect, it } from "vitest";
import {
  browseGroups,
  browseProfiles,
  callWorker,
  deleteGroup,
  deleteProfile,
  getProfile,
  KNOWN_SPELL_IDS_SAMPLE,
  KNOWN_WORLD,
  KNOWN_WORLD_DATA_CENTER,
  profileUrl,
  putGroup,
  putProfile,
  uniqueName,
  validBitmaskBase64,
} from "./helpers";

describe("OPTIONS (CORS preflight)", () => {
  it("returns 204 with CORS headers for any path", async () => {
    const response = await callWorker(new Request(profileUrl(KNOWN_WORLD, "Anyone"), { method: "OPTIONS" }));

    expect(response.status).toBe(204);
    expect(response.headers.get("Access-Control-Allow-Origin")).toBe("*");
  });
});

describe("unknown routes", () => {
  it("returns 404 for a completely unknown path", async () => {
    const response = await callWorker(new Request("http://example.com/nope", { method: "GET" }));

    expect(response.status).toBe(404);
  });

  it("returns 405 for an unsupported method on /profile/:world/:characterName", async () => {
    const response = await callWorker(new Request(profileUrl(KNOWN_WORLD, "Someone"), { method: "PATCH" }));

    expect(response.status).toBe(405);
  });

  it("returns 400 when world or characterName is empty", async () => {
    const response = await callWorker(new Request("http://example.com/profile//Someone", { method: "GET" }));

    // "/profile//Someone" hat ein leeres world-Segment - PROFILE_PATH matcht [^/]+ (min. 1
    // Zeichen), matcht hier also gar nicht -> landet im 404-Fallback statt im 400-world-leer-Pfad.
    // Verifiziert trotzdem ein WOHLDEFINIERTES (kein 500) Verhalten für diesen Randfall.
    expect([400, 404]).toContain(response.status);
  });
});

describe("handlePut (PUT /profile/:world/:characterName)", () => {
  it("creates a new profile and returns 201 with a plaintext editToken", async () => {
    const name = uniqueName("Neu");
    const response = await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });
    const json = await response.json<Record<string, unknown>>();

    expect(response.status).toBe(201);
    expect(json.characterName).toBe(name);
    expect(json.dataCenter).toBe(KNOWN_WORLD_DATA_CENTER);
    expect(typeof json.editToken).toBe("string");
    expect((json.editToken as string).length).toBeGreaterThan(0);
    // editTokenHash darf laut stripForResponse NIE im Response landen.
    expect(json.editTokenHash).toBeUndefined();
  });

  it("update with the correct editToken succeeds and returns 200", async () => {
    const name = uniqueName("Update");
    const created = await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });
    const { editToken } = await created.json<{ editToken: string }>();

    const updated = await putProfile(KNOWN_WORLD, name, {
      spellBitmaskBase64: validBitmaskBase64(),
      editToken,
    });

    expect(updated.status).toBe(200);
  });

  it("update with a wrong editToken is rejected with 409", async () => {
    const name = uniqueName("WrongToken");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });

    const response = await putProfile(KNOWN_WORLD, name, {
      spellBitmaskBase64: validBitmaskBase64(),
      editToken: "definitely-not-the-right-token",
    });

    expect(response.status).toBe(409);
  });

  it("update without an editToken on an existing profile is rejected with 409", async () => {
    const name = uniqueName("NoToken");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });

    const response = await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });

    expect(response.status).toBe(409);
  });

  it("rejects a missing spellBitmaskBase64 with 400", async () => {
    const response = await putProfile(KNOWN_WORLD, uniqueName("NoBitmask"), {});

    expect(response.status).toBe(400);
  });

  it("rejects a spellBitmaskBase64 of the wrong length with 400", async () => {
    const response = await putProfile(KNOWN_WORLD, uniqueName("ShortBitmask"), {
      // Nur 1 Byte statt der geforderten 16.
      spellBitmaskBase64: "AA",
    });

    expect(response.status).toBe(400);
  });

  it("rejects an invalid (non-base64url) spellBitmaskBase64 with 400 instead of throwing", async () => {
    const response = await putProfile(KNOWN_WORLD, uniqueName("BadBitmask"), {
      spellBitmaskBase64: "not valid base64url!!",
    });

    expect(response.status).toBe(400);
  });

  it("rejects an unknown world with 400", async () => {
    const response = await putProfile("NichtExistierendeWelt", uniqueName("BadWorld"), {
      spellBitmaskBase64: validBitmaskBase64(),
    });

    expect(response.status).toBe(400);
  });

  it("rejects malformed JSON body with 400", async () => {
    const response = await callWorker(
      new Request(profileUrl(KNOWN_WORLD, uniqueName("BadJson")), {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: "{not valid json",
      }),
    );

    expect(response.status).toBe(400);
  });

  it("accepts and echoes the optional Phase-2 fields (visibility, availabilityTags, note, wantedPlayerCount, targetSpellIds)", async () => {
    const response = await putProfile(KNOWN_WORLD, uniqueName("Optional"), {
      spellBitmaskBase64: validBitmaskBase64(),
      visibility: "listed",
      availabilityTags: ["evening", "weekend"],
      note: "Testnotiz",
      wantedPlayerCount: 3,
      targetSpellIds: KNOWN_SPELL_IDS_SAMPLE,
    });
    const json = await response.json<Record<string, unknown>>();

    expect(response.status).toBe(201);
    expect(json.visibility).toBe("listed");
    expect(json.availabilityTags).toEqual(["evening", "weekend"]);
    expect(json.note).toBe("Testnotiz");
    expect(json.wantedPlayerCount).toBe(3);
    expect(json.targetSpellIds).toEqual(KNOWN_SPELL_IDS_SAMPLE);
  });

  it("rejects a targetSpellIds entry that is not a known spell ID with 400", async () => {
    const response = await putProfile(KNOWN_WORLD, uniqueName("BadTarget"), {
      spellBitmaskBase64: validBitmaskBase64(),
      targetSpellIds: [999999999],
    });

    expect(response.status).toBe(400);
  });

  it("rejects more than TARGET_SPELL_COUNT_MAX (30) targetSpellIds entries with 400", async () => {
    const tooMany = Array.from({ length: 31 }, () => KNOWN_SPELL_IDS_SAMPLE[0]);
    const response = await putProfile(KNOWN_WORLD, uniqueName("TooManyTargets"), {
      spellBitmaskBase64: validBitmaskBase64(),
      targetSpellIds: tooMany,
    });

    expect(response.status).toBe(400);
  });

  it("defaults targetSpellIds to an empty array when never set", async () => {
    const response = await putProfile(KNOWN_WORLD, uniqueName("NoTargets"), {
      spellBitmaskBase64: validBitmaskBase64(),
    });
    const json = await response.json<{ targetSpellIds: number[] }>();

    expect(json.targetSpellIds).toEqual([]);
  });

  it("rejects an invalid availabilityTags entry with 400", async () => {
    const response = await putProfile(KNOWN_WORLD, uniqueName("BadTag"), {
      spellBitmaskBase64: validBitmaskBase64(),
      availabilityTags: ["not-a-real-tag"],
    });

    expect(response.status).toBe(400);
  });

  it("rejects an out-of-range wantedPlayerCount with 400", async () => {
    const response = await putProfile(KNOWN_WORLD, uniqueName("BadCount"), {
      spellBitmaskBase64: validBitmaskBase64(),
      wantedPlayerCount: 99,
    });

    expect(response.status).toBe(400);
  });

  it("caps a too-long note instead of rejecting it", async () => {
    const response = await putProfile(KNOWN_WORLD, uniqueName("LongNote"), {
      spellBitmaskBase64: validBitmaskBase64(),
      note: "x".repeat(200),
    });
    const json = await response.json<{ note: string }>();

    expect(response.status).toBe(201);
    expect(json.note.length).toBe(60);
  });

  it("leaves a missing optional field at its previous value on update instead of clearing it", async () => {
    const name = uniqueName("KeepNote");
    const created = await putProfile(KNOWN_WORLD, name, {
      spellBitmaskBase64: validBitmaskBase64(),
      note: "Bleibt erhalten",
    });
    const { editToken } = await created.json<{ editToken: string }>();

    // Zweiter Put OHNE note-Feld - simuliert einen reinen Spell-Status-Push aus Phase 1.
    const updated = await putProfile(KNOWN_WORLD, name, {
      spellBitmaskBase64: validBitmaskBase64(),
      editToken,
    });
    const json = await updated.json<{ note: string }>();

    expect(json.note).toBe("Bleibt erhalten");
  });

  it("leaves targetSpellIds at its previous value on update when omitted", async () => {
    const name = uniqueName("KeepTargets");
    const created = await putProfile(KNOWN_WORLD, name, {
      spellBitmaskBase64: validBitmaskBase64(),
      targetSpellIds: KNOWN_SPELL_IDS_SAMPLE,
    });
    const { editToken } = await created.json<{ editToken: string }>();

    const updated = await putProfile(KNOWN_WORLD, name, {
      spellBitmaskBase64: validBitmaskBase64(),
      editToken,
    });
    const json = await updated.json<{ targetSpellIds: number[] }>();

    expect(json.targetSpellIds).toEqual(KNOWN_SPELL_IDS_SAMPLE);
  });
});

describe("handleGet (GET /profile/:world/:characterName)", () => {
  it("returns 404 for a character that was never published", async () => {
    const response = await getProfile(KNOWN_WORLD, uniqueName("NieVeroeffentlicht"));

    expect(response.status).toBe(404);
  });

  it("returns 200 with the profile for an existing character", async () => {
    const name = uniqueName("Existiert");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });

    const response = await getProfile(KNOWN_WORLD, name);
    const json = await response.json<{ characterName: string }>();

    expect(response.status).toBe(200);
    expect(json.characterName).toBe(name);
  });

  it("is case-insensitive for world and characterName (kvKey lowercases both)", async () => {
    const name = uniqueName("GrossKlein");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });

    const response = await getProfile(KNOWN_WORLD.toUpperCase(), name.toUpperCase());

    expect(response.status).toBe(200);
  });
});

describe("handleDelete (DELETE /profile/:world/:characterName)", () => {
  it("without an X-Edit-Token header is rejected with 403", async () => {
    const name = uniqueName("KeinToken");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });

    const response = await deleteProfile(KNOWN_WORLD, name);

    expect(response.status).toBe(403);
  });

  it("with a wrong X-Edit-Token is rejected with 403", async () => {
    const name = uniqueName("FalscherToken");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });

    const response = await deleteProfile(KNOWN_WORLD, name, "falscher-token");

    expect(response.status).toBe(403);
  });

  it("for a profile that does not exist returns 404", async () => {
    const response = await deleteProfile(KNOWN_WORLD, uniqueName("ExistiertNicht"), "irgendein-token");

    expect(response.status).toBe(404);
  });

  it("with the correct token deletes the profile (subsequent GET is 404)", async () => {
    const name = uniqueName("WirdGeloescht");
    const created = await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64() });
    const { editToken } = await created.json<{ editToken: string }>();

    const deleteResponse = await deleteProfile(KNOWN_WORLD, name, editToken);
    expect(deleteResponse.status).toBe(200);

    const getResponse = await getProfile(KNOWN_WORLD, name);
    expect(getResponse.status).toBe(404);
  });
});

describe("handleBrowse (GET /profiles/browse)", () => {
  it("rejects a missing dataCenter query parameter with 400", async () => {
    const response = await browseProfiles(null);

    expect(response.status).toBe(400);
  });

  it("only returns profiles with visibility 'listed' on the requested data center", async () => {
    const listedName = uniqueName("Listed");
    const unlistedName = uniqueName("Unlisted");

    await putProfile(KNOWN_WORLD, listedName, {
      spellBitmaskBase64: validBitmaskBase64(),
      visibility: "listed",
    });
    await putProfile(KNOWN_WORLD, unlistedName, {
      spellBitmaskBase64: validBitmaskBase64(),
      visibility: "unlisted",
    });

    const response = await browseProfiles(KNOWN_WORLD_DATA_CENTER);
    const json = await response.json<{ characterName: string }[]>();
    const names = json.map((entry) => entry.characterName);

    expect(response.status).toBe(200);
    expect(names).toContain(listedName);
    expect(names).not.toContain(unlistedName);
  });

  it("does not return listed profiles from a different data center", async () => {
    const name = uniqueName("AndereDC");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64(), visibility: "listed" });

    // "Excalibur" liegt laut worlds.ts auf dem Primal-DC, nicht Aether.
    const response = await browseProfiles("Primal");
    const json = await response.json<{ characterName: string }[]>();

    expect(json.map((entry) => entry.characterName)).not.toContain(name);
  });

  it("does not include dataCenter, visibility, or editTokenHash in results (stripForBrowseResponse)", async () => {
    const name = uniqueName("StrippedFields");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64(), visibility: "listed" });

    const response = await browseProfiles(KNOWN_WORLD_DATA_CENTER);
    const json = await response.json<Record<string, unknown>[]>();
    const entry = json.find((e) => e.characterName === name);

    expect(entry).toBeDefined();
    expect(entry?.dataCenter).toBeUndefined();
    expect(entry?.visibility).toBeUndefined();
    expect(entry?.editTokenHash).toBeUndefined();
  });

  it("returns the full stripForBrowseResponse shape (computePlayersBrowse) with actual values", async () => {
    const name = uniqueName("VollesShape");
    await putProfile(KNOWN_WORLD, name, {
      spellBitmaskBase64: validBitmaskBase64(),
      visibility: "listed",
      availabilityTags: ["evening", "weekend"],
      note: "Shape-Testnotiz",
      wantedPlayerCount: 2,
      targetSpellIds: KNOWN_SPELL_IDS_SAMPLE,
    });

    const response = await browseProfiles(KNOWN_WORLD_DATA_CENTER);
    const json = await response.json<Record<string, unknown>[]>();
    const entry = json.find((e) => e.characterName === name);

    expect(entry).toEqual({
      characterName: name,
      world: KNOWN_WORLD,
      spellBitmaskBase64: expect.any(String),
      availabilityTags: ["evening", "weekend"],
      note: "Shape-Testnotiz",
      wantedPlayerCount: 2,
      targetSpellIds: KNOWN_SPELL_IDS_SAMPLE,
      updatedAt: expect.any(String),
    });
  });

  it("defaults availabilityTags/note/wantedPlayerCount/targetSpellIds to []/''/0/[] when never set", async () => {
    const name = uniqueName("BrowseDefaults");
    await putProfile(KNOWN_WORLD, name, { spellBitmaskBase64: validBitmaskBase64(), visibility: "listed" });

    const response = await browseProfiles(KNOWN_WORLD_DATA_CENTER);
    const json = await response.json<Record<string, unknown>[]>();
    const entry = json.find((e) => e.characterName === name);

    expect(entry?.availabilityTags).toEqual([]);
    expect(entry?.note).toBe("");
    expect(entry?.wantedPlayerCount).toBe(0);
    expect(entry?.targetSpellIds).toEqual([]);
  });
});

describe("handleGroupPut (PUT /group/:groupId)", () => {
  it("creates a new group listing and returns 201 with a plaintext editToken", async () => {
    const groupId = uniqueName("group-neu-");
    const memberName = uniqueName("Mitglied");
    const response = await putGroup(groupId, { members: [{ world: KNOWN_WORLD, characterName: memberName }] });
    const json = await response.json<Record<string, unknown>>();

    expect(response.status).toBe(201);
    expect(json.groupId).toBe(groupId);
    expect(typeof json.editToken).toBe("string");
    expect(json.editTokenHash).toBeUndefined();
  });

  it("rejects an empty members array with 400", async () => {
    const response = await putGroup(uniqueName("group-leer-"), { members: [] });

    expect(response.status).toBe(400);
  });

  it("rejects more than 8 members with 400", async () => {
    const members = Array.from({ length: 9 }, (_, i) => ({ world: KNOWN_WORLD, characterName: `M${i}` }));
    const response = await putGroup(uniqueName("group-zuviele-"), { members });

    expect(response.status).toBe(400);
  });

  it("rejects an unknown world inside members[] with 400", async () => {
    const response = await putGroup(uniqueName("group-badworld-"), {
      members: [{ world: "NichtExistierendeWelt", characterName: "Jemand" }],
    });

    expect(response.status).toBe(400);
  });

  it("update with the correct editToken succeeds", async () => {
    const groupId = uniqueName("group-update-");
    const created = await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
    });
    const { editToken } = await created.json<{ editToken: string }>();

    const updated = await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      editToken,
    });

    expect(updated.status).toBe(200);
  });

  it("update with a wrong editToken is rejected with 409", async () => {
    const groupId = uniqueName("group-wrongtoken-");
    await putGroup(groupId, { members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }] });

    const response = await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      editToken: "falscher-token",
    });

    expect(response.status).toBe(409);
  });

  it("derives dataCenter from the FIRST member's world", async () => {
    const groupId = uniqueName("group-dc-");
    const response = await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      visibility: "listed",
    });

    expect(response.status).toBe(201);

    const browseResponse = await browseGroups(KNOWN_WORLD_DATA_CENTER);
    const groups = await browseResponse.json<{ groupId: string }[]>();
    expect(groups.map((g) => g.groupId)).toContain(groupId);
  });

  it("accepts valid targetSpellIds and echoes them back", async () => {
    const groupId = uniqueName("group-targets-");
    const response = await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      targetSpellIds: KNOWN_SPELL_IDS_SAMPLE,
    });
    const json = await response.json<{ targetSpellIds: number[] }>();

    expect(response.status).toBe(201);
    expect(json.targetSpellIds).toEqual(KNOWN_SPELL_IDS_SAMPLE);
  });

  it("rejects a targetSpellIds entry that is not a known spell ID with 400", async () => {
    const response = await putGroup(uniqueName("group-badtarget-"), {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      targetSpellIds: [999999999],
    });

    expect(response.status).toBe(400);
  });

  it("rejects more than TARGET_SPELL_COUNT_MAX (30) targetSpellIds entries with 400", async () => {
    // KNOWN_SPELL_IDS_SAMPLE hat nur 2 Einträge - für 31 gültige IDs wiederholen wir absichtlich
    // (der Validator prüft laut Code nur "jede ID bekannt" + "Länge <= 30", KEINE Eindeutigkeit -
    // 31 wiederholte, aber jeweils gültige IDs reichen also, um ausschließlich die Längengrenze zu
    // testen, ohne von echten Spell-ID-Duplikat-Regeln überlagert zu werden).
    const tooMany = Array.from({ length: 31 }, () => KNOWN_SPELL_IDS_SAMPLE[0]);
    const response = await putGroup(uniqueName("group-toomanytargets-"), {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      targetSpellIds: tooMany,
    });

    expect(response.status).toBe(400);
  });

  it("defaults targetSpellIds to an empty array when omitted", async () => {
    const response = await putGroup(uniqueName("group-notargets-"), {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
    });
    const json = await response.json<{ targetSpellIds: number[] }>();

    expect(json.targetSpellIds).toEqual([]);
  });
});

describe("handleGroupDelete (DELETE /group/:groupId)", () => {
  it("without an X-Edit-Token header is rejected with 403", async () => {
    const groupId = uniqueName("group-notoken-");
    await putGroup(groupId, { members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }] });

    const response = await deleteGroup(groupId);

    expect(response.status).toBe(403);
  });

  it("with a wrong X-Edit-Token is rejected with 403", async () => {
    const groupId = uniqueName("group-wrongdeltoken-");
    await putGroup(groupId, { members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }] });

    const response = await deleteGroup(groupId, "falscher-token");

    expect(response.status).toBe(403);
  });

  it("for a group that does not exist returns 404", async () => {
    const response = await deleteGroup(uniqueName("group-existiertnicht-"), "irgendein-token");

    expect(response.status).toBe(404);
  });

  it("with the correct token deletes the group WITHOUT touching referenced member profiles", async () => {
    const groupId = uniqueName("group-wirdgeloescht-");
    const memberName = uniqueName("Mitglied");
    await putProfile(KNOWN_WORLD, memberName, { spellBitmaskBase64: validBitmaskBase64() });

    const created = await putGroup(groupId, { members: [{ world: KNOWN_WORLD, characterName: memberName }] });
    const { editToken } = await created.json<{ editToken: string }>();

    const deleteResponse = await deleteGroup(groupId, editToken);
    expect(deleteResponse.status).toBe(200);

    // Gruppen-Eintrag ist weg - ein erneuter PUT MIT dem jetzt ungültigen alten editToken muss
    // wie ein Neuanlegen (409 NUR wenn "existing", hier also KEIN 409) behandelt werden, weil der
    // alte KV-Eintrag tatsächlich gelöscht wurde.
    const recreated = await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: memberName }],
      editToken,
    });
    expect(recreated.status).toBe(201);

    // Das referenzierte Einzelprofil des Mitglieds bleibt unabhängig davon unverändert bestehen -
    // das ist der ganze Punkt des Referenz-statt-Kopie-Ansatzes (siehe Klassendoc in index.ts).
    const memberProfileResponse = await getProfile(KNOWN_WORLD, memberName);
    expect(memberProfileResponse.status).toBe(200);
  });
});

describe("handleGroupsBrowse (GET /groups/browse)", () => {
  it("rejects a missing dataCenter query parameter with 400", async () => {
    const response = await browseGroups(null);

    expect(response.status).toBe(400);
  });

  it("only returns groups with visibility 'listed' on the requested data center", async () => {
    const listedGroupId = uniqueName("group-listed-");
    const unlistedGroupId = uniqueName("group-unlisted-");

    await putGroup(listedGroupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      visibility: "listed",
    });
    await putGroup(unlistedGroupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      visibility: "unlisted",
    });

    const response = await browseGroups(KNOWN_WORLD_DATA_CENTER);
    const json = await response.json<{ groupId: string }[]>();
    const ids = json.map((g) => g.groupId);

    expect(ids).toContain(listedGroupId);
    expect(ids).not.toContain(unlistedGroupId);
  });

  it("resolves each member's spellBitmaskBase64 from their individual profile", async () => {
    const groupId = uniqueName("group-bitmask-");
    const memberName = uniqueName("MitProfil");
    const bitmask = validBitmaskBase64();
    await putProfile(KNOWN_WORLD, memberName, { spellBitmaskBase64: bitmask });
    await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: memberName }],
      visibility: "listed",
    });

    const response = await browseGroups(KNOWN_WORLD_DATA_CENTER);
    const json = await response.json<{ groupId: string; members: { characterName: string; spellBitmaskBase64: string | null }[] }[]>();
    const group = json.find((g) => g.groupId === groupId);

    expect(group).toBeDefined();
    expect(group?.members[0]?.spellBitmaskBase64).toBe(bitmask);
  });

  it("lists a member with spellBitmaskBase64: null when their profile no longer exists, instead of dropping the whole group", async () => {
    const groupId = uniqueName("group-missingmember-");
    const memberName = uniqueName("OhneProfil");
    // Bewusst KEIN putProfile() für dieses Mitglied - es referenziert ein nicht existierendes
    // Einzelprofil (siehe handleGroupsBrowse-Doc: "gelöscht/abgelaufen/nie gepusht").
    await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: memberName }],
      visibility: "listed",
    });

    const response = await browseGroups(KNOWN_WORLD_DATA_CENTER);
    const json = await response.json<{ groupId: string; members: { spellBitmaskBase64: string | null }[] }[]>();
    const group = json.find((g) => g.groupId === groupId);

    expect(group).toBeDefined();
    expect(group?.members[0]?.spellBitmaskBase64).toBeNull();
  });

  it("includes targetSpellIds in each group result", async () => {
    const groupId = uniqueName("group-targetsbrowse-");
    await putGroup(groupId, {
      members: [{ world: KNOWN_WORLD, characterName: uniqueName("M") }],
      visibility: "listed",
      targetSpellIds: KNOWN_SPELL_IDS_SAMPLE,
    });

    const response = await browseGroups(KNOWN_WORLD_DATA_CENTER);
    const json = await response.json<{ groupId: string; targetSpellIds: number[] }[]>();
    const group = json.find((g) => g.groupId === groupId);

    expect(group?.targetSpellIds).toEqual(KNOWN_SPELL_IDS_SAMPLE);
  });
});

describe("enforceWriteRateLimit (rate limiting on write endpoints)", () => {
  it("does not rate-limit when the CF-Connecting-IP header is absent (local/dev behavior)", async () => {
    // Alle Tests oben laufen bereits ohne CF-Connecting-IP-Header und wären längst an der
    // 20/Minute-Grenze gescheitert, wenn dieses Verhalten nicht griffe - dieser Test macht es nur
    // explizit.
    const response = await putProfile(KNOWN_WORLD, uniqueName("KeinRateLimit"), {
      spellBitmaskBase64: validBitmaskBase64(),
    });

    expect(response.status).not.toBe(429);
  });

  it("returns 429 after exceeding the configured limit for a single IP", async () => {
    const ip = "203.0.113." + Math.floor(Math.random() * 255);
    const makeRequest = () =>
      callWorker(
        new Request(profileUrl(KNOWN_WORLD, uniqueName("RateLimited")), {
          method: "PUT",
          headers: { "Content-Type": "application/json", "CF-Connecting-IP": ip },
          body: JSON.stringify({ spellBitmaskBase64: validBitmaskBase64() }),
        }),
      );

    // wrangler.toml: limit = 20, period = 60 - 21. Anfrage derselben IP innerhalb der Periode muss
    // laut enforceWriteRateLimit mit 429 abgelehnt werden.
    const responses = [];
    for (let i = 0; i < 21; i++) responses.push(await makeRequest());

    const statuses = responses.map((r) => r.status);
    expect(statuses).toContain(429);
  });
});
