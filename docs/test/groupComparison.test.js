import { beforeAll, describe, expect, it } from "vitest";
import { loadPage, runInPage } from "./loadPage.js";

// Gruppenvergleich (siehe TEST_REPORT.md §4.3): addOrUpdateGroupMember/renderGroupTable/renderGroup
// gegen mehrere importierte Mitglieder, inkl. der innerHTML/XSS-Regressionschecks aus §4.5.
describe("group comparison (addOrUpdateGroupMember / renderGroupTable / renderGroup)", () => {
  let window;

  beforeAll(async () => {
    window = await loadPage();
  });

  it("adds a new member", () => {
    const result = runInPage(
      window,
      function () {
        group = [];
        addOrUpdateGroupMember("Alice", new Set([1, 2]));
        return { length: group.length, name: group[0]?.name };
      }.toString(),
    );

    expect(result.length).toBe(1);
    expect(result.name).toBe("Alice");
  });

  it("overwrites instead of duplicating when the same name is added again", () => {
    const result = runInPage(
      window,
      function () {
        group = [];
        addOrUpdateGroupMember("Alice", new Set([1]));
        addOrUpdateGroupMember("Alice", new Set([1, 2, 3]));
        return { length: group.length, learnedSize: group[0]?.learned.size };
      }.toString(),
    );

    expect(result.length).toBe(1);
    expect(result.learnedSize).toBe(3);
  });

  it("keeps multiple distinct members separate", () => {
    const length = runInPage(
      window,
      function () {
        group = [];
        addOrUpdateGroupMember("Alice", new Set([1]));
        addOrUpdateGroupMember("Bob", new Set([2]));
        addOrUpdateGroupMember("Charlie", new Set([3]));
        return group.length;
      }.toString(),
    );

    expect(length).toBe(3);
  });

  it("renderGroupTable produces one row per visible spell with correct missing/known lists", () => {
    const result = runInPage(
      window,
      function () {
        group = [
          { name: "Alice", learned: new Set([SPELLS[0].id]) },
          { name: "Bob", learned: new Set([]) },
        ];
        document.getElementById("filterGroup").value = "";
        document.getElementById("hideTotemsToggle").checked = false;
        document.getElementById("groupSortMode").value = "order";
        renderGroupTable("");

        const tableEl = document.getElementById("groupTable");
        const rows = tableEl.querySelectorAll(".group-row");
        return {
          rowCount: rows.length,
          spellCount: SPELLS.length,
          firstRowKnown: rows[0].querySelector(".known").textContent,
          firstRowMissing: rows[0].querySelector(".missing").textContent,
        };
      }.toString(),
    );

    expect(result.rowCount).toBe(result.spellCount);
    expect(result.firstRowKnown).toContain("Alice");
    expect(result.firstRowMissing).toContain("Bob");
  });

  it("renderGroupTable respects the text filter (only matching spells get a row)", () => {
    const result = runInPage(
      window,
      function () {
        group = [{ name: "Alice", learned: new Set() }];
        document.getElementById("hideTotemsToggle").checked = false;
        document.getElementById("groupSortMode").value = "order";
        renderGroupTable("#" + SPELLS[0].order);

        const tableEl = document.getElementById("groupTable");
        return tableEl.querySelectorAll(".group-row").length;
      }.toString(),
    );

    expect(result).toBe(1);
  });

  // --- innerHTML/XSS-Regressionschecks (siehe TEST_REPORT.md §4.5) ---

  it("escapes HTML special characters in a member name inside renderGroupTable (no XSS)", () => {
    const result = runInPage(
      window,
      function () {
        group = [{ name: "<img src=x onerror=alert(1)>", learned: new Set() }];
        document.getElementById("filterGroup").value = "";
        document.getElementById("hideTotemsToggle").checked = false;
        document.getElementById("groupSortMode").value = "order";
        renderGroupTable("");

        const tableEl = document.getElementById("groupTable");
        const unexpectedImgs = Array.from(tableEl.querySelectorAll("img")).filter(
          (img) => !img.classList.contains("spell-icon"),
        );
        return {
          unexpectedImgCount: unexpectedImgs.length,
          missingText: tableEl.querySelector(".missing").textContent,
        };
      }.toString(),
    );

    expect(result.unexpectedImgCount).toBe(0);
    expect(result.missingText).toContain("<img");
  });

  it("does not execute injected markup in the member chips built by renderGroup (no XSS)", () => {
    const result = runInPage(
      window,
      function () {
        window.__xssFired = false;
        group = [{ name: "<img src=x onerror=window.__xssFired=true>", learned: new Set([1]) }];
        renderGroup();

        const membersList = document.getElementById("groupMembersList");
        return {
          imgCount: membersList.querySelectorAll("img").length,
          xssFired: window.__xssFired,
        };
      }.toString(),
    );

    expect(result.imgCount).toBe(0);
    expect(result.xssFired).toBe(false);
  });
});
