import { beforeAll, describe, expect, it } from "vitest";
import { loadPage, runInPage } from "./loadPage.js";

// Moderationshinweis, 409 "Name bereits belegt" und clientseitige Namensprüfung (dieselben Regeln
// wie isValidCharacterName in worker/src/index.ts).
describe("moderation hints and character name validation", () => {
  let window;

  beforeAll(async () => {
    window = await loadPage();
  });

  it("shows the 'names are not verified' hint with a Discord link in all four languages", () => {
    const result = runInPage(
      window,
      function () {
        const out = {};
        for (const lang of ["de", "en", "fr", "ja"]) {
          currentLang = lang;
          applyStaticTexts();
          const hint = document.getElementById("txt_gf_moderation_hint");
          const link = hint.querySelector("a");
          out[lang] = { text: hint.textContent, href: link && link.href, expected: I18N[lang].gfModerationHint };
        }
        currentLang = "de";
        applyStaticTexts();
        return out;
      }.toString(),
    );

    for (const lang of ["de", "en", "fr", "ja"]) {
      expect(result[lang].text).toContain(result[lang].expected);
      expect(result[lang].href).toMatch(/^https:\/\/discord\.gg\//);
    }
  });

  it("uses a Latin example name as the Japanese character name placeholder", () => {
    const placeholder = runInPage(window, function () { return I18N.ja.charNamePlaceholder; }.toString());

    expect(placeholder).toBe("例：Y'shtola Rhul");
  });

  it.each(["Alice Test", "Y'shtola Rhul", "Jean-Luc Picard", "Abcdefghij Abcdefghij"])(
    "isValidCharacterName accepts %s like the worker", (name) => {
      const valid = runInPage(window, `function () { return isValidCharacterName(${JSON.stringify(name)}); }`);
      expect(valid).toBe(true);
    });

  it.each(["Alice", "ユウシュトラ ルール", "Alice  Test", "A Test", "Abcdefghijklmno Abcdef", "'Alice Test", "Al1ce Test"])(
    "isValidCharacterName rejects %s like the worker", (name) => {
      const valid = runInPage(window, `function () { return isValidCharacterName(${JSON.stringify(name)}); }`);
      expect(valid).toBe(false);
    });

  it("rejects publishing an entry with an invalid (e.g. Katakana) name before calling fetch", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          currentLang = "ja";
          applyStaticTexts();
          document.getElementById("gfImportInput").value = encodeCompact("ユウシュトラ ルール", new Set());
          document.getElementById("gfWorld").value = "Aegis";

          let fetchCalled = false;
          window.fetch = async () => {
            fetchCalled = true;
            return { ok: true, status: 201, json: async () => ({}) };
          };

          await gfPublishMyEntry();
          const status = document.getElementById("gfMyEntryStatus");
          const out = { fetchCalled, className: status.className, text: status.textContent };
          currentLang = "de";
          applyStaticTexts();
          return out;
        })();
      }.toString(),
    );

    expect(result.fetchCalled).toBe(false);
    expect(result.className).toContain("error");
    expect(result.text).toContain("ユウシュトラ ルール");
    expect(result.text).toContain("アルファベット");
  });

  it("rejects adding a group member with an invalid name", () => {
    const result = runInPage(
      window,
      function () {
        groupsMembers = [];
        document.getElementById("groupMemberCodeInput").value = encodeCompact("Alice", new Set());
        document.getElementById("groupMemberWorldInput").value = "Gilgamesh";

        groupsAddMemberFromCode();

        return {
          memberCount: groupsMembers.length,
          className: document.getElementById("groupAddMemberStatus").className,
        };
      }.toString(),
    );

    expect(result.memberCount).toBe(0);
    expect(result.className).toContain("error");
  });

  it("shows the translated 'name taken' text with a Discord link on 409 name_taken", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          document.getElementById("gfImportInput").value = encodeCompact("Taken Name", new Set());
          document.getElementById("gfWorld").value = "Gilgamesh";
          gfRemoveStoredToken("Gilgamesh", "Taken Name");

          window.fetch = async () => ({
            ok: false,
            status: 409,
            json: async () => ({ error: "Profil existiert bereits", reason: "name_taken" }),
          });

          await gfPublishMyEntry();
          const status = document.getElementById("gfMyEntryStatus");
          const link = status.querySelector("a");
          return {
            text: status.textContent,
            href: link && link.href,
            expected: t().gfErrNameTaken,
            stored: gfGetStoredToken("Gilgamesh", "Taken Name"),
          };
        })();
      }.toString(),
    );

    expect(result.text).toContain(result.expected);
    expect(result.text).not.toContain("Profil existiert bereits"); // kein roher Worker-Text
    expect(result.href).toMatch(/^https:\/\/discord\.gg\//);
    expect(result.stored).toBeNull();
  });

  it("keeps the generic error (no Discord link) on 409 token_mismatch", async () => {
    const result = await runInPage(
      window,
      function () {
        return (async () => {
          document.getElementById("gfImportInput").value = encodeCompact("Stale Token", new Set());
          document.getElementById("gfWorld").value = "Gilgamesh";
          gfSetStoredToken("Gilgamesh", "Stale Token", "old-token");

          window.fetch = async () => ({
            ok: false,
            status: 409,
            json: async () => ({ error: "editToken stimmt nicht", reason: "token_mismatch" }),
          });

          await gfPublishMyEntry();
          const status = document.getElementById("gfMyEntryStatus");
          return { text: status.textContent, hasLink: status.querySelector("a") !== null };
        })();
      }.toString(),
    );

    expect(result.text).toContain("editToken stimmt nicht");
    expect(result.hasLink).toBe(false);
  });

  it("shows the 'name taken' text in all four languages", () => {
    const texts = runInPage(
      window,
      function () { return ["de", "en", "fr", "ja"].map((lang) => I18N[lang].gfErrNameTaken); }.toString(),
    );

    expect(new Set(texts).size).toBe(4);
    for (const text of texts) expect(text).toMatch(/Discord/);
  });
});
