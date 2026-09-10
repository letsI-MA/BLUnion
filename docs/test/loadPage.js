import { JSDOM } from "jsdom";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const INDEX_HTML_PATH = path.join(__dirname, "..", "index.html");

/**
 * Lädt die ECHTE docs/index.html in jsdom und führt ihre inline <script>-Tags aus - Tests rufen
 * damit die tatsächlichen Seiten-Funktionen auf (encodeCompact/decodeCompact/addOrUpdateGroupMember/
 * renderGroupTable/...), statt sie nachzubauen. Siehe ../../TEST_REPORT.md §4.1 für die Begründung,
 * warum das (statt reiner Code-Lektüre) für den Web-Companion-Teil verwendet wird.
 */
export async function loadPage() {
  const html = fs.readFileSync(INDEX_HTML_PATH, "utf8");
  const dom = new JSDOM(html, {
    url: "https://letsi-ma.github.io/BLUnion/",
    runScripts: "dangerously",
    resources: "usable",
    pretendToBeVisual: true,
  });

  // Kurze Wartezeit, bis alle inline <script>-Tags durchgelaufen sind - dieselbe Wartezeit, die
  // sich beim Ad-hoc-Prototyp dieser Suite als ausreichend erwiesen hat.
  await new Promise((resolve) => setTimeout(resolve, 300));

  return dom.window;
}

/**
 * Führt `fnSource` (Quelltext einer parameterlosen Funktion, siehe Aufrufer) über window.eval() im
 * lexikalischen Scope der Seite aus und liefert das Ergebnis zurück.
 *
 * WICHTIG, warum das über window.eval(String) statt eines normalen JS-Callbacks läuft: SPELLS/
 * group/encodeCompact/decodeCompact/... sind top-level "const"/"let" bzw. Funktionsdeklarationen im
 * Original-<script>-Tag der Seite - das sind lexikalische Bindings im Global Environment Record des
 * jsdom-Realms, KEINE Properties auf window. Von AUSSEN (aus einem normalen Node-Testmodul) ist
 * "window.SPELLS" deshalb undefined (per Ad-hoc-Prototyp verifiziert), obwohl "SPELLS" innerhalb
 * von WEITEREM, im SELBEN Realm über window.eval() ausgeführten Code ganz normal sichtbar ist -
 * beides teilt sich dieselbe äußere lexikalische Umgebung, genau wie zwei <script>-Tags derselben
 * echten Seite das auch täten. Ein aus dem Testmodul übergebenes Closure hätte diesen Zugriff
 * NICHT (es liefe im Node-Realm, nicht im jsdom-Realm) - deshalb der Umweg über einen String.
 */
export function runInPage(window, fnSource) {
  return window.eval(`(${fnSource})()`);
}
