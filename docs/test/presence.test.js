import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { beforeAll, describe, expect, it } from "vitest";
import { loadPage, runInPage } from "./loadPage.js";

// Online-Status im Gruppenfinder-Browse (online/lastSeen aus GET /profiles/browse, siehe
// computePlayersBrowse im Worker) und der "/tell kopieren"-Button. Die Website zeigt nur an und
// sendet selbst keine Presence-Signale.
describe("group finder presence display", () => {
  let window;

  beforeAll(async () => {
    window = await loadPage();
  });

  function render(entries) {
    return runInPage(
      window,
      `function () {
        currentLang = "en";
        gfRenderBrowseResults(${JSON.stringify(entries)});
        return Array.from(document.getElementById("gfBrowseList").querySelectorAll(".gf-entry")).map((card) => {
          const presence = card.querySelector(".gf-entry-presence");
          return {
            presenceText: presence ? presence.textContent : null,
            online: presence ? presence.classList.contains("online") : null,
            buttons: Array.from(card.querySelectorAll("button")).map((button) => button.textContent),
          };
        });
      }`,
    );
  }

  const base = { world: "Gilgamesh", spellBitmaskBase64: "AAAAAAAAAAAAAAAAAAAAAA" };

  it("shows online, last seen or nothing", () => {
    const twoHoursAgo = new Date(Date.now() - 2 * 3600 * 1000 - 60 * 1000).toISOString();
    const cards = render([
      { ...base, characterName: "Online Player", online: true, lastSeen: new Date().toISOString() },
      { ...base, characterName: "Away Player", online: false, lastSeen: twoHoursAgo },
      { ...base, characterName: "Quiet Player" },
    ]);

    expect(cards[0]).toMatchObject({ presenceText: "● online", online: true });
    expect(cards[1]).toMatchObject({ presenceText: "last online 2 h ago", online: false });
    expect(cards[2].presenceText).toBeNull();
  });

  it("formats last seen roughly in minutes, hours and days", () => {
    const result = runInPage(
      window,
      function () {
        currentLang = "de";
        const now = Date.parse("2026-09-30T12:00:00.000Z");
        const at = (minutesAgo) => gfFormatPresence({ online: false, lastSeen: new Date(now - minutesAgo * 60000).toISOString() }, now).text;
        return [at(0), at(45), at(60), at(23 * 60 + 59), at(3 * 24 * 60)];
      }.toString(),
    );

    expect(result).toEqual([
      "zuletzt online vor 1 Min.",
      "zuletzt online vor 45 Min.",
      "zuletzt online vor 1 Std.",
      "zuletzt online vor 23 Std.",
      "zuletzt online vor 3 Tag(en)",
    ]);
  });

  it("every card has the add and the copy /tell button", () => {
    const cards = render([{ ...base, characterName: "Quiet Player" }]);

    expect(cards[0].buttons).toEqual(["Add to comparison", "Copy /tell"]);
  });

  it("copy /tell puts '/tell Name@World ' into the clipboard and confirms", async () => {
    const result = await runInPage(
      window,
      async function () {
        currentLang = "en";
        let copied = null;
        Object.defineProperty(window.navigator, "clipboard", {
          configurable: true,
          value: { writeText: async (text) => { copied = text; } },
        });

        gfRenderBrowseResults([{ characterName: "Alice Test", world: "Gilgamesh", spellBitmaskBase64: "AAAAAAAAAAAAAAAAAAAAAA" }]);
        document.querySelector("#gfBrowseList .gf-tell").click();
        await new Promise((resolve) => setTimeout(resolve, 10));

        const status = document.getElementById("gfBrowseStatus");
        return { copied, statusClass: status.className, statusText: status.textContent };
      }.toString(),
    );

    expect(result.copied).toBe("/tell Alice Test@Gilgamesh ");
    expect(result.statusClass).toContain("ok");
    expect(result.statusText).toContain("/tell Alice Test@Gilgamesh");
  });

  it("shows the command as an error hint when the clipboard is not available", async () => {
    const result = await runInPage(
      window,
      async function () {
        currentLang = "en";
        Object.defineProperty(window.navigator, "clipboard", {
          configurable: true,
          value: { writeText: async () => { throw new Error("denied"); } },
        });

        gfRenderBrowseResults([{ characterName: "Alice Test", world: "Gilgamesh", spellBitmaskBase64: "AAAAAAAAAAAAAAAAAAAAAA" }]);
        document.querySelector("#gfBrowseList .gf-tell").click();
        await new Promise((resolve) => setTimeout(resolve, 10));

        const status = document.getElementById("gfBrowseStatus");
        return { statusClass: status.className, statusText: status.textContent };
      }.toString(),
    );

    expect(result.statusClass).toContain("error");
    expect(result.statusText).toContain("/tell Alice Test@Gilgamesh");
  });

  it("the website never sends presence signals itself", () => {
    const html = fs.readFileSync(path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "index.html"), "utf8");

    expect(html).not.toMatch(/\/presence\//);
  });
});
