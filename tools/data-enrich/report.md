# Data-Enrich Report

Erzeugt von `tools/data-enrich` gegen Spielversion `2026.09.15.0000.0000`. Nicht von Hand bearbeiten -
wird bei jedem Lauf neu geschrieben.

| Datei | Einträge | zugeordnet | teilweise | offen | bewusst übersprungen |
|---|---|---|---|---|---|
| spells.json | 124 | 124 | 0 | 0 | 0 |
| monsters.json | 165 | 141 | 0 | 11 | 13 |
| locations.json | 111 | 94 | 0 | 17 | 0 |

## spells.json

Alle Einträge zugeordnet.

## monsters.json

| Eintrag | Grund | Kandidaten | Hinweis |
|---|---|---|---|
| 7 A.D.S. | kein BNpcName mit diesem englischen Namen |  |  |
| 9 Haliostroper | kein BNpcName mit diesem englischen Namen |  |  |
| 11 Reflective Rebekah | kein BNpcName mit diesem englischen Namen |  |  |
| 26 Goblin Fisher & Gambler | Sammelname, nicht zuordenbar: "Gambler" (zugeordnet: "Goblin Fisher" = 367) | "Gambler": 3099 "goblin gambler" |  |
| 27 Sabetonder Bailaor | kein BNpcName mit diesem englischen Namen |  |  |
| 31 Denezen of the Dark | kein BNpcName mit diesem englischen Namen |  |  |
| 81 Everlasting Bibliotaph | kein BNpcName mit diesem englischen Namen |  |  |
| 85 Furryfoot Kupil Kipp | kein BNpcName mit diesem englischen Namen |  |  |
| 122 Coeurl Sruti or Smriti | Sammelname, nicht zuordenbar: "Smriti" (zugeordnet: "Coeurl Sruti" = 6119) | "Smriti": 6120 "coeurl smriti" |  |
| 149 Aenc Thon | kein BNpcName mit diesem englischen Namen |  |  |
| 150 Lord of the Lingering Gaze | kein BNpcName mit diesem englischen Namen |  |  |

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

## locations.json

| Eintrag | Grund | Kandidaten | Hinweis |
|---|---|---|---|
| 2 Masked Carnivale: 20 | Masked-Carnivale-Stage, kein eindeutiger Spieleintrag |  |  |
| 3 Masked Carnivale: 27 | Masked-Carnivale-Stage, kein eindeutiger Spieleintrag |  |  |
| 5 Masked Carnivale: 15 | Masked-Carnivale-Stage, kein eindeutiger Spieleintrag |  |  |
| 6 The Binding Coil of Bahamut (Turn 1-2) | keine ContentFinderCondition mit diesem Namen | CFC 93 "the Binding Coil of Bahamut - Turn 1", CFC 94 "the Binding Coil of Bahamut - Turn 2", CFC 95 "the Binding Coil of Bahamut - Turn 3", CFC 96 "the Binding Coil of Bahamut - Turn 4", CFC 97 "the Binding Coil of Bahamut - Turn 5" |  |
| 10 Masked Carnivale: 19 | Masked-Carnivale-Stage, kein eindeutiger Spieleintrag |  |  |
| 14 Masked Carnivale: 25 | Masked-Carnivale-Stage, kein eindeutiger Spieleintrag |  |  |
| 37 Masked Carnivale | Masked-Carnivale-Stage, kein eindeutiger Spieleintrag |  |  |
| 42 The Bowl of Embers (Normal, Hard oder Extreme) | keine ContentFinderCondition mit diesem Namen | CFC 56 "the Bowl of Embers", CFC 59 "the Bowl of Embers (Hard)", CFC 63 "the Bowl of Embers (Extreme)", CFC 959 "Memory of Embers" |  |
| 43 The Navel (Hard oder Extreme) | keine ContentFinderCondition mit diesem Namen | CFC 57 "the Navel", CFC 60 "the Navel (Hard)", CFC 64 "the Navel (Extreme)" |  |
| 44 The Striking Tree (Hard oder Extreme) | keine ContentFinderCondition mit diesem Namen | CFC 77 "the Striking Tree (Hard)", CFC 78 "the Striking Tree (Extreme)" |  |
| 45 Akh Afah Amphitheatre (Extreme) | keine ContentFinderCondition mit diesem Namen | CFC 79 "the Akh Afah Amphitheatre (Hard)", CFC 80 "the Akh Afah Amphitheatre (Extreme)" |  |
| 46 The Whorleater (Hard oder Extreme) | keine ContentFinderCondition mit diesem Namen | CFC 72 "the Whorleater (Hard)", CFC 73 "the Whorleater (Extreme)" |  |
| 49 Masked Carnivale: 29 | Masked-Carnivale-Stage, kein eindeutiger Spieleintrag |  |  |
| 58 Thornmarch (Hard oder Extreme) | keine ContentFinderCondition mit diesem Namen | CFC 66 "Thornmarch (Hard)", CFC 67 "Thornmarch (Extreme)" |  |
| 61 Masked Carnivale: 24 | Masked-Carnivale-Stage, kein eindeutiger Spieleintrag |  |  |
| 72 Thok ast Thok (Hard oder Extreme) | keine ContentFinderCondition mit diesem Namen | CFC 86 "Thok ast Thok (Hard)", CFC 87 "Thok ast Thok (Extreme)" |  |
| 108 Masked Carnivale: 08 | Masked-Carnivale-Stage, kein eindeutiger Spieleintrag |  |  |

## SeString-Makros in AozActionTransient.Description (DE/EN/FR/JA)

Zur Kontrolle der Klartext-Bereinigung im Plugin (GameTextCleaner).

- Italic: 2
- NewLine: 373
- NonBreakingSpace: 24
- SoftHyphen: 131
