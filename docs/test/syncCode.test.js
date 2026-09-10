import { beforeAll, describe, expect, it } from "vitest";
import { loadPage, runInPage } from "./loadPage.js";

// Export/Import-Symmetrie von encodeCompact/decodeCompact (siehe TEST_REPORT.md §4.2) - inkl. des
// dort unter §5.1 dokumentierten und hier behobenen Findings: decodeCompact validiert jetzt die
// Payload-Länge analog zu ManualCodeSyncProvider.DecodeCurrentFormat im Plugin, statt mit
// abgeschnittenen/kaputten Codes klaglos Datenmüll zu produzieren.
describe("encodeCompact / decodeCompact", () => {
  let window;

  beforeAll(async () => {
    window = await loadPage();
  });

  it("round-trips a simple name with a couple of learned spells", () => {
    const result = runInPage(
      window,
      function () {
        const ids = new Set([SPELLS[0].id, SPELLS[2].id]);
        const code = encodeCompact("Testname", ids);
        const decoded = decodeCompact(base64UrlToBytes(code.slice(4)));
        return {
          hasPrefix: code.startsWith("BLU:"),
          name: decoded.name,
          learned: Array.from(decoded.learned).sort((a, b) => a - b),
          expectedLearned: Array.from(ids).sort((a, b) => a - b),
        };
      }.toString(),
    );

    expect(result.hasPrefix).toBe(true);
    expect(result.name).toBe("Testname");
    expect(result.learned).toEqual(result.expectedLearned);
  });

  it.each([
    ["apostrophe", "Y'shtola Rhul"],
    ["umlauts", "Söldnerin Ärger"],
    ["japanese / multi-byte UTF-8", "プレイヤー名"],
    ["spaces", "Name With Spaces"],
  ])("preserves special characters in the character name (%s)", (_label, characterName) => {
    const result = runInPage(
      window,
      function () {
        const code = encodeCompact(CHARACTER_NAME, new Set());
        return decodeCompact(base64UrlToBytes(code.slice(4))).name;
      }
        .toString()
        .replace("CHARACTER_NAME", JSON.stringify(characterName)),
    );

    expect(result).toBe(characterName);
  });

  it("round-trips an empty learned-spells set", () => {
    const learnedSize = runInPage(
      window,
      function () {
        const code = encodeCompact("Niemand", new Set());
        return decodeCompact(base64UrlToBytes(code.slice(4))).learned.size;
      }.toString(),
    );

    expect(learnedSize).toBe(0);
  });

  it("round-trips every known spell learned", () => {
    const result = runInPage(
      window,
      function () {
        const allIds = new Set(SPELLS.map((s) => s.id));
        const code = encodeCompact("Alles", allIds);
        const decoded = decodeCompact(base64UrlToBytes(code.slice(4)));
        return { allCount: allIds.size, learnedCount: decoded.learned.size };
      }.toString(),
    );

    expect(result.learnedCount).toBe(result.allCount);
  });

  it("encodeCompact throws for a name longer than 255 UTF-8 bytes", () => {
    const threw = runInPage(
      window,
      function () {
        try {
          encodeCompact("a".repeat(300), new Set());
          return false;
        } catch {
          return true;
        }
      }.toString(),
    );

    expect(threw).toBe(true);
  });

  // --- Regressionstests für den unter TEST_REPORT.md §5.1 dokumentierten und hier behobenen Fund ---

  it("decodeCompact throws for a payload shorter than the minimum length (1 + BITMASK_BYTES)", () => {
    const result = runInPage(
      window,
      function () {
        // nameLen behauptet 0, aber es folgen gar keine 16 Bitmask-Bytes - insgesamt nur 5 Byte.
        const payload = new Uint8Array([0, 1, 2, 3, 4]);
        try {
          decodeCompact(payload);
          return { threw: false };
        } catch (err) {
          return { threw: true, message: err.message };
        }
      }.toString(),
    );

    expect(result.threw).toBe(true);
    expect(result.message).toMatch(/too short/i);
  });

  it("decodeCompact throws instead of silently truncating when nameLen exceeds the actual payload", () => {
    // Der konkrete Repro-Fall aus TEST_REPORT.md §5.1: nameLen behauptet 50 Byte, die Payload hat
    // aber nur 5 Byte insgesamt. VORHER: kein Fehler, sondern ein aus den Resten dekodierter
    // "Name" + leere learned-Menge. JETZT: klarer Fehler statt Datenmüll. (5 Byte liegen zwar schon
    // unter der absoluten Mindestlänge 1+BITMASK_BYTES=17 und lösen deshalb technisch bereits die
    // "zu kurz"-Prüfung aus statt der "unerwartete Länge"-Prüfung - für diesen konkreten Fund reicht
    // das: entscheidend ist NUR, dass irgendein klarer Fehler kommt, nicht welcher der beiden.)
    const result = runInPage(
      window,
      function () {
        const payload = new Uint8Array([50, 1, 2, 3, 4]);
        try {
          decodeCompact(payload);
          return { threw: false };
        } catch (err) {
          return { threw: true, message: err.message };
        }
      }.toString(),
    );

    expect(result.threw).toBe(true);
    expect(result.message).toMatch(/too short|unexpected length/i);
  });

  it("decodeCompact throws (unexpected length, not the too-short case) when nameLen mismatches an otherwise long-enough payload", () => {
    // Anders als der Fall oben: hier ist die Payload insgesamt lang genug (25 >= 17), aber nameLen
    // (3) passt nicht zur tatsächlichen Gesamtlänge (erwartet wären 1+3+16=20) - das MUSS die
    // zweite Prüfung (unerwartete Länge) auslösen, nicht die erste (zu kurz).
    const result = runInPage(
      window,
      function () {
        const payload = new Uint8Array(25);
        payload[0] = 3;
        try {
          decodeCompact(payload);
          return { threw: false };
        } catch (err) {
          return { threw: true, message: err.message };
        }
      }.toString(),
    );

    expect(result.threw).toBe(true);
    expect(result.message).toMatch(/unexpected length/i);
  });

  it("decodeCompact throws for a payload longer than nameLen + BITMASK_BYTES accounts for (trailing garbage)", () => {
    const result = runInPage(
      window,
      function () {
        // nameLen = 0, danach müssten exakt BITMASK_BYTES (16) Byte folgen - hier folgen 20.
        const payload = new Uint8Array(1 + 20);
        payload[0] = 0;
        try {
          decodeCompact(payload);
          return { threw: false };
        } catch (err) {
          return { threw: true, message: err.message };
        }
      }.toString(),
    );

    expect(result.threw).toBe(true);
    expect(result.message).toMatch(/unexpected length/i);
  });

  it("decodeCompact still accepts a well-formed payload of exactly the expected length", () => {
    // Kein Regressions-Fehlalarm: ein KORREKT langer Payload (wie ihn encodeCompact tatsächlich
    // erzeugt) darf durch die neue Längenprüfung nicht plötzlich abgelehnt werden.
    const result = runInPage(
      window,
      function () {
        const code = encodeCompact("Ok", new Set([SPELLS[0].id]));
        const decoded = decodeCompact(base64UrlToBytes(code.slice(4)));
        return decoded.name;
      }.toString(),
    );

    expect(result).toBe("Ok");
  });

  it("importCode() surfaces a clear, translated error message for a truncated BLU: code instead of importing garbage", () => {
    // End-to-End über importCode() (nicht nur die reine decodeCompact-Funktion) - bestätigt, dass
    // der geworfene Fehler tatsächlich bis zur UI durchgereicht wird (siehe dortiges try/catch mit
    // t().errImportFailed(err.message)), statt irgendwo verschluckt zu werden.
    const result = runInPage(
      window,
      function () {
        // 1 Null-Byte (nameLen = 0) gefolgt von nur 3 Bitmask-Bytes statt 16 - eindeutig zu kurz.
        const brokenPayload = new Uint8Array([0, 1, 2, 3]);
        const brokenCode = "BLU:" + bytesToBase64Url(brokenPayload);

        document.getElementById("importInput").value = brokenCode;
        return importCode().then(() => ({
          statusClass: document.getElementById("importStatus").className,
          statusText: document.getElementById("importStatus").textContent,
        }));
      }.toString(),
    );

    return result.then((r) => {
      expect(r.statusClass).toContain("error");
      expect(r.statusText.length).toBeGreaterThan(0);
    });
  });
});
