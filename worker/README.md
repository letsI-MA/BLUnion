# BLUnion Live-Sync Worker (Phase 1)

Cloudflare-Worker-Backend für das Live-Sync-Feature des BLUnion-Plugins. Ersetzt für den
Alltagsfall "ich bin mit anderen Blue Mages in einer Party" den manuellen `BLU:`-Code-Austausch
(siehe `ManualCodeSyncProvider.cs`): das Plugin veröffentlicht den eigenen Spell-Status hier
automatisch und ruft die Profile anderer Blue-Mage-Party-Mitglieder ebenso automatisch ab.

Bewusst **eigenständig** vom .NET-Plugin-Code (eigener `package.json`/`tsconfig.json`) - kein
Build-Schritt des Plugins hängt hiervon ab, und umgekehrt.

**Phase 2 (aktuell):** öffentlicher Gruppenfinder (`GET /profiles/browse`, siehe unten) -
erweitert dasselbe Profil um `visibility`/`availabilityTags`/`note`/`wantedPlayerCount`. Es gibt
**kein separates Gruppenfinder-Profil und keinen eigenständigen Login** - Sichtbarkeit im
Gruppenfinder setzt zwingend voraus, dass für den Charakter bereits ein Live-Sync-Profil (Phase 1,
mit `editToken`) existiert.

**Noch nicht Teil dieses Workers:** Website-Integration, Discord-Bot.

## Datenmodell

Ein Profil (KV-Value, JSON) sieht so aus:

```json
{
  "characterName": "Beispielname",
  "world": "Gilgamesh",
  "dataCenter": "Aether",
  "spellBitmaskBase64": "…16-Byte-Bitmaske, Base64 URL-safe ohne Padding…",
  "editTokenHash": "…SHA-256-Hex, NIE der Klartext-Token…",
  "visibility": "unlisted",
  "availabilityTags": ["evening", "weekend"],
  "note": "Suche noch 3 Spells, gerne auch mehrfach am Abend",
  "wantedPlayerCount": 0,
  "createdAt": "2026-08-20T12:00:00.000Z",
  "updatedAt": "2026-08-20T12:00:00.000Z"
}
```

KV-Key: `profile:<world>:<characterName>` (beide Teile lowercased). `spellBitmaskBase64` nutzt
exakt dasselbe Bit-Mapping wie der bestehende `BLU:`-Sync-Code (`SpellDataService.OrderedSpellIds`,
aufsteigend nach Spell-Id), nur ohne das dortige Namens-Präfixbyte - der Name steht hier schon im
KV-Key bzw. im `characterName`-Feld.

`visibility` ist `"unlisted"` (Default, auch für alle vor Phase 2 angelegten Profile - siehe
Rückwärtskompatibilität unten) oder `"listed"` (im Gruppenfinder sichtbar). `availabilityTags`
sind 0-5 Werte aus `morning`/`afternoon`/`evening`/`weekend`/`flexible` (intern englisch, siehe
Übersetzung im Plugin über `UiStrings`). `note` ist serverseitig auf 60 Zeichen gekappt.
`wantedPlayerCount` ist eine ganze Zahl 0-8 (`0` = "egal wie viele").

Profile laufen automatisch nach 90 Tagen Inaktivität ab (KV `expirationTtl`, wird bei jedem
Update neu gesetzt) - kein Cron-Job nötig.

## Endpunkte

| Methode | Pfad | Auth | Zweck |
|---|---|---|---|
| `PUT` | `/profile/:world/:characterName` | `editToken` im Body (außer beim allerersten Anlegen) | Profil anlegen/aktualisieren |
| `GET` | `/profile/:world/:characterName` | keine | Profil abrufen (öffentlich lesbar, siehe Aufgabenstellung) |
| `DELETE` | `/profile/:world/:characterName` | Header `X-Edit-Token` | Profil löschen |
| `GET` | `/profiles/browse?dataCenter=<DC>` | keine | Gruppenfinder: alle `listed`-Profile auf diesem Data Center |

`:world` und `:characterName` müssen URI-komponenten-kodiert werden (Charakternamen enthalten
oft Leerzeichen/Apostrophe).

`visibility`/`availabilityTags`/`note`/`wantedPlayerCount` sind im `PUT`-Body allesamt optional -
fehlt eines, bleibt der bisherige gespeicherte Wert unverändert (bzw. der jeweilige Default bei
einem neuen Profil). Ein reiner Spell-Status-Push (Phase 1, kennt diese Felder nicht) leert die
Gruppenfinder-Angaben dadurch nicht versehentlich. Ein tatsächlich übergebener, aber ungültiger
Wert (z.B. ein nicht erlaubter `availabilityTags`-Eintrag) führt zu `400`.

`GET /profiles/browse` liefert je Treffer `{ characterName, world, spellBitmaskBase64,
availabilityTags, note, wantedPlayerCount, updatedAt }` - nie `dataCenter` (redundant, der
Aufrufer hat es selbst übergeben) und nie `editTokenHash`. Iteriert aktuell über ALLE
`profile:`-Keys und filtert in-memory (siehe Kommentar bei `handleBrowse` in `src/index.ts`) -
bei sehr vielen Profilen (>1000) sollte das durch einen echten Data-Center-Index ersetzt werden;
für die bei Phase 2 zu erwartende Nutzerzahl ist das bewusst noch keine verfrühte Optimierung.

Beim allerersten `PUT` für einen Charakter wird ein neuer `editToken` generiert und **einmalig**
in der Response zurückgegeben (`response.editToken`) - danach existiert er serverseitig nur noch
als Hash. Wer ihn verliert, kann das Profil nicht mehr bearbeiten/löschen und muss einfach ein
neues (mit neuem Token) anlegen; das alte läuft nach der jeweils gültigen TTL automatisch ab
(siehe `ttlHours` unten).

`ttlHours` ist ein weiteres optionales `PUT`-Body-Feld (Zahl) - überschreibt die reguläre
90-Tage-Lebensdauer mit `ttlHours * 3600` Sekunden, geclampt auf 1-2160 Stunden (max. 90 Tage,
also nie länger als regulär). Fehlt es oder ist es ungültig, gilt unverändert die 90-Tage-Default-
TTL. Gedacht für `docs/index.html` (die Website setzt hier immer `24`, damit dort veröffentlichte
Testprofile nicht 90 Tage lang herumliegen) - das Plugin schickt dieses Feld nie mit.

## Bekannte Einschränkungen

**KV-Konsistenz (Browse-Liste kann kurz hinterherhinken):** `BLUNION_PROFILES` ist ein
Cloudflare-KV-Namespace, und KV ist ["eventually consistent"](https://developers.cloudflare.com/kv/concepts/how-kv-works/)
("How KV works"): ein Schreibvorgang ist an dem Cloudflare-Standort, an dem geschrieben wurde,
sofort sichtbar, kann aber bis zu 60 Sekunden brauchen, um an ANDEREN Standorten anzukommen. In
der Praxis beobachtet: ca. 30 Sekunden Verzögerung, bis ein frisch veröffentlichtes Profil in
`GET /profiles/browse` an einem anderen Ort auftaucht.

Das ist **kein Bug im Worker-Code**, sondern erwartetes Verhalten der zugrundeliegenden
Speichertechnologie - falls dieses Verhalten künftig (mir, dir, oder Claude Code in einer
späteren Session) komisch vorkommt: bitte HIER nachschauen, bevor Zeit in die Fehlersuche
investiert wird.

Betrifft **nur** die Sichtbarkeit für ANDERE über die Browse-Liste. Das eigene, gerade
veröffentlichte Profil wird im Plugin bzw. auf der Website sofort über die lokale
Erfolgsbestätigung der `PUT`-Response angezeigt (siehe `LiveSyncService.LastKnownOwnProfile`
bzw. `docs/index.html`) - unabhängig von dieser KV-Verzögerung, da das nicht über einen erneuten
Lesevorgang aus KV läuft.

Bewusst **nicht** durch einen Wechsel auf Cloudflare Durable Objects gelöst (dort wäre echte
sofortige Konsistenz möglich) - der Zugewinn rechtfertigt bei der hier erwarteten Nutzerbasis-
Größe nicht die deutlich höhere Infrastruktur-Komplexität.

## Lokal testen

```bash
cd worker
npm install
npm run dev
```

`wrangler dev` startet einen lokalen Server (Standard: `http://localhost:8787`) inkl. lokal
simuliertem KV-Namespace - kein Cloudflare-Account nötig. Beispiel-Request:

```bash
curl -X PUT http://localhost:8787/profile/Gilgamesh/Beispielname \
  -H "Content-Type: application/json" \
  -d "{\"spellBitmaskBase64\":\"AAAAAAAAAAAAAAAAAAAAAA\"}"
```

## Deployen

```bash
cd worker
npm install
npx wrangler login          # einmalig, öffnet den Browser für die Cloudflare-Anmeldung
npx wrangler kv namespace create BLUNION_PROFILES
```

Der letzte Befehl gibt eine `id` aus - die in `wrangler.toml` unter `[[kv_namespaces]]` anstelle
von `REPLACE_WITH_KV_NAMESPACE_ID` eintragen. Danach:

```bash
npm run deploy
```

Wrangler gibt am Ende die generierte `*.workers.dev`-URL aus (Format:
`https://blunion-livesync.<dein-subdomain>.workers.dev`, der Name kommt aus `wrangler.toml`).

**Wichtig:** Diese URL anschließend in die Plugin-Konstante `LiveSyncService.WorkerBaseUrl`
(`Services/LiveSyncService.cs`, deutlich als `TODO` markiert) eintragen - ohne das bleibt
Live-Sync im Plugin funktionslos (alle Aufrufe schlagen fehl, das Plugin bleibt aber voll nutzbar,
da Live-Sync rein additiv/opt-in ist).

## Typprüfung ohne echten Cloudflare-Account

```bash
npm run typecheck
```
