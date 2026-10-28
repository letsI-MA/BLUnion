# Data-Enrich Report

Erzeugt von `tools/data-enrich` gegen Spielversion `2026.10.16.0000.0000`. Nicht von Hand bearbeiten -
wird bei jedem Lauf neu geschrieben.

| Datei | Einträge | zugeordnet | teilweise | offen | bewusst übersprungen |
|---|---|---|---|---|---|
| spells.json | 124 | 124 | 0 | 0 | 0 |
| monsters.json | 167 | 154 | 0 | 0 | 13 |
| locations.json | 130 | 121 | 0 | 0 | 9 |

## spells.json

Alle Einträge zugeordnet.

## monsters.json

Alle Einträge zugeordnet.

Bewusst übersprungen:

- 43 Totem: kein echtes Monster (TotemSpellCount) - bewusst ohne BNpcNameId
- 46 Totem: kein echtes Monster (TotemSpellCount) - bewusst ohne BNpcNameId
- 48 Totem: kein echtes Monster (TotemSpellCount) - bewusst ohne BNpcNameId
- 53 Masked Carnivale: kein echtes Monster (MaskedCarnivaleProgress) - bewusst ohne BNpcNameId
- 54 Masked Carnivale: kein echtes Monster (MaskedCarnivaleProgress) - bewusst ohne BNpcNameId
- 55 Totem: kein echtes Monster (TotemSpellCount) - bewusst ohne BNpcNameId
- 99 Totem: kein echtes Monster (TotemSpellCount) - bewusst ohne BNpcNameId
- 100 Masked Carnivale: kein echtes Monster (MaskedCarnivaleProgress) - bewusst ohne BNpcNameId
- 118 Totem: kein echtes Monster (TotemLevel) - bewusst ohne BNpcNameId
- 125 Totem: kein echtes Monster (TotemSpellCount) - bewusst ohne BNpcNameId
- 137 Totem: kein echtes Monster (TotemLevel) - bewusst ohne BNpcNameId
- 145 Totem: kein echtes Monster (TotemSpellCount) - bewusst ohne BNpcNameId
- 164 Learned First: kein echtes Monster (StartingSpell) - bewusst ohne BNpcNameId

Hinweise:

- 26 Goblin Fisher & Gambler: von Hand ergänzt: "Gambler" = 3099 "goblin gambler"
- 122 Coeurl Sruti or Smriti: von Hand ergänzt: "Smriti" = 6120 "coeurl smriti"

## locations.json

Alle Einträge zugeordnet.

Bewusst übersprungen:

- 2 Masked Carnivale: 20: Masked-Carnivale-Stage 20 - Name über UiStrings.MaskedCarnivaleStageFormat, bewusst ohne Game-ID
- 3 Masked Carnivale: 27: Masked-Carnivale-Stage 27 - Name über UiStrings.MaskedCarnivaleStageFormat, bewusst ohne Game-ID
- 5 Masked Carnivale: 15: Masked-Carnivale-Stage 15 - Name über UiStrings.MaskedCarnivaleStageFormat, bewusst ohne Game-ID
- 10 Masked Carnivale: 19: Masked-Carnivale-Stage 19 - Name über UiStrings.MaskedCarnivaleStageFormat, bewusst ohne Game-ID
- 14 Masked Carnivale: 25: Masked-Carnivale-Stage 25 - Name über UiStrings.MaskedCarnivaleStageFormat, bewusst ohne Game-ID
- 37 Masked Carnivale: 25: Masked-Carnivale-Stage 25 - Name über UiStrings.MaskedCarnivaleStageFormat, bewusst ohne Game-ID
- 49 Masked Carnivale: 29: Masked-Carnivale-Stage 29 - Name über UiStrings.MaskedCarnivaleStageFormat, bewusst ohne Game-ID
- 61 Masked Carnivale: 24: Masked-Carnivale-Stage 24 - Name über UiStrings.MaskedCarnivaleStageFormat, bewusst ohne Game-ID
- 108 Masked Carnivale: 8: Masked-Carnivale-Stage 8 - Name über UiStrings.MaskedCarnivaleStageFormat, bewusst ohne Game-ID

Hinweise:

- 6 The Binding Coil of Bahamut (Turn 1-2): Varianten zugeordnet: CFC 93 "the Binding Coil of Bahamut - Turn 1", CFC 94 "the Binding Coil of Bahamut - Turn 2"
- 42 The Bowl of Embers (Normal, Hard oder Extreme): Varianten zugeordnet: CFC 56 "the Bowl of Embers", CFC 59 "the Bowl of Embers (Hard)", CFC 63 "the Bowl of Embers (Extreme)"
- 43 The Navel (Hard oder Extreme): Varianten zugeordnet: CFC 60 "the Navel (Hard)", CFC 64 "the Navel (Extreme)"
- 44 The Striking Tree (Hard oder Extreme): Varianten zugeordnet: CFC 77 "the Striking Tree (Hard)", CFC 78 "the Striking Tree (Extreme)"
- 45 Akh Afah Amphitheatre (Extreme): über die Variante zugeordnet: CFC 80 "the Akh Afah Amphitheatre (Extreme)"
- 46 The Whorleater (Hard oder Extreme): Varianten zugeordnet: CFC 72 "the Whorleater (Hard)", CFC 73 "the Whorleater (Extreme)"
- 58 Thornmarch (Hard oder Extreme): Varianten zugeordnet: CFC 66 "Thornmarch (Hard)", CFC 67 "Thornmarch (Extreme)"
- 72 Thok ast Thok (Hard oder Extreme): Varianten zugeordnet: CFC 86 "Thok ast Thok (Hard)", CFC 87 "Thok ast Thok (Extreme)"

## SeString-Makros in AozActionTransient.Description (DE/EN/FR/JA)

Zur Kontrolle der Klartext-Bereinigung im Plugin (GameTextCleaner).

- Italic: 2
- NewLine: 373
- NonBreakingSpace: 24
- SoftHyphen: 131
