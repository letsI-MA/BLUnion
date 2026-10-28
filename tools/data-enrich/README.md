# Data-Enrich (einmaliges Anreicherungs-Werkzeug)

Ergänzt `Data/spells.json`, `Data/monsters.json` und `Data/locations.json` um Game-IDs, damit das
Plugin Namen und Beschreibungen zur Laufzeit in der Anzeigesprache aus den Spieldaten lesen kann
(siehe `Services/GameTextService.cs`). Eigenständige .NET-Konsolen-App mit dem Lumina-NuGet-Paket -
**nicht** Teil des Plugins oder des Release-Builds (`BLUnion.csproj` schließt `tools\**` aus).

## Aufruf

```
cd tools/data-enrich
dotnet run -- "C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack"
```

Optionen: `--data <Ordner>` (Standard: der `Data`-Ordner im Repo, wird von hier aus gesucht) und
`--report <Datei>` (Standard: `report.md` in diesem Ordner).

## Was es tut

| Datei | neue Felder | Abgleich |
|---|---|---|
| spells.json | `ActionId`, `AozActionId` | `AozAction.Action` = `Id`, geprüft gegen `AozActionTransient.Number` = `SpellbookOrder` |
| monsters.json | `BNpcNameId` bzw. `BNpcNameIds` | englischer Name = `BNpcName.Singular` (case-insensitiv, bei gleichnamigen die niedrigste ID); Sammelnamen mit ` & ` / ` or ` werden geteilt und je Teil abgeglichen |
| locations.json | `TerritoryTypeId`, `MapId`, `PlaceNameId`, `ContentFinderConditionId` bzw. `ContentFinderConditionIds` | Duties: `DutyName` = `ContentFinderCondition.Name`; Sammeleinträge („Thornmarch (Hard oder Extreme)“, „… (Turn 1-2)“) je Variante; sonst `ZoneNameEn` = `PlaceName.Name` → `TerritoryType` |

- **Nichts wird geraten.** Kein oder kein eindeutiger Treffer → Feld bleibt leer, Eintrag steht
  mit Grund und Kandidaten in [`report.md`](report.md). Verglichen wird exakt (nur
  Groß-/Kleinschreibung, Leerzeichen und typografische Apostrophe werden vereinheitlicht).
- Pseudo-Monster (Totems, Startzauber, Carnivale-Fortschritt) bekommen bewusst keine ID.
- Sammelnamen („Goblin Fisher & Gambler") bekommen `BNpcNameIds` nur, wenn **jeder** Teil exakt
  passt. Teile, die für sich kein vollständiger Monstername sind (z.B. „Gambler"), stehen mit
  Kandidaten im Report und werden von Hand eingetragen - z.B. `"BNpcNameIds": [367, 3099]` (in der
  Reihenfolge des Namens). Von Hand gesetzte Werte bleiben bei weiteren Läufen erhalten und zählen
  als zugeordnet, wenn sie zum Namen passen (je Teil eine existierende ID, automatisch gefundene Teile
  stimmen überein) - der Report nennt sie dann unter „Hinweise“. Die Anzeige verbindet die Namen
  pro Sprache mit „ / “ (`GameTextService.GetMonsterName`).
- Sammeleinträge mit mehreren Duty-Varianten („<Basis> (Hard oder Extreme)“, „(Normal, Hard oder Extreme)“,
  „(Turn 1-2)“) bleiben EIN Eintrag mit dem Sammelnamen und bekommen `ContentFinderConditionIds` - nur
  wenn jede Variante genau einen Treffer hat („<Basis> (<Variante>)“ bzw. „<Basis> - <Variante>“, mit/ohne
  führendes „The“; „Normal“ = nur die Basis). Keine TerritoryType-/PlaceName-ID, die unterscheiden sich je
  Variante. Ergibt sich nur eine Variante, wird sie wie eine Einzel-Duty zugeordnet.
- Masked-Carnivale-Stages sind keine eigenen Spieleinträge und bekommen keine Game-ID. Sie tragen
  stattdessen `MaskedCarnivaleStage` (Stage-Nummer, von Hand gepflegt); der Anzeigename kommt aus
  `UiStrings.Key.MaskedCarnivaleStageFormat` in allen Sprachen. Ohne diese Nummer steht die Stage als
  offen im Report.
- Ist ein Gebiet mehrdeutig, der Ortsname aber eindeutig, wird nur `PlaceNameId` gesetzt (reicht
  für die Namensauflösung).
- `Coordinates` bleiben handgepflegt - die stehen in keinem Sheet.
- Bestehende Felder bleiben unverändert, neue Felder stehen am Ende jedes Objekts. Ein von Hand
  gesetzter Wert, zu dem das Tool keinen Treffer findet, bleibt stehen (im Report markiert).
- **Idempotent:** Ein zweiter Lauf ändert nichts. Formatierung, Reihenfolge und Zeilenenden der
  JSONs bleiben erhalten, damit der Git-Diff nur die neuen Felder zeigt.

Zu Beginn prüft das Tool per Stichprobe (Snort = Action 11383 = Spellbook #25), ob die
Sheet-Definitionen zu den Spieldaten passen, und bricht sonst ab. Hintergrund: Dalamud nutzt
Lumina.Excel 7.5.1, auf NuGet gibt es nur 7.5.0.

## Wann erneut ausführen

- Nach neuen Spells, Monstern oder Orten in den JSONs (z.B. neuer Blaumagier-Patch).
- Nach einem Spiel-Patch, falls der Report danach offene Einträge zeigt, die es im Spiel inzwischen gibt.
- Nach Korrekturen an Namen in den JSONs (z.B. `DutyName` an den Spielnamen angleichen - der
  Report listet dafür passende Kandidaten).

Danach `report.md` und den Diff der JSONs prüfen und mitcommitten.
