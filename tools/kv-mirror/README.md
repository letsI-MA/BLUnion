# BLUnion Live-Sync KV-Mirror (Dev-Tool)

Rein lokales Debug-/Inspektions-Tool: spiegelt alle Live-Sync-Profile aus dem
Cloudflare-KV-Namespace `BLUNION_PROFILES` (Backend des Live-Sync-Features, siehe
`worker/src/index.ts` im Repo-Root) in eine lokale SQLite-Datei, damit man sie z.B. mit
[DB Browser for SQLite](https://sqlitebrowser.org/) bequem durchsuchen/filtern/sortieren kann -
ohne die Cloudflare-Weboberfläche einzeln nach jedem Key abzusuchen.

**Kein Teil des eigentlichen Plugins/Workers/Website.** Analog zu `Services/DevTestFixtures.cs`
im .NET-Projekt bewusst dauerhaft im Repo belassen, klar als Dev-Only markiert. Das Skript
greift nur **lesend** auf die Cloudflare-KV-REST-API zu (List Keys + Bulk Get) - es verändert
oder löscht nichts in Cloudflare KV selbst; verändert wird ausschließlich die lokale
`blunion_profiles.db`.

## Technik

Python 3, **ausschließlich Standardbibliothek** (`urllib`, `json`, `sqlite3`, `pathlib`, ...) -
kein `pip install` nötig. Kein Build-Schritt, keine weitere Abhängigkeit zum restlichen Projekt
(Plugin/Worker/Website bleiben davon komplett unberührt).

## Einrichtung

1. Cloudflare-API-Token erstellen (einmalig):
   - [dash.cloudflare.com](https://dash.cloudflare.com/) → oben rechts auf das Profil-Icon →
     **My Profile** → **API Tokens** → **Create Token**.
   - **Create Custom Token** wählen.
   - Permissions: `Account` | `Workers KV Storage` | `Read`.
   - Account Resources: `Include` | den Account, der den Namespace `BLUNION_PROFILES` enthält.
   - Restlichen Standardeinstellungen folgen, **Continue to summary** → **Create Token**.
   - Den angezeigten Token **sofort kopieren** (wird danach nicht mehr angezeigt).
2. Im selben Ordner (`tools/kv-mirror/`) eine Datei `.env` anlegen (z.B. Kopie von
   `.env.example`) mit folgendem Inhalt:
   ```
   CLOUDFLARE_API_TOKEN=<dein-token-hier>
   ```
   `.env` ist über `.gitignore` in diesem Ordner vom Commit ausgeschlossen - der Token landet
   dadurch nie im Repo. Account-ID und Namespace-ID stehen dagegen (unkritisch, siehe
   `worker/README.md`) direkt als Konstanten in `mirror_kv_to_sqlite.py`.

Der Token bekommt nur Lesezugriff auf Workers-KV-Storage des gesamten Accounts - trotzdem wie
ein Passwort behandeln, nicht teilen/committen. Nicht mehr gebraucht? Im Cloudflare-Dashboard
unter **API Tokens** jederzeit widerrufbar.

## Ausführen

```bash
cd tools/kv-mirror
python mirror_kv_to_sqlite.py
```

(Je nach System/PATH ggf. `python3` oder `py` statt `python`.)

Das Skript:

1. listet alle Keys mit Prefix `profile:` im Namespace auf (paginiert über alle Seiten),
2. lädt die zugehörigen Werte in 100er-Gruppen über den Bulk-Get-Endpoint,
3. parst jeden Wert als JSON und schreibt ihn in die Tabelle `profiles` der lokalen
   `blunion_profiles.db` (`INSERT OR REPLACE` nach `kv_key` - mehrfaches Ausführen aktualisiert
   bestehende Zeilen statt Duplikate anzulegen),
4. entfernt lokale Zeilen, deren `kv_key` beim aktuellen Lauf nicht mehr in KV gefunden wurde
   (abgelaufen/gelöscht) - die lokale DB spiegelt so immer den aktuellen Cloudflare-Stand,
   statt nur zu wachsen,
5. gibt am Ende eine kurze Zusammenfassung aus (gefundene/gespiegelte/übersprungene Einträge,
   Laufzeit).

Danach `blunion_profiles.db` (liegt im selben Ordner) mit DB Browser for SQLite öffnen, Tabelle
`profiles`.

## Tabellenschema

Eine Spalte pro Feld des `StoredProfile`-Interfaces aus `worker/src/index.ts`, plus `kv_key`
(Primary Key, der komplette KV-Key `profile:<world>:<characterName>`) und `mirroredAt`
(Zeitpunkt des letzten Spiegel-Laufs, in dem diese Zeile zuletzt aktualisiert wurde):

| Spalte | Typ | Bemerkung |
|---|---|---|
| `kv_key` | TEXT (PK) | `profile:<world>:<characterName>`, beide Teile lowercased |
| `characterName` | TEXT | |
| `world` | TEXT | |
| `dataCenter` | TEXT | |
| `spellBitmaskBase64` | TEXT | 16-Byte-Bitmaske, Base64 URL-safe ohne Padding |
| `editTokenHash` | TEXT | SHA-256-Hex - **nur der Hash**, nie der Klartext-Token (der verlässt Cloudflare KV nie) |
| `visibility` | TEXT | `"listed"` oder `"unlisted"` |
| `availabilityTags` | TEXT | JSON-Array-String, z.B. `["evening","weekend"]` |
| `note` | TEXT | |
| `wantedPlayerCount` | INTEGER | |
| `createdAt` | TEXT | ISO-8601 |
| `updatedAt` | TEXT | ISO-8601 |
| `mirroredAt` | TEXT | ISO-8601, UTC - wann DIESER Spiegel-Lauf die Zeile zuletzt geschrieben hat |

`availabilityTags`/`note`/`wantedPlayerCount` sind bei sehr alten, noch von vor Phase 2
(Gruppenfinder) stammenden Profilen `NULL` (siehe `worker/README.md` zur
Rückwärtskompatibilität) - kein Fehler, einfach noch nicht gesetzt gewesen.

## Fehlerbehandlung

- Fehlt `.env` oder enthält keinen `CLOUDFLARE_API_TOKEN`: klare Fehlermeldung mit Verweis auf
  diesen README-Abschnitt, kein Stacktrace.
- Einzelne Werte, die sich nicht als JSON parsen lassen (oder kein JSON-Objekt sind): werden
  übersprungen, tauchen aber gezählt in der Abschluss-Zusammenfassung auf ("Übersprungen
  (ungültig)") - ein einzelner kaputter Eintrag bricht den restlichen Lauf nicht ab.
