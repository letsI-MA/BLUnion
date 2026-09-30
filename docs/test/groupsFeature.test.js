import { beforeAll, describe, expect, it } from "vitest";
import { loadPage, runInPage } from "./loadPage.js";

// Mehrköpfige Gruppen-Listungen (handleGroupPut/handleGroupsBrowse in worker/src/index.ts) im
// Web-Companion - Gegenstück zu DrawGroupPublishSection/DrawGroupBrowseSection in
// UI/MainWindow.cs, siehe dortige Doku und die neuen groups*-Funktionen in docs/index.html.
//
// PUT/DELETE-Aufrufe laufen hier gegen einen gemockten window.fetch statt einen echten lokalen
// Worker (bewusst, siehe TEST_REPORT.md §4.1/§4.4 - der Live-Sync-Flow gegen einen echten
// wrangler dev bleibt unabhängig davon separat/nicht committed) - reicht aus, um die
// Request-Form (URL/Methode/Body) und die Token-Speicherung/-Wiederverwendung zu verifizieren,
// ohne einen externen Prozess zu brauchen.
describe("groups feature (multi-member group listings)", () => {
  let window;

  beforeAll(async () => {
    // Gruppen-Logik bleibt testbar, auch wenn der Schalter in index.html aus ist.
    window = await loadPage({ groupsEnabled: true });

    // Test-Helfer im Seiten-Realm (indirektes eval -> globale Funktion, siehe runInPage-Doc):
    // setzt "Mein Eintrag" (= Gruppen-Ersteller, siehe groupsGetCreator) inkl. Token in
    // localStorage, bzw. entfernt das Token bei token === null.
    window.eval(`
      function testSetupCreator(name, world, token) {
        document.getElementById('gfImportInput').value = encodeCompact(name, new Set());
        document.getElementById('gfWorld').value = world;
        if (token) gfSetStoredToken(world, name, token); else gfRemoveStoredToken(world, name);
        gfUpdateMyEntryPreview();
      }
    `);
  });

  it("adds a member from a valid code + world", () => {
    const result = runInPage(
      window,
      function () {
        groupsMembers = [];
        const code = encodeCompact("Alice Test", new Set([SPELLS[0].id, SPELLS[1].id]));
        document.getElementById("groupMemberCodeInput").value = code;
        document.getElementById("groupMemberWorldInput").value = "Gilgamesh";

        groupsAddMemberFromCode();

        return {
          memberCount: groupsMembers.length,
          member: groupsMembers[0],
          statusClass: document.getElementById("groupAddMemberStatus").className,
          codeInputCleared: document.getElementById("groupMemberCodeInput").value === "",
        };
      }.toString(),
    );

    expect(result.memberCount).toBe(1);
    expect(result.member.characterName).toBe("Alice Test");
    expect(result.member.world).toBe("Gilgamesh");
    expect(result.member.learnedCount).toBe(2);
    expect(result.statusClass).toContain("ok");
    expect(result.codeInputCleared).toBe(true);
  });

  it("rejects adding a member when the world field is empty", () => {
    const result = runInPage(
      window,
      function () {
        groupsMembers = [];
        const code = encodeCompact("Bob", new Set());
        document.getElementById("groupMemberCodeInput").value = code;
        document.getElementById("groupMemberWorldInput").value = "   ";

        groupsAddMemberFromCode();

        return {
          memberCount: groupsMembers.length,
          statusClass: document.getElementById("groupAddMemberStatus").className,
        };
      }.toString(),
    );

    expect(result.memberCount).toBe(0);
    expect(result.statusClass).toContain("error");
  });

  it("rejects adding a member for a code without the BLU: prefix", () => {
    const result = runInPage(
      window,
      function () {
        groupsMembers = [];
        document.getElementById("groupMemberCodeInput").value = "not-a-code";
        document.getElementById("groupMemberWorldInput").value = "Gilgamesh";

        groupsAddMemberFromCode();

        return { memberCount: groupsMembers.length, statusClass: document.getElementById("groupAddMemberStatus").className };
      }.toString(),
    );

    expect(result.memberCount).toBe(0);
    expect(result.statusClass).toContain("error");
  });

  it("shows a clear error instead of adding garbage for a truncated/corrupt code (decodeCompact length validation)", () => {
    // Regressionstest für dieselbe decodeCompact-Längenvalidierung wie in syncCode.test.js - hier
    // über den NEUEN Aufrufpfad (groupsAddMemberFromCode) statt importCode().
    const result = runInPage(
      window,
      function () {
        groupsMembers = [];
        const brokenPayload = new Uint8Array([0, 1, 2, 3]); // nameLen=0, aber nur 3 statt 16 Bitmask-Bytes
        document.getElementById("groupMemberCodeInput").value = "BLU:" + bytesToBase64Url(brokenPayload);
        document.getElementById("groupMemberWorldInput").value = "Gilgamesh";

        groupsAddMemberFromCode();

        return {
          memberCount: groupsMembers.length,
          statusClass: document.getElementById("groupAddMemberStatus").className,
          statusText: document.getElementById("groupAddMemberStatus").textContent,
        };
      }.toString(),
    );

    expect(result.memberCount).toBe(0);
    expect(result.statusClass).toContain("error");
    expect(result.statusText.length).toBeGreaterThan(0);
  });

  it("updates learnedCount instead of duplicating when the same member (world+name) is added again", () => {
    const result = runInPage(
      window,
      function () {
        groupsMembers = [];
        groupsAddOrUpdateMember("Gilgamesh", "Alice", 1);
        groupsAddOrUpdateMember("Gilgamesh", "Alice", 7);
        return { memberCount: groupsMembers.length, learnedCount: groupsMembers[0].learnedCount };
      }.toString(),
    );

    expect(result.memberCount).toBe(1);
    expect(result.learnedCount).toBe(7);
  });

  it("removes a member and re-renders the chip list", () => {
    const result = runInPage(
      window,
      function () {
        groupsMembers = [];
        groupsAddOrUpdateMember("Gilgamesh", "Alice", 1);
        groupsAddOrUpdateMember("Excalibur", "Bob", 2);
        renderGroupsMembers();

        groupsRemoveMember("Gilgamesh", "Alice");

        const chips = document.getElementById("groupPublishMembersList").querySelectorAll(".group-member");
        return { memberCount: groupsMembers.length, chipCount: chips.length, remainingName: groupsMembers[0]?.characterName };
      }.toString(),
    );

    expect(result.memberCount).toBe(1);
    expect(result.chipCount).toBe(1);
    expect(result.remainingName).toBe("Bob");
  });

  it("shows the empty hint and no chips when there are no members", () => {
    const result = runInPage(
      window,
      function () {
        groupsMembers = [];
        renderGroupsMembers();
        return {
          emptyHintDisplay: document.getElementById("txt_groupMembers_empty_hint").style.display,
          chipCount: document.getElementById("groupPublishMembersList").querySelectorAll(".group-member").length,
        };
      }.toString(),
    );

    expect(result.emptyHintDisplay).not.toBe("none");
    expect(result.chipCount).toBe(0);
  });

  // --- Veröffentlichen-Flow (PUT), inkl. Token-Speicherung ---

  it("publishes a new group: sends the expected PUT body and stores the returned editToken", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          localStorage.removeItem("blunion_livesync_owngroup");
          testSetupCreator("Alice Test", "Gilgamesh", "creator-token");
          groupsMembers = [];
          groupsAddOrUpdateMember("Gilgamesh", "Bob Test", 1);
          groupsTargetSpellIds = new Set([SPELLS[0].id]);

          document.getElementById("groupPublishVisible").checked = true;
          document.getElementById("groupPublishNote").value = "Testnotiz";
          document.getElementById("groupPublishWantedCount").value = "3";
          document.getElementById("groupPublishTag_evening").checked = true;

          let captured = null;
          window.fetch = async (url, options) => {
            captured = { url, options };
            // Realistischer Mock: der ECHTE Worker spiegelt in stripForGroupResponse() die
            // groupId aus dem URL-Pfad zurück (siehe worker/src/index.ts handleGroupPut) - er
            // erfindet NIE eine eigene. groupsPublish() darf sich für die lokale Speicherung
            // ohnehin NICHT auf ein Response-Feld verlassen, sondern MUSS die selbst generierte/
            // aus der URL verwendete groupId nehmen (analog zu LiveSyncService.cs, das seine
            // client-generierte groupId speichert statt einem Response-Feld zu vertrauen) - dieser
            // Mock spiegelt das nur zur Realismus-Kontrolle.
            const groupIdFromUrl = decodeURIComponent(url.split("/").pop());
            return {
              ok: true,
              status: 201,
              json: async () => ({ groupId: groupIdFromUrl, editToken: "secret-token-1" }),
            };
          };

          await groupsPublish();

          const stored = JSON.parse(localStorage.getItem("blunion_livesync_owngroup"));

          return {
            url: captured.url,
            method: captured.options.method,
            creatorTokenHeader: captured.options.headers["X-Creator-Token"],
            body: JSON.parse(captured.options.body),
            groupIdFromUrl: decodeURIComponent(captured.url.split("/").pop()),
            storedGroupId: stored ? stored.groupId : null,
            storedEditToken: stored ? stored.editToken : null,
            statusClass: document.getElementById("groupsPublishStatus").className,
            deleteButtonDisabled: document.getElementById("btn_groupsDelete").disabled,
          };
        })();
      }.toString(),
    );

    expect(result.method).toBe("PUT");
    expect(result.url).toMatch(/\/group\//); // NICHT /groups/ (das ist nur der Browse-Endpoint)
    // Ersteller ("Mein Eintrag") automatisch als erstes Mitglied + Creator-Nachweis
    expect(result.body.members).toEqual([
      { world: "Gilgamesh", characterName: "Alice Test" },
      { world: "Gilgamesh", characterName: "Bob Test" },
    ]);
    expect(result.body.creatorWorld).toBe("Gilgamesh");
    expect(result.body.creatorCharacterName).toBe("Alice Test");
    expect(result.creatorTokenHeader).toBe("creator-token");
    expect(result.body.visibility).toBe("listed");
    expect(result.body.availabilityTags).toContain("evening");
    expect(result.body.note).toBe("Testnotiz");
    expect(result.body.wantedPlayerCount).toBe(3);
    expect(result.body.targetSpellIds.length).toBe(1);
    expect(result.body.editToken).toBeUndefined(); // Neuanlage - noch kein gespeichertes Token
    // groupId wurde clientseitig generiert (crypto.randomUUID()) - die lokal gespeicherte ID MUSS
    // exakt der in der Request-URL verwendeten entsprechen, unabhängig davon, was der (gemockte)
    // Server zurückliefert.
    expect(result.storedGroupId).toBe(result.groupIdFromUrl);
    expect(result.storedEditToken).toBe("secret-token-1");
    expect(result.statusClass).toContain("ok");
    expect(result.deleteButtonDisabled).toBe(false);
  });

  it("updates an existing group: includes the stored editToken and reuses the stored groupId", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          localStorage.setItem(
            "blunion_livesync_owngroup",
            JSON.stringify({ groupId: "existing-group-id", editToken: "existing-token" }),
          );
          testSetupCreator("Alice Test", "Gilgamesh", "creator-token");
          groupsMembers = [];

          let captured = null;
          window.fetch = async (url, options) => {
            captured = { url, options };
            return { ok: true, status: 200, json: async () => ({}) };
          };

          await groupsPublish();

          return { url: captured.url, body: JSON.parse(captured.options.body) };
        })();
      }.toString(),
    );

    expect(result.url).toContain("existing-group-id");
    expect(result.body.editToken).toBe("existing-token");
  });

  it("rejects publishing without an own entry (no creator token) without calling fetch", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          testSetupCreator("Alice Test", "Gilgamesh", null);
          groupsMembers = [];
          let fetchCalled = false;
          window.fetch = async () => {
            fetchCalled = true;
            return { ok: true, status: 201, json: async () => ({}) };
          };

          await groupsPublish();

          return { fetchCalled, statusClass: document.getElementById("groupsPublishStatus").className };
        })();
      }.toString(),
    );

    expect(result.fetchCalled).toBe(false);
    expect(result.statusClass).toContain("error");
  });

  it("shows an error and does not store a token when the worker rejects the PUT", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          localStorage.removeItem("blunion_livesync_owngroup");
          testSetupCreator("Alice Test", "Gilgamesh", "creator-token");
          groupsMembers = [];

          window.fetch = async () => ({
            ok: false,
            status: 400,
            json: async () => ({ error: "members muss ein Array mit 1-8 Einträgen sein." }),
          });

          await groupsPublish();

          return {
            statusClass: document.getElementById("groupsPublishStatus").className,
            statusText: document.getElementById("groupsPublishStatus").textContent,
            stored: localStorage.getItem("blunion_livesync_owngroup"),
          };
        })();
      }.toString(),
    );

    expect(result.statusClass).toContain("error");
    expect(result.statusText).toContain("members muss ein Array");
    expect(result.stored).toBeNull();
  });

  // --- Ersteller-Nachweis (B3) ---

  it("disables publishing and shows the hint without an own entry, and enables it with one", () => {
    const result = runInPage(
      window,
      function () {
        testSetupCreator("Alice Test", "Gilgamesh", null);
        const without = {
          disabled: document.getElementById("btn_groupsPublish").disabled,
          hintVisible: document.getElementById("groupsCreatorHint").style.display !== "none",
          hintText: document.getElementById("groupsCreatorHint").textContent,
        };

        testSetupCreator("Alice Test", "Gilgamesh", "creator-token");
        const withToken = {
          disabled: document.getElementById("btn_groupsPublish").disabled,
          hintVisible: document.getElementById("groupsCreatorHint").style.display !== "none",
          chips: Array.from(document.querySelectorAll("#groupPublishMembersList .group-member")).map(c => c.textContent),
        };

        return { without, withToken };
      }.toString(),
    );

    expect(result.without.disabled).toBe(true);
    expect(result.without.hintVisible).toBe(true);
    expect(result.without.hintText.length).toBeGreaterThan(0);
    expect(result.withToken.disabled).toBe(false);
    expect(result.withToken.hintVisible).toBe(false);
    expect(result.withToken.chips[0]).toContain("Alice Test (Gilgamesh)");
  });

  it("does not duplicate the creator when they were also added via code", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          localStorage.removeItem("blunion_livesync_owngroup");
          testSetupCreator("Alice Test", "Gilgamesh", "creator-token");
          groupsMembers = [];
          groupsAddOrUpdateMember("gilgamesh", "alice test", 3);
          groupsAddOrUpdateMember("Cerberus", "Bob Test", 1);

          let captured = null;
          window.fetch = async (url, options) => {
            captured = options;
            return { ok: true, status: 201, json: async () => ({ editToken: "t" }) };
          };

          await groupsPublish();
          return JSON.parse(captured.body).members;
        })();
      }.toString(),
    );

    expect(result).toEqual([
      { world: "Gilgamesh", characterName: "Alice Test" },
      { world: "Cerberus", characterName: "Bob Test" },
    ]);
  });

  it("rejects more than 7 additional members (8 including the creator) without calling fetch", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          testSetupCreator("Alice Test", "Gilgamesh", "creator-token");
          groupsMembers = [];
          for (let i = 0; i < 8; i++) groupsAddOrUpdateMember("Gilgamesh", `Member ${String.fromCharCode(97 + i)}x`, 1);

          let fetchCalled = false;
          window.fetch = async () => {
            fetchCalled = true;
            return { ok: true, status: 201, json: async () => ({}) };
          };

          await groupsPublish();
          return { fetchCalled, statusClass: document.getElementById("groupsPublishStatus").className };
        })();
      }.toString(),
    );

    expect(result.fetchCalled).toBe(false);
    expect(result.statusClass).toContain("error");
  });

  it("shows the creator error on 403 and disables publishing when the own profile has expired (404)", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          localStorage.removeItem("blunion_livesync_owngroup");
          testSetupCreator("Alice Test", "Gilgamesh", "creator-token");
          groupsMembers = [];

          window.fetch = async (url, options) => {
            if (options && options.method === "PUT") {
              return { ok: false, status: 403, json: async () => ({ error: "Ersteller-Nachweis fehlgeschlagen" }) };
            }
            return { ok: false, status: 404, json: async () => ({ error: "Kein Profil" }) }; // GET /profile
          };

          await groupsPublish();
          await new Promise((resolve) => setTimeout(resolve, 0)); // groupsCheckCreatorProfile läuft asynchron

          return {
            statusText: document.getElementById("groupsPublishStatus").textContent,
            tokenAfter: gfGetStoredToken("Gilgamesh", "Alice Test"),
            publishDisabled: document.getElementById("btn_groupsPublish").disabled,
            hintVisible: document.getElementById("groupsCreatorHint").style.display !== "none",
          };
        })();
      }.toString(),
    );

    expect(result.statusText).toBe(runInPage(window, function () { return t().groupsErrCreatorRejected; }.toString()));
    expect(result.tokenAfter).toBeNull();
    expect(result.publishDisabled).toBe(true);
    expect(result.hintVisible).toBe(true);
  });

  it("lists missing members from a 400 response as plain text (no markup injection)", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          localStorage.removeItem("blunion_livesync_owngroup");
          testSetupCreator("Alice Test", "Gilgamesh", "creator-token");
          groupsMembers = [];
          groupsAddOrUpdateMember("Gilgamesh", "Bob Test", 1);

          window.fetch = async () => ({
            ok: false,
            status: 400,
            json: async () => ({
              error: "Nicht alle Mitglieder haben ein Live-Sync-Profil",
              missingMembers: [
                { world: "Gilgamesh", characterName: "Bob Test" },
                { world: "Cerberus", characterName: "<img src=x onerror=alert(1)>" },
              ],
            }),
          });

          await groupsPublish();
          const statusEl = document.getElementById("groupsPublishStatus");
          return {
            text: statusEl.textContent,
            hasImgElement: statusEl.querySelector("img") !== null,
            stored: localStorage.getItem("blunion_livesync_owngroup"),
          };
        })();
      }.toString(),
    );

    expect(result.text).toContain("Bob Test (Gilgamesh)");
    expect(result.text).toContain("<img src=x onerror=alert(1)> (Cerberus)");
    expect(result.hasImgElement).toBe(false);
    expect(result.stored).toBeNull();
  });

  it("removes a stale creator token when opening the publish view and the profile is gone (404)", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          testSetupCreator("Alice Test", "Gilgamesh", "creator-token");
          let requestedUrl = null;
          window.fetch = async (url) => {
            requestedUrl = url;
            return { ok: false, status: 404, json: async () => ({}) };
          };

          groupsSetView("publish");
          await new Promise((resolve) => setTimeout(resolve, 0));

          return {
            requestedUrl,
            tokenAfter: gfGetStoredToken("Gilgamesh", "Alice Test"),
            publishDisabled: document.getElementById("btn_groupsPublish").disabled,
          };
        })();
      }.toString(),
    );

    expect(result.requestedUrl).toMatch(/\/profile\/Gilgamesh\/Alice%20Test$/);
    expect(result.tokenAfter).toBeNull();
    expect(result.publishDisabled).toBe(true);
  });

  it("keeps the creator token when the profile check succeeds", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          testSetupCreator("Alice Test", "Gilgamesh", "creator-token");
          window.fetch = async () => ({ ok: true, status: 200, json: async () => ({}) });

          groupsSetView("publish");
          await new Promise((resolve) => setTimeout(resolve, 0));

          return {
            tokenAfter: gfGetStoredToken("Gilgamesh", "Alice Test"),
            publishDisabled: document.getElementById("btn_groupsPublish").disabled,
          };
        })();
      }.toString(),
    );

    expect(result.tokenAfter).toBe("creator-token");
    expect(result.publishDisabled).toBe(false);
  });

  // --- Löschen-Flow (DELETE) ---

  it("deletes the stored group and clears the stored token", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          localStorage.setItem(
            "blunion_livesync_owngroup",
            JSON.stringify({ groupId: "to-delete", editToken: "delete-token" }),
          );
          groupsUpdatePublishButtonState();

          let captured = null;
          window.fetch = async (url, options) => {
            captured = { url, options };
            return { ok: true, status: 200, json: async () => ({ deleted: true }) };
          };

          await groupsDeleteOwn();

          return {
            url: captured.url,
            method: captured.options.method,
            editTokenHeader: captured.options.headers["X-Edit-Token"],
            stored: localStorage.getItem("blunion_livesync_owngroup"),
            statusClass: document.getElementById("groupsPublishStatus").className,
            deleteButtonDisabledAfter: document.getElementById("btn_groupsDelete").disabled,
          };
        })();
      }.toString(),
    );

    expect(result.method).toBe("DELETE");
    expect(result.url).toContain("to-delete");
    expect(result.editTokenHeader).toBe("delete-token");
    expect(result.stored).toBeNull();
    expect(result.statusClass).toContain("ok");
    expect(result.deleteButtonDisabledAfter).toBe(true);
  });

  it("does nothing when there is no stored group to delete", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          localStorage.removeItem("blunion_livesync_owngroup");
          let fetchCalled = false;
          window.fetch = async () => {
            fetchCalled = true;
            return { ok: true, status: 200, json: async () => ({}) };
          };

          await groupsDeleteOwn();

          return { fetchCalled };
        })();
      }.toString(),
    );

    expect(result.fetchCalled).toBe(false);
  });

  // --- Browse-Rendering (inkl. Escaping-Regressionstest) ---

  it("renders a browse result card with member names, tags, note, and target spells", () => {
    const result = runInPage(
      window,
      function () {
        const entries = [
          {
            groupId: "g1",
            members: [
              { world: "Gilgamesh", characterName: "Alice", spellBitmaskBase64: bytesToBase64Url(buildBitmask(new Set([SPELLS[0].id]))) },
              { world: "Gilgamesh", characterName: "Bob", spellBitmaskBase64: null },
            ],
            availabilityTags: ["evening", "weekend"],
            note: "Sucht 2 weitere",
            wantedPlayerCount: 4,
            targetSpellIds: [SPELLS[0].id],
          },
        ];

        renderGroupsBrowseResults(entries);

        const listEl = document.getElementById("groupsBrowseList");
        const card = listEl.querySelector(".gf-entry");
        const memberRows = card.querySelectorAll(".row");

        return {
          cardCount: listEl.querySelectorAll(".gf-entry").length,
          metaText: card.querySelector(".gf-entry-meta").textContent,
          memberRowCount: memberRows.length,
          firstMemberText: memberRows[0].querySelector("span").textContent,
          secondMemberText: memberRows[1].querySelector("span").textContent,
        };
      }.toString(),
    );

    expect(result.cardCount).toBe(1);
    expect(result.metaText).toContain("Sucht 2 weitere");
    expect(result.memberRowCount).toBe(2);
    expect(result.firstMemberText).toContain("Alice");
    expect(result.firstMemberText).toContain("Gilgamesh");
    expect(result.secondMemberText).toContain("Bob");
  });

  it("shows the empty-state hint when there are no groups", () => {
    const result = runInPage(
      window,
      function () {
        renderGroupsBrowseResults([]);
        const listEl = document.getElementById("groupsBrowseList");
        return { cardCount: listEl.querySelectorAll(".gf-entry").length, hintText: listEl.textContent };
      }.toString(),
    );

    expect(result.cardCount).toBe(0);
    expect(result.hintText.length).toBeGreaterThan(0);
  });

  it("escapes HTML special characters in note/tags and never executes injected markup from member names (no XSS)", () => {
    // Analog zu groupComparison.test.js - dieselbe Regressionsklasse, hier für die NEUE
    // renderGroupsBrowseResults()-Funktion statt renderGroupTable()/renderGroup().
    const result = runInPage(
      window,
      function () {
        window.__xssFired = false;
        const entries = [
          {
            groupId: "g-xss",
            members: [
              { world: '"><img src=x onerror=window.__xssFired=true>', characterName: "<img src=x onerror=window.__xssFired=true>", spellBitmaskBase64: null },
            ],
            availabilityTags: [],
            note: '<img src=x onerror=window.__xssFired=true>',
            wantedPlayerCount: 0,
          },
        ];

        renderGroupsBrowseResults(entries);

        const listEl = document.getElementById("groupsBrowseList");
        const unexpectedImgs = Array.from(listEl.querySelectorAll("img"));

        return {
          unexpectedImgCount: unexpectedImgs.length,
          xssFired: window.__xssFired,
          noteText: listEl.querySelector(".gf-entry-meta").textContent,
          memberText: listEl.querySelector(".row span").textContent,
        };
      }.toString(),
    );

    expect(result.unexpectedImgCount).toBe(0);
    expect(result.xssFired).toBe(false);
    expect(result.noteText).toContain("<img");
    expect(result.memberText).toContain("<img");
  });

  it("clicking 'add to comparison' on a browse member uses addOrUpdateGroupMember (same path as the single-player search)", () => {
    const result = runInPage(
      window,
      function () {
        group = [];
        const entries = [
          {
            groupId: "g2",
            members: [
              { world: "Gilgamesh", characterName: "Charlie", spellBitmaskBase64: bytesToBase64Url(buildBitmask(new Set([SPELLS[0].id, SPELLS[1].id]))) },
            ],
            availabilityTags: [],
            note: "",
            wantedPlayerCount: 0,
          },
        ];

        renderGroupsBrowseResults(entries);

        const addBtn = document.getElementById("groupsBrowseList").querySelector(".row button");
        addBtn.click();

        return { groupLength: group.length, addedName: group[0]?.name, learnedSize: group[0]?.learned.size };
      }.toString(),
    );

    expect(result.groupLength).toBe(1);
    expect(result.addedName).toBe("Charlie");
    expect(result.learnedSize).toBe(2);
  });

  it("displays resolved target-spell names for entries that include targetSpellIds", () => {
    const result = runInPage(
      window,
      function () {
        const entries = [
          {
            groupId: "g3",
            members: [],
            availabilityTags: [],
            note: "",
            wantedPlayerCount: 0,
            targetSpellIds: [SPELLS[0].id, SPELLS[1].id],
          },
        ];

        renderGroupsBrowseResults(entries);

        const metas = document.getElementById("groupsBrowseList").querySelectorAll(".gf-entry-meta");
        return { metaCount: metas.length, targetsText: metas[1]?.textContent };
      }.toString(),
    );

    expect(result.metaCount).toBe(2);
    expect(result.targetsText).toBeTruthy();
  });
});
