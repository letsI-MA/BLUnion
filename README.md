# BLUnion — MVP-Gerüst (Phase 1)

## Setup lokal

1. Das Projekt nutzt `Dalamud.NET.Sdk` (aktuell Version 15.0.0, passend zu
   Dalamud API Level 15 / Spielpatch 7.5) als Projekt-SDK in der `.csproj` —
   das bindet `Dalamud`, `Lumina`/`Lumina.Excel` und ImGui automatisch ein,
   kein manuelles Referenzieren mehr nötig. Falls eure lokal installierte
   Dalamud-Version einen anderen API-Level hat, die SDK-Version in der ersten
   Zeile von `BLUnion.csproj` entsprechend anpassen (siehe
   https://dalamud.dev/plugin-development/how-tos/v12-SDK-migration/) und
   `DalamudApiLevel` in `BLUnion.json` gegenprüfen.
2. Es wird trotzdem die Umgebungsvariable `DALAMUD_HOME` erwartet (zeigt
   normalerweise auf `%AppData%\XIVLauncher\addon\Hooks\dev`) — für's Laden
   als Dev-Plugin im Spiel, nicht mehr für die Build-Referenzen selbst.
3. `dotnet build`. Das Manifest (`BLUnion.json`) wird von
   `DalamudPackager` (Teil der SDK) automatisch nach jedem Build gelesen und
   neben die `.dll` in den Output-Ordner kopiert/ergänzt — dort erwartet
   Dalamud es beim Laden als Dev-Plugin. `Punchline` ist darin ein
   Pflichtfeld (der Build bricht sonst ab).

Stand 2026-08-17 lokal verifiziert: `dotnet build` läuft mit dieser
Konfiguration sauber durch (0 Warnungen, 0 Fehler) gegen Dalamud 15.0.3.2 /
API Level 15.

## Was funktioniert (soweit auf offiziellen, stabilen APIs aufgebaut)

- `PartyService`: Party lesen, Blue Mages filtern (über `IPartyList` +
  dynamischer ClassJob-Lookup, keine hartcodierte Job-Id).
- `SpellDataService`: lädt spells/monsters/sources/locations.json.
- `ComparisonService`: reiner Algorithmus, testbar ohne Spielverbindung.
- `ManualCodeSyncProvider`: Export/Import-Code (Sync-Option A).
- `LocalSpellUnlockService`: liest den eigenen BLU-Unlock-Status über den
  offiziellen (aber noch als "experimental" markierten) Dalamud-Service
  `IUnlockState.IsAozActionUnlocked(...)` (Details + Versionsstand siehe
  Kommentar in der Datei). **Noch nicht im Spiel gegen das eigene Spellbook
  verifiziert** — das ist ein manueller Schritt vor produktivem Einsatz.
- UI-Grundgerüst mit Party/Comparison/Sync-Tabs.

## Beispieldaten

Die JSON-Dateien unter `Data/` enthalten nur 3 Beispiel-Spells zum Testen der
Struktur (Glower, Bad Breath, Missile) — Action-Ids, Monster und Koordinaten
sind **nicht verifiziert** und müssen vor echter Nutzung gegen eine
verlässliche Quelle (z.B. Lumina-Sheets, aktuelle Community-Guides)
gegengeprüft werden.

## Nächste Schritte

1. `LocalSpellUnlockService`-Ergebnis im Spiel gegen das eigene Spellbook
   verifizieren (Plugin laden, "Eigenen Status ermitteln + exportieren"
   klicken, `LearnedSpellIds` mit dem AOZ-Notizbuch abgleichen).
2. Sobald das steht: Comparison-Tab end-to-end mit echten Daten testen.
3. Danach Phase 2 (Party-Vergleich mit mehreren echten Spielern über
   Sync-Option A) und später Sync-Option C (Cloudflare Worker).
