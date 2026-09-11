# Discord-Integration (Phase 1 rein lesend + Phase 1.5 persistente Gruppen-Karten)

`/blunion browse` ist ein Discord-Slash-Command, der öffentlich gelistete (`visibility: "listed"`)
Blue-Mage-Gruppen direkt in Discord anzeigt - als Embed, mit einem Link-Button zur bestehenden
[Web-Companion-Seite](https://letsi-ma.github.io/BLUnion/). Er reicht dafür **dieselbe Logik**
durch, die bereits `GET /groups/browse` benutzt (siehe `computeGroupsBrowse` in
`worker/src/index.ts`) - keine neue KV-Struktur, kein zweites Backend.

**Bewusst NICHT Teil von Phase 1** (siehe Aufgabenstellung): Discord-Account-Linking
(`/blunion link`), und jede Art von Schreiboperation (Gruppe erstellen/beitreten/verlassen/
bearbeiten/canceln) über Discord. `/blunion browse` ist rein lesend.

**Phase 1.5** (siehe [Abschnitt weiter unten](#phase-15---persistente-gruppen-karten-pro-region))
ergänzt das um automatische, persistente Karten pro Gruppe in vier festen Discord-Kanälen (eine
pro FFXIV-Region) - weiterhin keine Schreiboperation, die vom Discord-Nutzer selbst ausgeht, nur
eine automatische Spiegelung dessen, was übers Plugin/die Website bereits veröffentlicht wurde.

## 1. Was wurde geändert/hinzugefügt

| Datei | Änderung | Warum |
|---|---|---|
| `worker/src/index.ts` | Neue Route `POST /discord/interactions` + `verifyDiscordSignature`, `handleDiscordInteractions`, `handleDiscordApplicationCommand`, `handleDiscordBrowse`, `buildGroupsBrowseEmbed`/`buildGroupEmbedField`, Discord-Typen/-Konstanten. `handleGroupsBrowse` minimal in `computeGroupsBrowse` (Kernlogik) + dünnen HTTP-Wrapper aufgeteilt. `Env` um `DISCORD_PUBLIC_KEY` erweitert. | Neue Route im bestehenden Router-Pattern; `computeGroupsBrowse`-Extraktion ermöglicht Wiederverwendung durch den Discord-Handler, ohne die KV-Iterations-/Filterlogik zu duplizieren. **Verhalten von `GET /groups/browse` unverändert** (siehe `worker/test/index.test.ts`, alle bestehenden Tests laufen unverändert grün). |
| `worker/src/crypto.ts` | Neue Funktion `hexToBytes`. | Zum Dekodieren von `DISCORD_PUBLIC_KEY` und der `X-Signature-Ed25519`-Headerwerte in rohe Bytes für `crypto.subtle.importKey`/`verify`. |
| `worker/wrangler.toml` | Kommentar zu `DISCORD_PUBLIC_KEY` (kein neuer Eintrag - siehe unten). | Dokumentiert, wie/wo das Secret gesetzt wird, ohne es im Klartext einzutragen. |
| `worker/.dev.vars.example` (neu, committed) | Vorlage für lokale Secrets. | Lokale Entwicklung/Tests brauchen einen `DISCORD_PUBLIC_KEY`-Wert, ohne echte Secrets zu committen. |
| `worker/.dev.vars` (neu, **NICHT** committed, siehe `worker/.gitignore`) | Enthält ein extra für dieses Repo generiertes Test-Keypair (Public Key hier, privater Schlüssel nur in `worker/test/discord.test.ts`). | Ermöglicht `npm test`, ohne einen echten Discord-Public-Key zu brauchen. **Schützt nichts in Produktion.** |
| `worker/test/discord.test.ts` (neu) | Tests für Signaturverifikation (gültig/fehlend/manipuliert), PING, `/blunion browse` (mit/ohne/leerem Datacenter, `listed` vs. `unlisted`, kein `editToken` im Embed). | Siehe Abschnitt "Testschritte" unten. |
| `worker/scripts/register-discord-commands.mjs` (neu) | Node-Skript, registriert `/blunion browse` als Guild-Command. | Siehe Abschnitt 4 unten - läuft lokal, ist kein Teil des deployten Workers. |
| `worker/README.md`, `README.md` | Je ein Verweis auf diese Datei/die neue Route in der Endpunkt-Tabelle. | Auffindbarkeit. |

Keine bestehenden Response-Shapes, KV-Keys oder Datenstrukturen wurden verändert.

## 2. Neues Secret in Cloudflare setzen

Das einzige neue Geheimnis ist der **Ed25519-Public-Key deiner Discord-Application** (zu finden im
[Discord Developer Portal](https://discord.com/developers/applications) unter deiner App ->
"General Information" -> "Public Key"). Er heißt technisch kein "Secret" (er ist ja öffentlich),
wird hier aber trotzdem wie eines behandelt (nie im Code/in `wrangler.toml`), weil er
projektspezifisch ist:

```bash
cd worker
wrangler secret put DISCORD_PUBLIC_KEY
# Fragt interaktiv nach dem Wert - den Public Key aus dem Discord Developer Portal einfügen.
```

Für lokale Entwicklung/Tests (`wrangler dev`, `npm test`) brauchst du stattdessen eine lokale
`.dev.vars`-Datei (wird nie committed):

```bash
cd worker
cp .dev.vars.example .dev.vars
# .dev.vars öffnen und DISCORD_PUBLIC_KEY=<dein Public Key> eintragen
```

Ein per `wrangler secret put` gesetzter Wert überschreibt beim Deploy automatisch einen
gleichnamigen Eintrag aus `.dev.vars`/`wrangler.toml` - das ist das von Cloudflare vorgesehene
Muster für "Dev-Default lokal, echtes Secret in Produktion".

## 3. Interactions Endpoint URL im Discord Developer Portal eintragen

1. [Discord Developer Portal](https://discord.com/developers/applications) -> deine Application
   (oder eine neue anlegen).
2. Unter "General Information" den Public Key kopieren und wie in Abschnitt 2 als
   `DISCORD_PUBLIC_KEY`-Secret setzen - **und den Worker deployen** (`wrangler deploy` in
   `worker/`), BEVOR du die Endpoint-URL einträgst, da Discord die URL sofort mit einer
   `PING`-Interaction gegenprüft (siehe `handleDiscordInteractions`/`DISCORD_INTERACTION_TYPE_PING`
   in `worker/src/index.ts`) und ohne gültigen Public Key jede Prüfung mit 401 fehlschlägt.
3. Dort das Feld "Interactions Endpoint URL" setzen auf:
   ```
   https://blunion-livesync.skysurfer101.workers.dev/discord/interactions
   ```
   (die Basis-URL ist dieselbe wie `WORKER_BASE_URL` in `docs/index.html` - bei einem eigenen
   Deploy unter anderem Namen/eigener Subdomain entsprechend anpassen.)
4. Speichern - Discord schickt daraufhin sofort eine `PING`-Interaction; bei korrektem Setup
   antwortet der Worker mit `{ "type": 1 }` und Discord akzeptiert die URL.

## 4. `/blunion` als Guild-Command im Testserver registrieren

Bewusst (noch) **kein globaler Command** - nur für einen einzelnen Testserver, per Skript:

```bash
# In Discord: Einstellungen -> Erweitert -> Entwicklermodus aktivieren, dann Rechtsklick auf den
# Testserver -> "ID kopieren" für DISCORD_TEST_GUILD_ID.
# DISCORD_APPLICATION_ID und DISCORD_BOT_TOKEN stehen im Discord Developer Portal (General
# Information bzw. Bot-Tab) deiner Application.

cd worker
DISCORD_APPLICATION_ID=... DISCORD_BOT_TOKEN=... DISCORD_TEST_GUILD_ID=... \
  node scripts/register-discord-commands.mjs
```

(PowerShell: `$env:DISCORD_APPLICATION_ID = "..."` usw. vor dem `node`-Aufruf, siehe Kommentar am
Kopf von `register-discord-commands.mjs`.)

Der Bot braucht außerdem eine Einladung in den Testserver mit dem `applications.commands`-Scope
(OAuth2 -> URL Generator im Portal, Scope `applications.commands` ankreuzen, Link öffnen).

Guild-Commands sind sofort nutzbar (anders als globale Commands, die bis zu einer Stunde zur
Propagierung brauchen) - direkt danach sollte `/blunion browse` im Testserver erscheinen.

## 5. Testschritte

Automatisiert (siehe `worker/test/discord.test.ts`, deckt a/b/c ab):

```bash
cd worker
npm test
```

Manuell:

**a) Signaturverifikation**

- Gültige Signatur (z.B. über den offiziellen ["Example App"-Testscript von
  Discord](https://discord.com/developers/docs/interactions/overview#example-app) oder einfach
  live über Discord selbst, siehe unten) -> `200`.
- Fehlende/falsche Signatur, z.B.:
  ```bash
  curl -i -X POST https://<deine-worker-url>/discord/interactions \
    -H "Content-Type: application/json" \
    -d '{"type":1}'
  ```
  -> `401`, ohne dass der Body inhaltlich verarbeitet wurde.

**b) `/blunion browse` mit/ohne Datacenter-Filter**

- Im Testserver `/blunion browse` OHNE `datacenter`-Option ausführen -> Chat-Antwort mit Hinweis,
  ein Data Center anzugeben (kein Embed).
- `/blunion browse datacenter:Aether` (oder ein DC, auf dem eine `listed`-Gruppe existiert, z.B.
  vorher übers Plugin oder die Website veröffentlicht) -> Embed mit Notiz/Mitgliederliste/
  Verfügbarkeit/Mitgliederzahl sowie einem "Companion-Website öffnen"-Button.

**c) Leeres Ergebnis**

- `/blunion browse datacenter:<DC ohne gelistete Gruppen>` -> klare Chat-Antwort ("Keine
  öffentlich gelisteten Gruppen auf ... gefunden."), kein leeres Embed.

**d) Bestehende Endpunkte weiterhin unverändert**

```bash
cd worker
npm test        # alle bestehenden Tests in index.test.ts müssen weiterhin grün sein
```
Zusätzlich manuell z.B.:
```bash
curl "https://<deine-worker-url>/groups/browse?dataCenter=Aether"
curl "https://<deine-worker-url>/profile/Gilgamesh/Irgendwer"
```
sollten sich exakt wie vor dieser Änderung verhalten (unverändertes Response-Shape, unveränderte
Statuscodes).

---

## Phase 1.5 - Persistente Gruppen-Karten pro Region

Jede öffentlich gelistete (`visibility: "listed"`) Gruppe bekommt automatisch eine **persistente
Karte** (Embed) in einem von vier festen Discord-Kanälen - einem **pro FFXIV-Region**, nicht pro
Data Center:

| Region | Data Center |
|---|---|
| North America (`DISCORD_WEBHOOK_NA`) | Aether, Crystal, Dynamis, Primal |
| Europe (`DISCORD_WEBHOOK_EU`) | Chaos, Light |
| Japan (`DISCORD_WEBHOOK_JP`) | Elemental, Gaia, Mana, Meteor |
| Oceania (`DISCORD_WEBHOOK_OC`) | Materia |

(11 Data Center insgesamt - bewusst ohne "Shadow": ein 2024 eingeführtes, im selben Jahr wieder
geschlossenes temporäres EU-Überlauf-Data-Center, das `lookupDataCenter`/`worlds.ts` nie kannte und
nie zurückliefern wird.)

**Warum Region statt Data Center**: Innerhalb einer Region kann man per "World Visit" jederzeit
zwischen den Data Centern wechseln, zwischen Regionen nicht - eine auf Aether veröffentlichte
Gruppe ist für einen Crystal-Spieler genauso erreichbar wie für einen Aether-Spieler. Ein Kanal
pro Data Center würde Gruppen künstlich zerstreuen. Jede Karte zeigt trotzdem weiterhin das
**konkrete** Data Center an (siehe `buildGroupCardEmbed` in `worker/src/index.ts`) - nur die
Kanal-/Webhook-*Wahl* selbst gruppiert nach Region. Die vollständige Tabelle steht als einzige,
zentrale Konstante `DATA_CENTER_TO_REGION` in `worker/src/index.ts`.

Die Karte wird bei jedem `PUT /group/:groupId` automatisch mitgepflegt (angelegt/editiert/
verschoben/entfernt, siehe `syncGroupDiscordCard`) und bei `DELETE /group/:groupId` entfernt
(siehe `removeGroupDiscordCard`) - **ohne** dass Plugin/Website davon irgendetwas mitbekommen
müssen. Ein täglicher Cron Trigger räumt zusätzlich Karten von Gruppen auf, deren KV-Eintrag
*still* per TTL abgelaufen ist, ohne dass vorher `DELETE /group/:groupId` aufgerufen wurde (siehe
`cleanupOrphanedDiscordCards`).

### 1. Neue/geänderte Dateien

| Datei | Änderung | Warum |
|---|---|---|
| `worker/src/discordWebhook.ts` (neu) | `createWebhookMessage`/`editWebhookMessage`/`deleteWebhookMessage`/`extractWebhookId` - rohe Discord-Webhook-HTTP-Calls, KEINE davon wirft je (siehe Klassendoc dort). Zusätzlich `setDiscordFetchForTests` als expliziter, dokumentierter Test-Hook. | Eigenes Modul analog zu `crypto.ts`/`worlds.ts` (reine Helfer ohne `Env`-Zugriff). Getrennt von `index.ts`, weil es eine eigene, in sich geschlossene Verantwortung ist (rohes HTTP gegen die Discord-API) und weil der Test-Hook so isoliert bleibt. |
| `worker/src/index.ts` | `DATA_CENTER_TO_REGION`/`regionForDataCenter`/`getRegionWebhookUrl`, `StoredGroupProfile.discordCard` (additiv, optional) + `DiscordCard`-Interface, `discordcards:<region>`-Index (`addDiscordCardIndexEntry`/`removeDiscordCardIndexEntry`/`DiscordCardIndexEntry`), `buildGroupCardEmbed` (nutzt das bestehende `buildGroupEmbedField` aus Phase 1 wieder), `createGroupDiscordCard`/`removeGroupDiscordCard`/`syncGroupDiscordCard`, `cleanupOrphanedDiscordCards`, neuer `scheduled`-Handler. `handleGroupPut`/`handleGroupDelete` bekommen zusätzlich den `ctx`-Parameter und stoßen die Kartensynchronisierung über `ctx.waitUntil(...)` an - **nach** dem eigentlichen KV-Put/-Delete, ohne die Antwort zu beeinflussen. `Env` um vier optionale `DISCORD_WEBHOOK_*`-Felder erweitert. | Kernstück der Funktion; `ctx.waitUntil` stellt sicher, dass kein Discord-Aufruf die Antwort an Plugin/Website verzögert oder deren Erfolg beeinflusst (siehe Aufgabenstellung). `buildGroupCardEmbed`-Wiederverwendung verhindert, dass sich das Kartenformat zwischen `/blunion browse` und den Kanal-Karten auseinanderentwickelt. **Verhalten von `GET /groups/browse`, `POST /discord/interactions` und allen bestehenden Endpunkten unverändert** - siehe `worker/test/index.test.ts`/`discord.test.ts`, alle 76 bisherigen Tests laufen weiterhin unverändert grün (siehe Testschritte unten). |
| `worker/wrangler.toml` | Kommentar zu den vier neuen `DISCORD_WEBHOOK_*`-Secrets (kein Klartext-Wert - siehe unten) + neuer `[triggers]`-Block mit `crons = ["0 3 * * *"]` (einmal täglich). | Cron Trigger müssen in `wrangler.toml` deklariert sein, um beim Deploy registriert zu werden; Secrets dokumentiert, aber nirgends im Klartext. |
| `worker/.dev.vars.example` | Vier neue, leere `DISCORD_WEBHOOK_*`-Platzhalter. | Lokale Entwicklung/Tests. |
| `worker/test/discordCards.test.ts` (neu) | Tests für: Kartenerstellung pro Region (alle vier Webhooks), Karten-Edit statt Duplikat bei Update, Karten-Verschiebung bei Regionswechsel, Kartenentfernung bei `unlisted`/`DELETE`, Discord-Ausfall bricht PUT/DELETE nicht ab, `regionForDataCenter` für alle 11 Data Center (plus ein expliziter Test, dass das retired "Shadow" `null` liefert), Cron-Cleanup verwaister Karten (inkl. "kein doppeltes Löschen beim zweiten Cron-Lauf"). | Siehe Abschnitt "Testschritte" unten. |
| `worker/src/index.ts` | `regionForDataCenter` zusätzlich `export`iert. | Direkt unit-testbar (siehe `crypto.test.ts` für dasselbe Muster). |

Keine bestehende Response-Shape, kein bestehender KV-Key und kein bestehendes Feld wurde
umbenannt/entfernt - `discordCard` ist rein additiv und wird nie an Plugin/Website ausgeliefert
(genau wie `editTokenHash`, siehe `stripForGroupResponse`, die unverändert bleibt).

**Warum ein zusätzlicher `discordcards:<region>`-Index nötig ist** (ausführlich dokumentiert direkt
am `DiscordCardIndexEntry`-Interface in `worker/src/index.ts`): Gruppen laufen nicht per explizitem
Löschen ab, sondern über KVs `expirationTtl` - der Key verschwindet dabei still, ohne jede
Benachrichtigung. Ein `KV.list({ prefix: "group:" })`-Durchlauf zeigt deshalb *immer* nur noch
existierende Gruppen; genau die bereits abgelaufenen Einträge, um die es beim Karten-Aufräumen
geht, sind darin per Definition nicht mehr enthalten. Ohne einen separaten, eigenständig
gepflegten Index gäbe es keine Möglichkeit zu erkennen "Gruppe X hatte mal eine Karte, X existiert
nicht mehr, also muss die Karte weg" - diese Information wäre mit `group:X` selbst mitverschwunden.

**Wie Discord-Fehler getestet werden, ohne echtes Netzwerk**: `discordWebhook.ts` ruft intern nie
direkt `fetch()` auf, sondern eine austauschbare Modulvariable (`setDiscordFetchForTests`). Ein
Versuch, stattdessen einen lokalen Test-HTTP-Server zu nutzen, scheitert innerhalb der
workerd-Testlaufzeit mit "Network connection lost" (Workers können keine eingehenden Verbindungen
von einem separaten Node-Prozess annehmen) - die Tests ersetzen `fetch` deshalb durch eine
In-Memory-Fake-Implementierung, die Aufrufe aufzeichnet und kontrollierte Antworten liefert.

### 2. Die 4 neuen Secrets setzen

```bash
cd worker
wrangler secret put DISCORD_WEBHOOK_NA
wrangler secret put DISCORD_WEBHOOK_EU
wrangler secret put DISCORD_WEBHOOK_JP
wrangler secret put DISCORD_WEBHOOK_OC
```

Jedes Mal fragt Wrangler interaktiv nach dem Wert - die vollständige Webhook-URL aus Schritt 3
einfügen (Format `https://discord.com/api/webhooks/<id>/<token>`). Anders als
`DISCORD_PUBLIC_KEY` sind alle vier **optional**: fehlt eine, legt der Worker für Gruppen auf
dieser Region einfach keine Karte an (kein Fehler, keine Auswirkung auf Plugin/Website).

Für lokale Entwicklung/Tests wie gehabt in `worker/.dev.vars` (siehe `.dev.vars.example`) - für die
automatisierten Tests in `worker/test/discordCards.test.ts` reicht das NICHT aus (siehe dortiger
Kommentar zu `setRegionWebhooks`/`setDiscordFetchForTests`: die Tests setzen die vier
`env.DISCORD_WEBHOOK_*`-Werte selbst zur Laufzeit, damit bestehende, Discord-unabhängige Tests in
`index.test.ts` nicht plötzlich unnötig einen Kartenaufbau im Hintergrund anstoßen).

### 3. Die 4 Discord-Webhooks anlegen

Für jede Region EINEN Kanal (z.B. `#gruppen-na`, `#gruppen-eu`, `#gruppen-jp`, `#gruppen-oc`) in
deinem Server anlegen, dann pro Kanal:

1. Kanal-Einstellungen (Zahnrad) -> "Integrationen" -> "Webhooks" -> "Neuer Webhook".
2. Dem Webhook einen Namen geben (z.B. "BLUnion Gruppenfinder"), optional ein Avatar.
3. "Webhook-URL kopieren" - das ist der Wert für `wrangler secret put DISCORD_WEBHOOK_<REGION>`
   aus Schritt 2 (bzw. `.dev.vars` für lokale Entwicklung).
4. Speichern.

Wiederholen für alle vier Regionen/Kanäle. Ein Webhook ist unabhängig von der
Interactions-Endpoint-URL aus Phase 1 (Abschnitt 3 oben) - Phase 1 (`/blunion browse`) und Phase
1.5 (persistente Karten) nutzen unterschiedliche Discord-Mechanismen (Slash-Command-Antwort vs.
Webhook-Push) und können unabhängig voneinander konfiguriert werden.

### 4. Cron Trigger in Cloudflare aktivieren/verifizieren

Der `[triggers]`-Block in `wrangler.toml` wird automatisch mit registriert, sobald du das nächste
Mal deployst:

```bash
cd worker
wrangler deploy
```

Danach im [Cloudflare-Dashboard](https://dash.cloudflare.com/) unter Workers & Pages -> dein
Worker -> Tab "Triggers" -> Abschnitt "Cron Triggers" sichtbar (`0 3 * * *`, einmal täglich um
03:00 UTC). Kein weiterer manueller Aktivierungsschritt nötig - ein Cron Trigger in `wrangler.toml`
ist beim Deploy automatisch aktiv.

Um NICHT 90 Tage auf einen echten TTL-Ablauf warten zu müssen, um das Aufräumverhalten zu prüfen
(siehe Testschritte unten für die Details):

- **Automatisiert** (empfohlen, siehe `worker/test/discordCards.test.ts`): löscht einen
  `group:`-KV-Eintrag direkt (statt über `DELETE /group/:groupId`, um genau den stillen
  TTL-Ablauf zu simulieren) und ruft danach `worker.scheduled(...)` direkt auf.
- **Lokal manuell**: `wrangler dev --test-scheduled` startet den Dev-Server mit einem zusätzlichen
  `/__scheduled`-Endpoint; `curl "http://localhost:8787/__scheduled"` löst dann denselben
  `scheduled`-Handler aus wie ein echter Cron-Tick.
- **Nach einem echten Deploy**: `wrangler tail` laufen lassen und entweder bis 03:00 UTC warten
  oder (falls im Dashboard verfügbar) den Cron Trigger dort manuell auslösen - die Logs zeigen dann
  jeden `console.error` aus `discordWebhook.ts`, falls ein Lösch-Call fehlschlägt.

### 5. Testschritte

Automatisiert (siehe `worker/test/discordCards.test.ts`, deckt alle Punkte unten ab):

```bash
cd worker
npm test
```

Manuell, pro Region einmal:

1. Über das Plugin oder die Website eine Testgruppe auf einem DC dieser Region veröffentlichen
   (`visibility: "listed"`, z.B. ein DC aus North America) -> im zugehörigen Discord-Kanal (z.B.
   `#gruppen-na`) sollte innerhalb weniger Sekunden eine neue Karte mit Notiz/Mitgliederliste/
   Verfügbarkeit UND dem konkreten Data Center im Titel erscheinen.
2. Dieselbe Gruppe aktualisieren (z.B. Notiz ändern, per PUT mit demselben `editToken`) -> DIESELBE
   Discord-Nachricht sollte sich aktualisieren (editiert, an derselben Stelle im Kanal, keine neue
   Nachricht darunter).
3. Die Gruppe löschen (`DELETE /group/:groupId` bzw. über Plugin/Website) -> die Karte verschwindet
   aus dem Kanal.
4. Wiederholen für je eine Testgruppe pro übriger Region (Europe/Japan/Oceania) - jede sollte in
   ihrem jeweils EIGENEN Kanal erscheinen, nicht im `#gruppen-na`-Kanal.
5. Cron-Aufräumverhalten ohne 90 Tage Wartezeit prüfen: siehe Abschnitt 4 oben ("Lokal manuell" via
   `wrangler dev --test-scheduled` + `curl .../__scheduled`, oder die automatisierten
   `discordCards.test.ts`-Tests, die genau das bereits abdecken).
