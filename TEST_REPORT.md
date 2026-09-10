# BLUnion – Testbericht

Datum: 2026-09-10 (Update 2026-09-10: beide unter §5 dokumentierten Findings wurden auf
ausdrücklichen Wunsch behoben, siehe dortige "Status"-Vermerke; der Web-Companion-Testharness aus
§4.1 wurde dabei zusätzlich ins Repo aufgenommen, siehe `docs/test/`. Weiteres Update 2026-09-10:
die neu ergänzte Gruppen-Funktion (mehrköpfige Gruppen-Listungen) im Web-Companion - Gegenstück zu
DrawGroupPublishSection/DrawGroupBrowseSection in UI/MainWindow.cs - ist jetzt ebenfalls per
`docs/test/groupsFeature.test.js` abgedeckt, siehe §4.6.)
Umfang: Dalamud-Plugin (C#), Cloudflare Worker (`worker/`), Web-Companion (`docs/index.html`).

**Wichtig:** Dieser Bericht enthielt ursprünglich ausschließlich Test-Infrastruktur (Testprojekt,
Testdateien, eine kleine, notwendige Compile-Ausschluss-Anpassung in `BLUnion.csproj`) und Befunde,
OHNE die gefundenen Business-Logik-Bugs eigenmächtig zu fixen. Die beiden Findings aus §5 wurden
zwischenzeitlich auf expliziten Auftrag hin behoben (siehe dortige Vermerke) – das war zum
ursprünglichen Erstellungszeitpunkt dieses Berichts bewusst NICHT der Fall.

---

## Inhalt

1. [Zusammenfassung](#1-zusammenfassung)
2. [C#/Dalamud-Plugin – `BLUnion.Tests`](#2-cdalamud-plugin--blunontests)
3. [Cloudflare Worker – `worker/test`](#3-cloudflare-worker--workertest)
4. [Web-Companion – `docs/index.html`](#4-web-companion--docsindexhtml)
5. [Gefundene Bugs / unerwartetes Verhalten](#5-gefundene-bugs--unerwartetes-verhalten)
6. [Nicht automatisiert testbare Lücken + Empfehlungen für manuelle Prüfung](#6-nicht-automatisiert-testbare-lücken--empfehlungen-für-manuelle-prüfung)
7. [Wie reproduzieren](#7-wie-reproduzieren)

---

## 1. Zusammenfassung

| Bereich | Framework | Testfälle | Ergebnis |
|---|---|---|---|
| C# Services (`SpellFilter`, `ComparisonService`, `SpellDataService`, `ManualCodeSyncProvider`) | xUnit (neues Projekt `BLUnion.Tests`) | **60** | **60 bestanden, 0 fehlgeschlagen** |
| Cloudflare Worker (`crypto.ts`, `index.ts`) | Vitest + `@cloudflare/vitest-plugin` (echter lokaler `workerd`) | **66** | **66 bestanden, 0 fehlgeschlagen** |
| Web-Companion (`docs/index.html`) | Vitest + jsdom, **committed** unter `docs/test/` (siehe §4.1) | **39** (Export/Import-Symmetrie + Gruppenvergleich + Gruppen-Feature + innerHTML/XSS-Regression) | **39 bestanden, 0 fehlgeschlagen** |

Zusätzlich (nicht committed, siehe §4.4) manuell gegen einen echten lokalen `wrangler dev`
verifiziert: der komplette Live-Sync-Publish/Update/Delete-Flow (6 Schritte, alle bestanden).

Ursprünglich gefunden wurden **zwei kleine, real existierende Verhaltens-Auffälligkeiten** (kein
Crash, kein Sicherheitsproblem) – siehe [§5](#5-gefundene-bugs--unerwartetes-verhalten) – **beide
mittlerweile behoben und per Regressionstest abgesichert**. Der bereits in einer früheren Session
behobene innerHTML/XSS-Bereich wurde erneut geprüft und ist weiterhin sauber (siehe
[§4.5](#45-innerhtmlxss-audit-re-verifiziert)).

---

## 2. C#/Dalamud-Plugin – `BLUnion.Tests`

### 2.1 Setup

Es gab noch kein Testprojekt – neu angelegt unter `BLUnion.Tests/` (eigenes Unterverzeichnis, damit
`dotnet build` im Repo-Root weiterhin eindeutig `BLUnion.csproj` findet, siehe fehlende `.sln`).

- **Framework:** xUnit (`Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`).
- **SDK:** Bewusst `Dalamud.NET.Sdk/15.0.0` (dieselbe wie `BLUnion.csproj`), NICHT
  `Microsoft.NET.Sdk` + reine `ProjectReference` – ein reines `ProjectReference` reicht nicht, weil
  `Dalamud.NET.Sdk` die Dalamud-Assemblies (`Dalamud`, `Serilog`, …) über
  `AssemblySearchPaths`/lokale `Reference`-Items aus der XIVLauncher-Installation aufzulöst, was
  NICHT transitiv über eine `ProjectReference` fließt (per Build-Test verifiziert: `CS0246` für
  `IPluginLog`/`Serilog.ILogger` ohne dies). `Use_DalamudPackager=false` gesetzt, da dieses Projekt
  kein Plugin ist und kein Manifest hat. `Reference Update="Dalamud"/"Serilog" Private="true"`
  überschreibt die SDK-Vorgabe `Private="false"` (dort bewusst so, damit das fertige *Plugin* diese
  DLLs nicht mitliefert) – ein Testlauf läuft aber nicht im Spielprozess und braucht die DLLs
  tatsächlich im Testausgabeverzeichnis.
- **`Fakes/TestPluginLog.cs`:** minimaler No-Op-Fake für `Dalamud.Plugin.Services.IPluginLog`. Alle
  Interface-Member sind in Dalamud `ABSTRACT` deklariert (kein Default-Interface-Method-Chaining,
  per Reflection gegen die installierte `Dalamud.dll` verifiziert) – müssen also alle implementiert
  werden; für die getesteten Services wird nur `Information`/`Warning`/`Error` tatsächlich
  aufgerufen, der Rest ist reiner Platzhalter.
- **Fixtures:**
  - `Fixtures/SpellData/*.json` – handgeschriebene MINI-Datensätze (4 Spells, 3 Monster, 2 Orte,
    6 Quellen) mit bewusst gewählten Grenzfällen: ein Spell ganz ohne Quelle, eine Quelle mit
    unbekannter `MonsterId`, ein Monster mit unbekannter `LocationId`, ein nur per Totem lernbarer
    Spell.
  - Zusätzlich eine Kopie der ECHTEN `Data/*.json` (`LinkBase="TestData"`) für einen
    Integrationstest, der die tatsächlich ausgelieferten Produktivdaten gegen das aktuelle Modell
    prüft (`SpellDataServiceTests.Load_RealShippedData_ParsesWithoutErrors`).
- Läuft **ohne laufendes FFXIV/Dalamud** – verifiziert über `dotnet test` in einer Umgebung ohne
  gestartetes Spiel.

**Notwendige Infrastruktur-Änderung** (keine Business-Logik): `BLUnion.csproj` bekam einen
`<Compile Remove="BLUnion.Tests\**\*.cs" />`-Eintrag. Ohne ihn zieht das SDK-Standard-Compile-Glob
(`**/*.cs`) die neuen Testdateien versehentlich in `BLUnion.csproj` selbst hinein (beobachtet als
`CS0246` für xUnit-Typen direkt in `BLUnion.csproj`, obwohl das gar kein xUnit referenziert). Ohne
diese Zeile lässt sich `BLUnion.Tests/` als Unterordner gar nicht anlegen.

### 2.2 Abgedeckte Services + Ergebnisse

Alle 60 Testfälle **bestanden** (verifiziert per `dotnet test` in Debug **und** Release-Konfiguration).

| Datei | Methoden | Abgedeckt |
|---|---|---|
| `SpellFilterTests.cs` | 15 (`[Fact]`/`[Theory]`, einige mit mehreren `InlineData`) | Namenssuche (Teilstring, case-insensitive), `#NNN`-Nummernsuche, reine Zahlensuche ohne `#`, leere/Whitespace-Eingabe, Trimmen, `#` ohne Ziffern danach (**Regressionstest für das behobene Finding**, siehe [§5.2](#52-spellfiltermatches-ein-alleinstehendes--matchte-alles-statt-nichts-behoben)), gemischt alphanumerisch, nicht-numerischer Rest nach `#`. |
| `ComparisonServiceTests.cs` | 11 | `GetCommonlyMissingSpells`: keine Spieler, keine bekannten Spells, Spell von allen gekannt (ausgeschlossen), einzelner/alle fehlend, überlappende Mengen + Sortierreihenfolge (absteigend nach Anzahl), Stabilität bei Gleichstand. `GroupMissingSpellsByMonster`: Gruppierung nach gemeinsamem Monster + Sortierreihenfolge, `excludeTotems`, Spell ohne Quelle (keine Gruppe), leere Eingabe. |
| `SpellDataServiceTests.cs` | 14 | `Load()` liest alle Dateien korrekt; `GetSourcesForSpell` für normalen Spell, **unbekannte SpellId** (leer statt Exception), **Spell ohne jede Quelle** (leer), **Quelle mit unbekannter MonsterId** (übersprungen statt Exception), **Monster mit unbekannter LocationId** (Location kommt als `null` zurück statt zu werfen), `excludeTotems`-Filter; `IsOnlyLearnableViaTotem` für reinen Totem-Spell/gemischte Quellen/keine Quelle/unbekannte ID; fehlende Datendatei (leere Liste + Warning statt Crash); kaputtes JSON (leere Liste + Error statt Crash); Integrationstest gegen die echten `Data/*.json`. |
| `ManualCodeSyncProviderTests.cs` (Bonus – siehe §2.3) | 10 | Encode→Import-Round-Trip (Name + gelernte Spells), Sonderzeichen im Namen (`Y'shtola Rhul`, Umlaute, Japanisch, Leerzeichen), zu langer Name (>255 UTF-8-Bytes → Exception), unbekanntes Code-Präfix, ungültiges Base64, importierte Spieler sind nie `IsLocalPlayer`, gleicher Name überschreibt statt zu duplizieren, `RemovePlayer` entfernt nur das Ziel, leere/vollständige Spell-Menge. |

### 2.3 Bonus: `ManualCodeSyncProvider`

Nicht explizit in der Aufgabenstellung genannt, aber ohne Mehraufwand isoliert testbar (hängt nur
von `SpellDataService` ab, für das bereits ein Fixture-Setup existierte) – deckt denselben
Encode/Decode-Symmetrie-Aspekt ab, der für die Web-Companion-Implementierung in §4 separat geprüft
wird. Beide Implementierungen müssen laut Projektdokumentation kompatibel bleiben ("keine
Parallelstruktur").

### 2.4 Nicht isoliert testbare Services (mit Begründung)

| Service | Warum nicht isoliert testbar |
|---|---|
| `LocalSpellUnlockService` | Hängt an `IDataManager.GetExcelSheet<AozAction>()` (Lumina-Excel-Sheet, nur mit echten Spieldaten befüllt), `IUnlockState.IsAozActionUnlocked(...)` (liest echten Spielzustand) und `IObjectTable.LocalPlayer` (lebendes Objekt aus dem Spielprozess). Diese Typen sind komplexe, von Dalamud/Lumina zur Laufzeit bereitgestellte Werttypen/Row-Refs, keine einfachen Interfaces – ein sinnvoller Fake würde faktisch eine Miniatur-Reimplementierung von Lumina-Excel-Sheets erfordern. |
| `PartyService` | Hängt an `IPartyList` (lebende Party-Enumeration) und ebenfalls `IObjectTable.LocalPlayer` – gleiches Problem: `RowRef<World>`, `ClassJob`, `Level` etc. sind aus Spielspeicher gelesene Live-Daten. |
| `LiveSyncService` | Hängt zusätzlich an einem **privaten, nicht injizierten** `HttpClient`-Feld (`private readonly HttpClient httpClient = new() {...}`) – ohne `HttpMessageHandler`-Injection-Seam lässt sich kein Netzwerk-Mock einsetzen, ohne den Produktivcode zu ändern (was laut Aufgabenstellung nicht eigenmächtig erfolgen soll). Die reine Business-Logik (`BuildTokenKey`, `DescribeHttpFailure`, die Count-Grenzen-Prüfungen in `PublishGroup`) ist zwar simpel genug, um testbar zu sein, ist aber `private static`/`private const` auf einer `sealed class` – ohne `InternalsVisibleTo` oder Reflection nicht von außen erreichbar. |

**Empfehlung, falls das künftig gewünscht ist** (nur als Hinweis, nicht umgesetzt): `HttpClient`
über den Konstruktor injizierbar machen (`HttpMessageHandler`-Parameter mit Default) würde
`LiveSyncService` ohne echtes Netzwerk testbar machen, exakt wie das Worker-Team über
`enforceWriteRateLimit`/`env` bereits für den Worker gelöst hat. Das wäre aber eine
Produktivcode-Änderung und liegt außerhalb des "nur Tests"-Auftrags dieser Runde.

---

## 3. Cloudflare Worker – `worker/test`

### 3.1 Setup

`package.json` hatte noch keine Test-Abhängigkeiten. Ergänzt:

- `vitest@^4.1.11`
- `@cloudflare/vitest-plugin@^1.1.6`
- `npm test` → `vitest run`

**Hindernis beim Setup (dokumentiert, da es beim nächsten `npm install` wieder auftreten könnte):**
Der ursprünglich naheliegende Ansatz (`@cloudflare/vitest-pool-workers` + `defineWorkersConfig` aus
`@cloudflare/vitest-pool-workers/config`, der in der Cloudflare-Doku/dem `wrangler`-Skill für
Vitest 3 beschrieben ist) funktioniert mit der aktuell installierten Vitest-Major-Version (4.x)
NICHT mehr – das Subpath-Export `./config` existiert in der installierten
`vitest-pool-workers`-Version nicht mehr, und `vitest-pool-workers` selbst verlangt als Peer
weiterhin Vitest 3. Der tatsächlich für Vitest 4 vorgesehene Nachfolger ist das neue Paket
`@cloudflare/vitest-plugin` mit `cloudflareTest(...)` als Vite-Plugin statt eines eigenen
Test-Pools (siehe `vitest.config.mts`, per Web-Recherche gegen die aktuelle Cloudflare-Doku
verifiziert). `vitest.config.ts` musste zudem in `vitest.config.mts` umbenannt werden, weil
`@cloudflare/vitest-plugin` ESM-only ist und Node/Vite die `.ts`-Datei sonst per CJS `require()`
zu laden versucht.

Läuft gegen einen **echten lokalen `workerd`-Prozess** (kein reines JS-Mock) inkl. echtem, lokal
simuliertem KV-Namespace und dem nativen Rate-Limiting-Binding `WRITE_RATE_LIMITER` – `wrangler.toml`
wird 1:1 als Konfiguration verwendet (`configPath: "./wrangler.toml"`), keine separate Testkonfig.
`npm install` verlangt zusätzlich `npm approve-scripts` für die native `workerd`-Binary (Postinstall-
Skript) – einmalig nötig, im Bericht dokumentiert statt stillschweigend automatisiert.

### 3.2 `crypto.ts` – 15 Tests, alle bestanden

`sha256Hex` (bekannter Referenzwert gegen `hello`/leerer String, deterministisch, unterschiedliche
Eingaben → unterschiedlicher Hash, 64-stelliger Hex-String), `generateEditToken` (nicht-leer, 20x
unterschiedlich, URL-safe ohne `+`/`/`/`=`, dekodiert zu genau 32 Byte), `base64UrlEncode`/
`base64UrlDecode` (Round-Trip beliebiger Bytes inkl. leer, Round-Trip der 16-Byte-Bitmaskengröße,
kein `+`/`/`/`=` im Ergebnis, wirft bei ungültiger Länge/ungültigen Zeichen statt eine falsche
Byte-Folge zurückzugeben).

### 3.3 `index.ts` – 51 Tests, alle bestanden

Alle Endpunkte, mit Fokus auf Fehlerfälle/Statuscodes wie gefordert (nicht nur Happy Path):

- **`handlePut`** (`PUT /profile/:world/:characterName`): neues Profil (201 + `editToken`), Update
  mit korrektem Token (200), Update mit **falschem** Token (**409**), Update **ohne** Token bei
  existierendem Profil (**409**), fehlende/zu kurze/ungültige `spellBitmaskBase64` (**400**),
  unbekannte World (**400**), kaputtes JSON (**400**), optionale Phase-2-Felder werden
  angenommen/zurückgegeben, ungültiger `availabilityTags`-Wert (**400**), `wantedPlayerCount`
  außerhalb 0–8 (**400**), zu lange `note` wird gekappt statt abgelehnt, ein im Body fehlendes
  optionales Feld lässt den bisherigen Wert unverändert (Phase-1-Kompatibilität).
- **`handleGet`**: 404 für nie veröffentlichten Charakter, 200 für existierenden, Groß-/
  Kleinschreibung wird ignoriert (`kvKey` lowercased).
- **`handleDelete`**: **403** ohne `X-Edit-Token`, **403** mit falschem Token, **404** für
  nicht-existentes Profil, 200 + tatsächliche Löschung (nachfolgendes GET → 404) mit korrektem
  Token.
- **`handleBrowse`**: **400** ohne `dataCenter`, nur `visibility: "listed"` erscheint,
  `dataCenter`/`visibility`/`editTokenHash` werden laut `stripForBrowseResponse` NICHT
  mitgeliefert, Profile eines anderen Data Centers erscheinen nicht.
- **`handleGroupPut`**: neue Gruppen-Listung (201 + `editToken`), leeres `members[]` (**400**), >8
  Mitglieder (**400**), unbekannte World in `members[]` (**400**), Update mit korrektem/falschem
  Token (200/**409**), `dataCenter` wird vom ERSTEN Mitglied hergeleitet, `targetSpellIds` wird
  akzeptiert und zurückgegeben, eine unbekannte Spell-ID in `targetSpellIds` (**400**), mehr als 30
  `targetSpellIds` (**400**), fehlendes `targetSpellIds` defaultet zu `[]`.
- **`handleGroupDelete`**: **403** ohne/mit falschem Token, **404** für nicht existente Gruppe,
  korrekte Löschung OHNE die referenzierten Mitglieder-Einzelprofile anzurühren (explizit
  verifiziert: Profil bleibt nach Gruppen-Löschung abrufbar).
- **`handleGroupsBrowse`**: **400** ohne `dataCenter`, nur `listed`-Gruppen, `spellBitmaskBase64`
  wird pro Mitglied live aus dessen Einzelprofil nachgeladen, fehlendes Mitgliedsprofil → `null`
  statt die ganze Gruppe zu verwerfen, `targetSpellIds` ist im Ergebnis enthalten.
- **CORS/Routing:** `OPTIONS` → 204 + CORS-Header, unbekannter Pfad → 404, falsche Methode → 405.
- **Rate-Limiting (`enforceWriteRateLimit`):** ohne `CF-Connecting-IP`-Header wird NICHT limitiert
  (so laufen alle obigen Tests unbehindert durch), **mit** festem `CF-Connecting-IP` schlägt die
  21. Anfrage derselben IP innerhalb der 20/60s-Grenze tatsächlich mit **429** fehl – bestätigt,
  dass das native Rate-Limiting-Binding auch lokal in `workerd` funktioniert, nicht nur auf der
  echten Cloudflare-Edge.

### 3.4 Beobachtung beim Testen (kein Bug, aber dokumentationswürdig)

`GET /profiles/browse` und `GET /groups/browse` sind serverseitig für `BROWSE_CACHE_TTL_SECONDS`
(20s) gecached (`withCache`, `caches.default`). Diese Cache-API-Instanz wird von
`@cloudflare/vitest-plugin` NICHT pro Test zurückgesetzt (anders als KV) – mehrere Tests, die
denselben `dataCenter` browsen, würden sonst den 20-Sekunden-Cache-Eintrag eines VORHERIGEN Tests
treffen und dessen (zu dem Zeitpunkt noch unvollständige) Ergebnisliste sehen. Das ist **kein
Worker-Bug**, sondern exakt das beabsichtigte Server-Cache-Verhalten – für die Tests wurde in
`test/helpers.ts` ein für den Handler bedeutungsloser `_cacheBust`-Query-Parameter ergänzt, der
NUR die Test-URLs eindeutig macht, ohne die Produktivlogik zu berühren.

---

## 4. Web-Companion – `docs/index.html`

### 4.1 Vorgehen

Die Aufgabenstellung verlangte für diesen Bereich ursprünglich ausdrücklich ein **"manuell/
funktional Durchgehen"**, nicht (wie bei C#/Worker) ein committtetes, dauerhaftes Testprojekt. Um
die Ergebnisse trotzdem auf echter Ausführung statt auf reiner Code-Lektüre zu stützen, wurde
zunächst ein kleines, **nicht committtetes** jsdom-basiertes Node-Skript verwendet.

**Update:** Auf ausdrücklichen Nachtrags-Auftrag ("ins Repo aufnehmen") wurde dieser Harness
überarbeitet, um einen Regressionstest für das behobene Finding §5.1 zu ergänzen, und **jetzt unter
`docs/test/` committtet** – analog zur Struktur von `worker/test/` (eigenes `package.json` in
`docs/`, `npm test` → `vitest run`, kein Cloudflare-spezifisches Tooling nötig, da hier nur
"normales" jsdom statt eines Workers simuliert wird). `docs/test/loadPage.js` lädt die ECHTE
`docs/index.html` per `jsdom` und führt ihre inline `<script>`-Tags aus; die Original-Funktionen
(`encodeCompact`/`decodeCompact`/`addOrUpdateGroupMember`/`renderGroupTable`/`renderGroup`/
`importCode`) werden direkt aufgerufen statt nachgebaut (siehe dortige Doku zu `window.eval()` –
nötig, weil `SPELLS`/`group`/etc. lexikalische Top-Level-Bindings des Original-`<script>`-Tags
sind, keine `window`-Properties).

Der in §4.4 beschriebene Live-Sync-Flow gegen einen echten lokalen `wrangler dev`-Prozess bleibt
**bewusst nicht committtet** (braucht einen separat gestarteten Server-Prozess, passt nicht zu
einem eigenständig lauffähigen `npm test`) – dafür war explizit nur der decodeCompact-Regressionstest
verlangt.

### 4.2 Export/Import-Symmetrie (`encodeCompact`/`decodeCompact`) – `docs/test/syncCode.test.js`, 14 Tests, alle bestanden

- Normaler Name + 2 gelernte Spells: Round-Trip korrekt.
- Sonderzeichen im Namen: `Y'shtola Rhul` (Apostroph), `Söldnerin Ärger` (Umlaute), `プレイヤー名`
  (japanisch/Mehrbyte-UTF-8), Leerzeichen – alle korrekt round-getrippt (bestätigt, dass die
  `TextEncoder`/`TextDecoder`-basierte UTF-8-Byte-Längen-Kodierung mit Mehrbyte-Zeichen umgehen
  kann, nicht nur mit ASCII).
  - Zur Einordnung: Die C#-Seite (`ManualCodeSyncProvider.ExportToCode`) verwendet exakt dasselbe
    Schema (1 Byte UTF-8-Länge + Name-Bytes + 16-Byte-Bitmaske) – ein mit dem Plugin exportierter
    Code lässt sich also auch über die Web-Companion importieren und umgekehrt, solange der Name
    ≤255 UTF-8-Bytes bleibt.
- Leere gelernte Spells / alle Spells gelernt: beide Extremfälle korrekt.
- Name >255 UTF-8-Bytes: `encodeCompact` wirft wie erwartet (`"Name too long."`).
- **Regressionstests für das behobene Finding §5.1** (`decodeCompact`-Längenvalidierung): zu kurze
  Payload wirft, `nameLen` größer als tatsächliche Payload wirft, Payload länger als
  `nameLen + BITMASK_BYTES` (Trailing-Garbage) wirft, eine korrekt lange Payload wird weiterhin
  akzeptiert (kein Fehlalarm), und End-to-End über `importCode()`: ein abgeschnittener `"BLU:"`-Code
  erzeugt eine sichtbare, übersetzte Fehlermeldung im Status-Element statt eines stillen
  Datenmüll-Imports.

### 4.3 Gruppenvergleich (`addOrUpdateGroupMember`, `renderGroupTable`, `renderGroup`) – `docs/test/groupComparison.test.js`, 7 Tests, alle bestanden

- Neues Mitglied wird hinzugefügt; gleicher Name **überschreibt** (kein Duplikat) mit aktualisierter
  `learned`-Menge; mehrere unterschiedliche Mitglieder bleiben getrennt.
- `renderGroupTable` erzeugt genau eine Zeile pro sichtbarem Spell mit korrekt berechneten
  `missing`/`known`-Namenslisten, und respektiert den Textfilter.
- HTML-Sonderzeichen in einem Mitgliedsnamen (`<img src=x onerror=...>`) werden in
  `renderGroupTable` UND in den Chips von `renderGroup` als reiner Text behandelt, kein zusätzliches
  `<img>`-Element entsteht, der `onerror`-Handler feuert nicht – siehe §4.5.

### 4.4 Live-Sync-Publish/Delete-Flow gegen echten lokalen Worker – 6 Schritte, alle bestanden

`wrangler dev` lokal gestartet (Port 18787, KV lokal simuliert), dann exakt die Request-Form
nachgebildet, die `gfPublishMyEntry()`/`gfDeleteMyEntry()` in `docs/index.html` tatsächlich senden
(Bitmaske via `buildBitmask`, PUT-Body inkl. `visibility`/`availabilityTags`/`note`/
`wantedPlayerCount`/`ttlHours`, DELETE mit `X-Edit-Token`-Header) – alle Werte über die ECHTEN
Seiten-Funktionen erzeugt, nicht neu nachgebaut:

1. `PUT` (Neuanlage) → 201 + `editToken`.
2. `GET` direkt danach → 200, Bitmaske/Note/Tags stimmen mit dem Gesendeten überein.
3. `PUT` (Update mit `editToken`) → 200, Note aktualisiert.
4. `PUT` mit **falschem** `editToken` → 409 (genau der Fall, den `gfErrPublishFailed` im UI anzeigen
   würde).
5. `DELETE` mit korrektem Token → 200.
6. `GET` danach → 404.

### 4.5 innerHTML/XSS-Audit (re-verifiziert)

In einer früheren Session wurden mehrere Stellen identifiziert und gefixt, an denen Server-/
Fremddaten (Charakternamen, Notizen aus dem Gruppenfinder-Browse, Gruppenmitglieder-Namen aus
Import/Browse) ungeprüft per `innerHTML` gerendert wurden (`escapeHtml()`-Helper eingeführt bzw.
`createElement`/`textContent` statt `innerHTML` für die Gruppenmitglieder-Chips). Dieser Bereich
wurde für diesen Testdurchgang **erneut geprüft**:

- Statische Re-Prüfung aller verbleibenden `innerHTML`-Zuweisungen in der Datei: alle verbleibenden
  Stellen sind entweder reine Leerungen (`= ''`), verwenden ausschließlich interne/statische Daten
  (`SPELLS`-Konstante, `I18N`-Übersetzungen), oder laufen bereits durch `escapeHtml()`.
- Zusätzlich **funktional** bestätigt (siehe §4.3): ein böswilliger Mitgliedsname mit eingebettetem
  `<img onerror=...>` erzeugt weder in der Tabellen- noch in der Chip-Darstellung ein ausführbares
  Element.

**Keine neuen Stellen gefunden.** Der Bereich ist nach aktuellem Stand sauber.

### 4.6 Gruppen-Feature (mehrköpfige Gruppen-Listungen) – `docs/test/groupsFeature.test.js`, 18 Tests, alle bestanden

Neu ergänzt: das Gegenstück zu `DrawGroupPublishSection`/`DrawGroupBrowseSection` in
`UI/MainWindow.cs` im Web-Companion (`handleGroupPut`/`handleGroupsBrowse` in
`worker/src/index.ts`), angepasst an den Browser-Kontext (Mitglieder über eingefügte Sync-Codes
statt Party-/Live-Sync-Datenquelle). PUT/DELETE laufen gegen ein gemocktes `window.fetch`
(bewusst kein Test-Abhängigkeit auf einen laufenden `wrangler dev`-Prozess, siehe §4.1) - zusätzlich
per Ad-hoc-Skript (nicht committed, analog zu §4.4) einmal gegen einen ECHTEN lokalen `wrangler dev`
end-to-end verifiziert: 2 Mitglieder per Code hinzufügen → veröffentlichen (inkl. `targetSpellIds`)
→ in `GET /groups/browse` wiederfinden → aktualisieren (reused `editToken`) → löschen, alle 5
Schritte bestanden.

- **Mitglied per Code hinzufügen/entfernen:** gültiger Code+World, fehlende World (Fehler),
  fehlendes `BLU:`-Präfix (Fehler), abgeschnittener/kaputter Code (Fehler dank der in §5.1
  behobenen `decodeCompact`-Längenvalidierung - Regressionstest über den NEUEN Aufrufpfad
  `groupsAddMemberFromCode`), gleiches Mitglied zweimal hinzugefügt aktualisiert statt zu
  duplizieren, Entfernen aktualisiert die Chip-Liste, leere Liste zeigt den Hinweistext.
- **Veröffentlichen-Flow:** PUT-Body-Form (members/visibility/availabilityTags/note/
  wantedPlayerCount/targetSpellIds) verifiziert, `editToken` aus der Response wird unter dem neuen,
  eigenen `localStorage`-Schema (`blunion_livesync_owngroup`, siehe Aufgabenstellung "eigenes
  Storage-Key-Schema") gespeichert und bei einem erneuten Publish als Update wiederverwendet
  (inkl. Wiederverwendung der lokal generierten `groupId` - NICHT eines Response-Felds, siehe
  Testkommentar), 0 Mitglieder verhindert den Request komplett, ein abgelehntes PUT (400) zeigt die
  Fehlermeldung an und speichert kein Token.
- **Löschen-Flow:** DELETE mit dem gespeicherten Token + `X-Edit-Token`-Header, Token wird danach
  entfernt und der Löschen-Button wieder deaktiviert; ohne gespeicherte Gruppe passiert nichts (kein
  Request).
- **Browse-Rendering:** Kartenaufbau (Mitgliederzeilen mit Name/World/gelernte Spell-Anzahl, Tags,
  Notiz, aufgelöste `targetSpellIds`-Namen), leere Ergebnisliste zeigt den Hinweistext, der "Zum
  Vergleich hinzufügen"-Button pro Mitglied nutzt nachweislich dieselbe `addOrUpdateGroupMember()`-
  Logik wie die bestehende Einzelspieler-Suche.
- **Escaping-Regressionstest** (analog zu `groupComparison.test.js`): ein `<img onerror=...>`-
  Payload in Mitgliedsname/-world/Notiz erzeugt kein ausführbares `<img>`-Element und der
  `onerror`-Handler feuert nicht - Mitgliedername/-world laufen dabei sogar über `textContent`
  statt `escapeHtml()+innerHTML` (noch eine Stufe direkter sicher als im Vorbild
  `gfRenderBrowseResults()`, weil für die Buttons ohnehin schon `createElement` gebraucht wird).

---

## 5. Gefundene Bugs / unerwartetes Verhalten

### 5.1 `decodeCompact` (docs/index.html) validierte die Payload-Länge nicht — **BEHOBEN**

- **Status:** ✅ Behoben (auf expliziten Nachtrags-Auftrag). Regressionstests in
  `docs/test/syncCode.test.js` (siehe §4.2).
- **Datei:** `docs/index.html`, Funktion `decodeCompact` (aktuell Zeile ~1093–1108).
- **Schweregrad:** Niedrig (kein Absturz, keine Sicherheitslücke – aber inkonsistentes Verhalten
  ggü. dem C#-Client).
- **Beobachtung (ursprünglich):** `decodeCompact` las `nameLen = payload[0]` und schnitt dann
  `payload.slice(1, 1 + nameLen)` heraus, OHNE zu prüfen, ob die Payload überhaupt lang genug ist.
  `Uint8Array.slice()` kürzt bei Bedarf stillschweigend, `TextDecoder.decode()` dekodierte den
  (zu kurzen) Rest klaglos.
- **Repro (ursprünglich):** `decodeCompact(new Uint8Array([50, 1, 2, 3, 4]))` (behauptet 50
  Namens-Bytes, liefert aber nur 4) warf **keinen** Fehler, sondern lieferte einen aus den Bytes
  `1,2,3,4` dekodierten "Namen" (Steuerzeichen) und eine leere `learned`-Menge – live verifiziert.
  Praktisch entsprach das einem Nutzer, der einen abgeschnittenen/kaputten `"BLU:"`-Code einfügt:
  statt einer klaren Fehlermeldung ("Code ungültig") erschien stillschweigend ein Import mit
  falschem/leerem Namen und falscher Spell-Auswahl.
- **Vergleich:** Die C#-Entsprechung `ManualCodeSyncProvider.DecodeCurrentFormat` prüft explizit
  `payload.Length != expectedLength` und wirft eine `FormatException` mit klarer Meldung.
- **Fix:** `decodeCompact` prüft jetzt VOR dem Slicing zwei Dinge, analog zur C#-Seite: (1)
  `payload.length < 1 + BITMASK_BYTES` → wirft ("Sync code is too short (…)."), (2) die aus
  `nameLen` hergeleitete `expectedLength` (`1 + nameLen + BITMASK_BYTES`) stimmt nicht mit der
  tatsächlichen Payload-Länge überein → wirft ("Sync code has unexpected length (…)."). Neue
  Konstante `BITMASK_BYTES = 16` ersetzt die bisher an drei Stellen wiederholte Magic Number `16`
  (`buildBitmask`, `encodeCompact`, `decodeCompact`). Der geworfene `Error` wird von den
  bestehenden Aufrufstellen bereits sauber behandelt: `importCode()` zeigt `err.message` über
  `t().errImportFailed(...)` als übersetzte Fehlermeldung im Status-Element an,
  `gfTryDecodeMyCode()` fängt ihn ab und liefert (wie zuvor bei jedem anderen ungültigen Code) `null`
  zurück, was die Publish/Delete-Buttons im Gruppenfinder-Tab korrekt deaktiviert hält, statt
  Datenmüll an den Worker zu senden. Fehlermeldungstext bewusst auf Englisch gehalten, analog zur
  bereits bestehenden Konvention in `encodeCompact` (`"Name too long."`) – der Wurf-Text selbst wird
  in dieser Datei nirgends lokalisiert, nur über die `t().err*(err.message)`-Templates eingebettet.

### 5.2 `SpellFilter.Matches`: ein alleinstehendes `"#"` matchte ALLES statt NICHTS — **BEHOBEN**

- **Status:** ✅ Behoben (auf expliziten Nachtrags-Auftrag). Regressionstest in
  `BLUnion.Tests/SpellFilterTests.cs` (`HashOnly_NoDigitsAfter_MatchesNothing`, jetzt `[Theory]` mit
  zwei Fällen inkl. Whitespace-Variante `"  #  "`).
- **Datei:** `Services/SpellFilter.cs` (`Matches`). Die JS-Filterlogik in `docs/index.html`
  (`applyFilter`/`renderGroupTable`-Filterung) wurde bei dieser Gelegenheit gegengeprüft: sie ist
  **unabhängig implementiert** (regex `^#?(\d+)$`, verlangt mindestens eine Ziffer für den
  Nummernpfad) und hatte den Fehler NICHT – ein alleinstehendes `"#"` matcht dort bereits korrekt
  nur Spells, deren Name buchstäblich ein `#`-Zeichen enthält (regulärer Teilstring-Fall), nicht
  "alles". Kein Fix dort nötig.
- **Schweregrad:** Sehr niedrig, rein kosmetisch/UX.
- **Beobachtung (ursprünglich):** Wurde nur `"#"` (ohne folgende Ziffern) eingegeben, war
  `candidate` nach dem Entfernen des `#` ein leerer String. `candidate.Length > 0` war dann
  `false`, der Code fiel auf den Namensvergleich `spellName.Contains(candidate, ...)` zurück – und
  `string.Contains("")` ist in .NET für JEDEN String `true`. Ergebnis: statt "keine Treffer" oder
  einer Aufforderung, Ziffern einzugeben, wurden ALLE Spells angezeigt.
- **Repro (ursprünglich):** `SpellFilter.Matches("Fireball", 1, "#")` → `true`.
- **Fix:** Ein neuer, expliziter Zwischenschritt behandelt "`#` ohne folgende Ziffern" als eigenen
  Fall und gibt direkt `false` zurück, BEVOR der Code auf den Namensvergleich zurückfallen kann –
  der eigentliche Namenspfad (leerer Filtertext, normale Wort-/Zahlensuche) bleibt unverändert.
- **Einordnung:** Kein Crash, keine Datenkorruption – nur potenziell verwirrend für einen Nutzer,
  der `#` tippt und noch keine Ziffer folgen lässt (kurzzeitig während der Eingabe).

Beide Punkte wurden ursprünglich bewusst **nicht** gefixt (siehe Aufgabenstellung der ersten
Testrunde) und sind jetzt, auf separaten expliziten Auftrag hin, behoben – siehe die
"Fix"-Abschnitte oben sowie §2.2/§4.2 für die zugehörigen (Regressions-)Tests.

---

## 6. Nicht automatisiert testbare Lücken + Empfehlungen für manuelle Prüfung

| Lücke | Warum nicht automatisiert testbar | Empfehlung für manuelle Prüfung im Spiel |
|---|---|---|
| Echtes ImGui-Rendering (Tab-Wechsel, `DrawCardGrid`-Spaltenberechnung bei unterschiedlichen Fensterbreiten, Popup-Öffnen/-Schließen für `DrawGroupTargetSpellDetailPopup`, ScrollY-Verhalten der Vergleichs-/Spellbook-Tabelle) | Braucht einen echten ImGui-Kontext (Frame-Loop, Eingabe-Events, tatsächliches GPU-Rendering) – dafür gibt es keine headless-Testinfrastruktur im Projekt. | Plugin im Spiel laden, Fenster auf verschiedene Breiten ziehen (Master-Detail-Umschaltpunkt bei `TwoColumnLayoutMinWidth`=600px prüfen), alle Tabs durchklicken, Gruppenfinder-Karten mit und ohne Ziel-Spells anklicken, Popup öffnen/schließen, mit gedrückter Maustaste durch die gescrollten Tabellen scrollen. |
| `LocalSpellUnlockService.GetLearnedSpellIds()` gegen echte `AozAction`-Unlock-Daten | Erfordert einen eingeloggten Charakter mit echtem `IUnlockState`; Lumina-Excel-Sheets sind nicht sinnvoll fakebar (siehe §2.4). | Mit einem Charakter mit bekannter Anzahl gelernter Blue-Mage-Spells einloggen, "Bestimmen & Exportieren" nutzen, Anzahl mit dem tatsächlichen In-Game-Spellbook (Blue Magic Spellbook) abgleichen. Insbesondere neu gelernte Spells sofort nach dem Erlernen prüfen (Cache-Verzögerung?). |
| `PartyService` gegen echte Party-Mitglieder | Erfordert eine echte laufende Party mit mind. einem weiteren Blue Mage. | Mit 2+ Spielern (davon mind. 1 Blue Mage) eine Party bilden, prüfen dass `GetBlueMagePartyMembers()` korrekt nur Blue Mages listet (ClassJobId 36), World-Namen korrekt aufgelöst werden, und dass ein Soloed-Charakter (keine Party) trotzdem als "Party von 1" (sich selbst) erkannt wird. |
| `LiveSyncService.Tick()`-Polling-Timing unter echten Netzwerkbedingungen (`PartyPollInterval`=60s, `LocalLearnedSpellCheckInterval`=5s) | Zeitbasiertes Verhalten über Minuten, plus echte Cloudflare-Edge-Latenz – im Unit-Test nur mit `HttpMessageHandler`-Injection sinnvoll simulierbar (siehe §2.4-Empfehlung). | Mit Live-Sync aktiviert einige Minuten im Spiel bleiben, `/xllog` (Dalamud-Log) auf wiederholte Push/Fetch-Zyklen prüfen, während ein zweiter Spieler (oder ein zweites Testkonto) gleichzeitig Spells lernt – prüfen, wie schnell der aktualisierte Status beim anderen Client ankommt. |
| Reale Multi-Client-Konkurrenz (zwei echte Spieler/Browser-Tabs gleichzeitig gegen denselben Worker-Endpoint) | Die Worker-Tests laufen sequenziell in einem einzelnen `workerd`-Prozess; echte Wettlaufsituationen (zwei gleichzeitige `PUT`s auf denselben `editToken`) sind damit nicht abgedeckt. | Zwei Browser-Tabs/zwei Plugin-Instanzen dasselbe Profil kurz hintereinander aktualisieren lassen und beobachten, ob KV "Last-Write-Wins" wie erwartet greift (keine Korruption, kein doppelter Edit-Token). |
| Rate-Limiting auf der ECHTEN Cloudflare-Edge (Produktion) | Das native Rate-Limiting-Binding ist laut Cloudflare-Doku selbst "permissive, eventually consistent" und pro Edge-Standort separat gezählt – lokal (`workerd`, ein Prozess) lässt sich nur der Code-Pfad, nicht das verteilte Produktionsverhalten testen. | Nach einem echten Deploy (`npm run deploy`) mit einem einfachen Skript (z.B. `curl`-Schleife) verifizieren, dass wiederholte PUTs von derselben IP tatsächlich irgendwann 429 liefern, OHNE dabei echte Nutzerprofile zu fluten – idealerweise gegen eine Wegwerf-`characterName`. |
| Browser-Kompatibilität der Web-Companion (`CompressionStream`/`DecompressionStream` für das Legacy-`BLU1:`-Format, `navigator.clipboard`) | Die jsdom-Umgebung dieser Session unterstützt/emuliert nicht notwendigerweise alle Browser-APIs identisch zu echten Browsern (insbesondere Safari ist bei `CompressionStream` historisch nachgezogen). | In mind. Chrome/Firefox/Safari (Desktop, wenn möglich auch mobil) tatsächlich einen Export erzeugen, "In Zwischenablage kopieren" nutzen und einen alten `"BLU1:"`-Code (falls noch vorhanden) importieren. |
| Visuelle/CSS-Korrektheit (Dark/Light-Theme-Aspekte, Responsive-Verhalten der Web-Companion unterhalb bestimmter Breiten) | Reine Optik, keine Logik – jsdom rendert kein Layout. | Seite im Browser bei verschiedenen Fensterbreiten (inkl. Mobile-Viewport) öffnen, insbesondere die Gruppentabelle und die Gruppenfinder-Karten. |

---

## 7. Wie reproduzieren

```bash
# C#-Tests (keine laufende FFXIV-Instanz nötig)
cd BLUnion.Tests
dotnet test              # Debug
dotnet test -c Release    # Release

# Worker-Tests (kein Cloudflare-Account nötig, läuft komplett lokal)
cd worker
npm install
npm test

# Worker-Typprüfung (unverändert)
npm run typecheck

# Web-Companion-Tests (kein Browser nötig, läuft komplett lokal über jsdom)
cd docs
npm install
npm test
```

Der Live-Sync-Publish/Delete-Flow aus §4.4 (gegen einen echten lokalen `wrangler dev`) ist weiterhin
NICHT Teil von `npm test` in `docs/` (siehe Begründung in §4.1) – bei Bedarf kann ich das
zugehörige Ad-hoc-Skript erneut erzeugen und/oder ebenfalls committen.
