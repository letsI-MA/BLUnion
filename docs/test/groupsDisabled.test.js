import { beforeAll, describe, expect, it } from "vitest";
import { loadPage, runInPage } from "./loadPage.js";

// GROUPS_ENABLED = false (Standard in index.html): der Gruppen-Block im Gruppenfinder ist
// ausgeblendet und keine Aktion schickt einen Request an /group bzw. /groups/browse. Die
// Gruppen-Logik selbst testet groupsFeature.test.js mit eingeschaltetem Schalter.
describe("groups feature switched off (GROUPS_ENABLED = false)", () => {
  let window;

  beforeAll(async () => {
    window = await loadPage();
  });

  it("uses the switch value from index.html (false)", () => {
    expect(runInPage(window, function () { return GROUPS_ENABLED; }.toString())).toBe(false);
  });

  it("hides the whole groups block including publish and browse", () => {
    const result = runInPage(
      window,
      function () {
        const section = document.getElementById("groupsSection");
        const ids = [
          "txt_groups_header", "btn_groupsViewPublish", "btn_groupsViewBrowse", "groupsPublishView",
          "btn_groupsPublish", "btn_groupsDelete", "groupsBrowseView", "groupsBrowseList", "groupsCreatorHint",
        ];
        return {
          sectionDisplay: section.style.display,
          allInside: ids.every((id) => section.contains(document.getElementById(id))),
        };
      }.toString(),
    );

    expect(result.sectionDisplay).toBe("none");
    expect(result.allInside).toBe(true);
  });

  it("keeps the single-player group finder and the local group comparison visible", () => {
    const result = runInPage(
      window,
      function () {
        const isHiddenByGroups = (id) => document.getElementById("groupsSection").contains(document.getElementById(id));
        return ["gfDataCenter", "btn_gfRefresh", "txt_h2_group", "groupMembersList"].map(isHiddenByGroups);
      }.toString(),
    );

    expect(result).toEqual([false, false, false, false]);
  });

  it("sends no request to /group or /groups/browse, whatever is triggered", async () => {
    const urls = await runInPage(
      window,
      async function () {
        const urls = [];
        window.fetch = async (url) => {
          urls.push(String(url));
          return { ok: true, status: 200, json: async () => [] };
        };

        // Eigener Eintrag + gespeicherte eigene Gruppe, damit Publish/Delete nicht schon vorher abbrechen würden.
        document.getElementById("gfImportInput").value = encodeCompact("Alice Test", new Set());
        document.getElementById("gfWorld").value = "Gilgamesh";
        gfSetStoredToken("Gilgamesh", "Alice Test", "token");
        gfUpdateMyEntryPreview();

        const select = document.getElementById("gfDataCenter");
        select.value = "Aether";
        select.dispatchEvent(new Event("change"));

        await groupsBrowse();
        await groupsPublish();
        await groupsDeleteOwn();
        await groupsCheckCreatorProfile();
        groupsSetView("publish");
        groupsSetView("browse");
        await new Promise((resolve) => setTimeout(resolve, 50));

        return urls;
      }.toString(),
    );

    expect(urls.filter((url) => /\/groups?\//.test(url))).toEqual([]);
  });
});
