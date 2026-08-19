# BLUnion 

A Dalamud plugin for FINAL FANTASY XIV that helps groups of Blue Mages figure out what to learn next, together.

If you've ever tried to organize a BLU spell-hunting session with friends, you know the drill: someone asks "wait, who still needs Diamondback?", nobody remembers, and you end up cross-referencing spellbooks by hand. BLUnion automates that part.

It reads your own learned spells directly from the game (no manual entry), lets you compare progress with your party, and shows you where to actually get the spells you're missing - monster, zone, coordinates if known. There's also a "Learning Plan" tab that groups missing spells by monster, so if one enemy teaches three things your group needs, you'll see that at a glance instead of hunting spell by spell.

This is still an early build I'm actively working on with a small group of friends testing it. Expect rough edges.

## What it does

- Detects your party and who in it is a Blue Mage
- Reads your own spell status automatically (via Dalamud's `IUnlockState`)
- Compares multiple players and sorts missing spells by how many people actually need them
- Shows monster/zone/coordinates for 122 of the 124 spells (the other 2 either need no source or the source data is still incomplete)
- Filter by name or spell number (`58`, `#058`, whatever)
- Option to hide totem-only spells if you're not chasing those right now
- Full UI in German, English, French, and Japanese
- Sync between players via a short export/import code - no account, no server
- A [browser companion](https://letsi-ma.github.io/BLUnion/) for people who don't want to install the plugin at all

## How sync works

You export your status as a short code and send it however you want - Discord, whatever. Someone else pastes it into the plugin and instantly sees the comparison. No backend, nothing stored anywhere but locally on your own machine.

If you don't have (or don't want) the Dalamud plugin, the [companion site](https://letsi-ma.github.io/BLUnion/) does the same thing in your browser - generate a code, read someone else's, done.

## Installing it

The easiest way is via the custom plugin repository:

1. In-game: `/xlsettings` → Experimental → Custom Plugin Repositories
2. Add: `https://raw.githubusercontent.com/letsI-MA/BLUnion/main/pluginmaster.json`
3. Save. BLUnion now shows up under Available Plugins in `/xlplugins`, updates included.

If you'd rather build it yourself: clone the repo, `dotnet build`, then point Dalamud's Dev Plugin Locations at the resulting DLL.

Either way, `/blunion` opens the window.

## Where the data comes from

Spell names, icons, and their order come straight from the game files via [Lumina](https://github.com/NotAdam/Lumina). Monster and source-location info was put together with help from [FFXIV Collect](https://ffxivcollect.com/)'s public API, with some gaps filled in from [Icy Veins](https://www.icy-veins.com/ffxiv/blue-mage-pve-dps-spell-summary). A handful of monster/zone names aren't translated into all four languages yet - those fall back to English until I get around to it.

## Rough architecture

```
Plugin
  ├── PartyService            – party / Blue Mage detection
  ├── LocalSpellUnlockService – reads your own spell status
  ├── SpellDataService        – spell/monster/source/location data
  ├── ComparisonService       – comparison & monster grouping
  ├── SpellFilter             – name/number filtering
  ├── ISyncProvider           – export/import code sync
  ├── UiStrings               – all the translated UI text
  └── UI (MainWindow)         – Party / Comparison / Learning Plan / Sync / Settings
```

Game data lives separately from your own settings, on purpose.

## Known gaps

- 2 of 124 spells don't have full source info yet
- Some monster/location names are still English-only placeholders
- Sync is code-based only for now - no live/automatic sync yet

## License

See [LICENSE](LICENSE).

# Unite. Learn. Mimic.

---

Unofficial fan project, not affiliated with SQUARE ENIX. FINAL FANTASY XIV © SQUARE ENIX CO., LTD.
