# BLUnion

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for FINAL FANTASY XIV that helps Blue Mage parties plan together which spells to learn next – including source locations and a web companion for players without the plugin installed.

> ⚠️ **Early development stage.** This project is being actively developed for a small test group.

## What does BLUnion do?

When several Blue Mages are in a party together, BLUnion shows:

- **Who already has which spells learned** – detected automatically, no manual entry needed
- **Which spells the group is still missing**, prioritized by how many players need them
- **Where to learn a missing spell** (monster, zone/dungeon/trial, coordinates where known)
- **Which monster covers several missing spells at once** (the "Learning Plan" tab), so the group can plan efficiently

The plugin does **not** automate any gameplay – it only reads and compares information.

## Features

- Party and Blue Mage detection
- Automatic spell status detection via the official (experimental) Dalamud API `IUnlockState`
- Multi-player comparison, sorted by urgency
- Monster/source location database for all 124 Blue Magic spells
- Filter by name or spellbook number (`58`, `#058`, `a`, ...)
- "Hide totems" filter for the source display
- Fully localized UI: **German, English, French, Japanese**
- Sync between players via export/import codes – no server required
- A companion **website** (see below), so people without the plugin installed can still contribute their status

## Sync without a server

Each player can export their own spell status as a compact text code (`BLU:...`) and share it with others, e.g. via Discord. Others import the code in the plugin and immediately see the comparison – no central server, no registration required. (Older `BLU1:...` codes from before this format change can still be imported during the transition period, but are no longer generated.)

For players who don't want to (or can't) install a Dalamud plugin, there's a web companion at **[letsi-ma.github.io/BLUnion](https://letsi-ma.github.io/BLUnion/)**, which can generate and read the same code directly in the browser – without FFXIV even running.

## Installation

**Recommended: via custom plugin repository**

1. In-game, open `/xlsettings` → *Experimental* → *Custom Plugin Repositories*.
2. Paste this URL into an empty field and confirm:
   ```
   https://raw.githubusercontent.com/letsI-MA/BLUnion/main/pluginmaster.json
   ```
3. Save. BLUnion will now show up under *Available Plugins* in `/xlplugins` and can be installed like any other plugin, including future updates.

**Alternative: building from source (dev plugin)**

1. Build the project (see below).
2. In `/xlsettings` → *Experimental* → *Dev Plugin Locations*, add the path to the built `BLUnion.dll`.
3. Enable the plugin via `/xlplugins`.

Either way, open the main window with `/blunion`.

## Build

Requires a local Dalamud installation (`DALAMUD_HOME`) and the .NET SDK matching the installed Dalamud API version.

```
dotnet build
```

## Data sources & credits

Spell, monster, and source-location data comes from several sources:

- Spell names, icons, and ordering: pulled directly from the game files (via [Lumina](https://github.com/NotAdam/Lumina)/Dalamud)
- Monster/source-location mappings: curated with the help of the public API from [FFXIV Collect](https://ffxivcollect.com/) (non-commercial use)
- Supplementary research: [Icy Veins](https://www.icy-veins.com/ffxiv/blue-mage-pve-dps-spell-summary)

Not all monster/location names have been verified in every supported language yet – missing translations are marked as placeholders in the code (falling back to English).

## Architecture

```
Plugin
  ├── PartyService            – party / Blue Mage detection
  ├── LocalSpellUnlockService – own spell status (IUnlockState)
  ├── SpellDataService        – loads spell/monster/source/location data
  ├── ComparisonService       – comparison & monster grouping
  ├── SpellFilter             – name/number filtering
  ├── ISyncProvider           – swappable sync strategy
  │     └── ManualCodeSyncProvider (export/import code)
  ├── UiStrings                – centralized, localized UI strings
  └── UI
        └── MainWindow – Party / Spell Comparison / Learning Plan / Sync / Settings
```

Static game data (`Data/*.json`) is intentionally kept separate from user settings.

## Known limitations

- 2 of 124 spells have no source listed (Kaltstrahl, partially) or need none (Water Cannon)
- Some monster/location names are still placeholders for certain languages
- Sync is currently manual (code-based) only, no automatic live sync

## License

See [LICENSE](LICENSE).

---

*This is an unofficial fan project and is not affiliated with SQUARE ENIX. FINAL FANTASY XIV © SQUARE ENIX CO., LTD.*