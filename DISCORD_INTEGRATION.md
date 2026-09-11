# Discord-Integration (Phase 1 - rein lesend)

`/blunion browse` ist ein Discord-Slash-Command, der öffentlich gelistete (`visibility: "listed"`)
Blue-Mage-Gruppen direkt in Discord anzeigt - als Embed, mit einem Link-Button zur bestehenden
[Web-Companion-Seite](https://letsi-ma.github.io/BLUnion/). Er reicht dafür **dieselbe Logik**
durch, die bereits `GET /groups/browse` benutzt (siehe `computeGroupsBrowse` in
`worker/src/index.ts`) - keine neue KV-Struktur, kein zweites Backend.

**Bewusst NICHT Teil dieser Phase** (siehe Aufgabenstellung): Discord-Account-Linking
(`/blunion link`), und jede Art von Schreiboperation (Gruppe erstellen/beitreten/verlassen/
bearbeiten/canceln) über Discord. `/blunion browse` ist rein lesend.

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
