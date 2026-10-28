<p align="center"><img src="images/icon.png" width="160" alt="BLUnion logo"></p>

# BLUnion

A Dalamud plugin for FINAL FANTASY XIV that helps groups of Blue Mages figure out what to learn next, together.

<p align="center">
  <a href="#installing-it"><b>Install</b></a> ·
  <a href="#what-it-does">Features</a> ·
  <a href="#how-sync-works">Sync</a> ·
  <a href="#group-finder">Group Finder</a> ·
  <a href="#what-gets-stored">Privacy</a> ·
  <a href="#discord-integration">Discord</a> ·
  <a href="#known-gaps">Known gaps</a>
</p>

<p align="center">
  <b>English</b> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="README.fr.md">Français</a> ·
  <a href="README.ja.md">日本語</a>
</p>

If you've ever tried to organize a BLU spell-hunting session with friends, you know the drill: someone asks "wait, who still needs Diamondback?", nobody remembers, and you end up cross-referencing spellbooks by hand. BLUnion automates that part.

It reads your own learned spells directly from the game (no manual entry), lets you compare progress with your party, and shows you where to actually get the spells you're missing - monster, zone, coordinates if known. There's also a "Learning Plan" tab that groups missing spells by monster, so if one enemy teaches three things your group needs, you'll see that at a glance instead of hunting spell by spell.

It started out as a tool for me and a few friends. By now other people are using it too, which is great, but it's still a hobby project I work on in my free time. Expect the occasional rough edge.

## What it does

- Detects your party and who in it is a Blue Mage
- Reads your own spell status automatically (via Dalamud's `IUnlockState`)
- Overview page with your party's average progress and a "next best target" - the spell that helps the most people at once
- Compares multiple players and sorts missing spells by how many people actually need them
- Spellbook view with all 124 spells, filterable by learned/missing
- Loadouts for all 32 Masked Carnivale stages plus role loadouts (Tank, Healer, Off-Healer, Damage Dealer), including how many of the needed spells you already have
- Shows where to get every spell - monster, zone or duty, and map coordinates for most open-world sources
- Clickable locations: a duty name opens the Duty Finder with that duty selected (for grouped entries like "Thornmarch (Hard or Extreme)" you pick the variant first), an open-world zone name teleports you to the matching aetheryte and flags the monster on your map. Map icons open the map with a flag or post the map link into your own chat window
- Filter by name or spell number (`58`, `#058`, whatever)
- Option to hide totem-only spells if you're not chasing those right now
- Group Finder to find other Blue Mages on your data center
- Sync via a short export/import code, or optionally via Live-Sync so you don't have to swap codes at all
- Full UI in German, English, French, and Japanese
- A [browser companion](https://letsi-ma.github.io/BLUnion/) for people who don't want to install the plugin at all

### What it does in the game (and what it doesn't)

BLUnion doesn't automate gameplay. Clicking in the plugin only ever triggers one thing, and only when you click:

- **Duty names** open the Duty Finder window with the duty selected. You still queue yourself.
- **Open-world zone names** teleport you once to the aetheryte closest to the monster (or the zone's main aetheryte), then set the map flag. It costs gil just like the teleport window, is blocked by the game wherever teleporting isn't allowed (duties, combat, crafting ...), and is never retried. Don't want it? Turn off "Teleport by clicking a location name" in the settings and names become plain text.
- **Map icons** open the map with a flag, or print the map link into your own chat window. Nothing is sent to anyone.

## How sync works

There are two ways, use whichever you like.

The default is codes. You export your status as a short `BLU:...` code and send it however you want - party chat, Discord, whatever. By default the plugin picks up codes that show up in your chat and adds them to the comparison automatically, so in a party it's usually just "everyone click export once". You can turn that off in the settings. No server involved, everything stays on your own machine.

If you turn on Live-Sync in the settings, your spell status gets uploaded to a small server and the plugin fetches the status of the other Blue Mages in your party on its own. No more codes, and new spells show up for the others shortly after you learn them.

If you don't have (or don't want) the Dalamud plugin, the [companion site](https://letsi-ma.github.io/BLUnion/) does the same thing in your browser - generate a code, read someone else's, or use the Group Finder.

## Group Finder

Built on top of Live-Sync. You can put yourself into a public list for your data center, with a short note, when you usually play (morning, evening, weekend, ...), how many people you're looking for, and which spells you want to farm. Others can browse that list, filter it by the spells they need, and see right away what they would bring to a group and what they would still be missing. One click adds a player to your comparison.

If you opt in to sharing your online status, others browsing the list can see whether you're currently online.

For now the Group Finder only has entries for single players - group listings are switched off.

Nothing is public unless you actually publish an entry, and you can delete it at any time.

Character names in the Group Finder are not verified - anyone could publish under someone else's name. If you spot a fake entry or someone is using your name, report it on the [Discord](https://discord.gg/uGW7kBG67) and it will be removed.

## What gets stored

Only relevant if you use Live-Sync or the Group Finder - otherwise nothing leaves your PC.

- Character name, world, and data center
- Which spells you've learned (as a compact bitmask)
- If you publish a Group Finder entry: your note, availability, wanted player count, and target spells
- A hashed edit token, so only you can change or delete your profile
- Only if you turn on "Share online status" (off by default) and list your entry in the Group Finder: whether you're online and when you were last seen, rounded to 15 minutes. The signal comes only from your own plugin (login, logout, and once an hour while you play). This data is deleted together with your profile, when you switch the option off, or after 30 days without a signal

When you first open the plugin it asks whether you want Live-Sync or manual codes only; you can change that (and the online status) any time in the settings.

Profiles expire on their own after 90 days without an update, and "Delete my profile" in the settings removes yours right away. Names are not verified against the game; misuse (e.g. a profile under your name that isn't yours) can be reported on the [Discord](https://discord.gg/uGW7kBG67). The backend is a Cloudflare Worker, the code is in [`worker/`](worker/).

## Installing it

The easiest way is via the custom plugin repository:

1. In-game: `/xlsettings` → Experimental → Custom Plugin Repositories
2. Add: `https://raw.githubusercontent.com/letsI-MA/BLUnion/main/pluginmaster.json`
3. Save. BLUnion now shows up under Available Plugins in `/xlplugins`, updates included.

If you'd rather build it yourself: clone the repo, `dotnet build`, then point Dalamud's Dev Plugin Locations at the resulting DLL.

Either way, `/blunion` opens the window.

## Where the data comes from

Spell names, icons, and their order come straight from the game files via [Lumina](https://github.com/NotAdam/Lumina). Monster and source-location info was put together with help from [FFXIV Collect](https://ffxivcollect.com/)'s public API, with some gaps filled in from [Icy Veins](https://www.icy-veins.com/ffxiv/blue-mage-pve-dps-spell-summary). Spell names and descriptions, monster names, and zone/duty names are read from the game files in your chosen display language at runtime, using game IDs that [`tools/data-enrich`](tools/data-enrich/) adds to the data files. Where there's no game match, the hand-maintained translations are used instead (English as the last resort).

Game data lives separately from your own settings, on purpose.

## Discord integration

There's a community Discord at https://discord.gg/uGW7kBG67.

Every public Group Finder entry also gets its own card in one of four region channels (North America, Europe, Japan, Oceania). The cards are created, updated, and removed automatically along with the entry itself. There are also two slash commands: `/blunion browse` to check the list from Discord, and `/blunion online` to see who on a data center is online right now (only entries that share their online status). For now Discord is read-only - you can't create entries from there. See [DISCORD_INTEGRATION.md](DISCORD_INTEGRATION.md) for setup/registration/testing.

## Known gaps

- Map coordinates for open-world sources are hand-maintained (they can't be read from the game files), so a few locations still don't have any
- New Group Finder entries can take up to about a minute to show up for others (that's how the storage behind it works, not a bug)

## License

See [LICENSE](LICENSE).

# Unite. Learn. Mimic.

---

Unofficial fan project, not affiliated with SQUARE ENIX. FINAL FANTASY XIV © SQUARE ENIX CO., LTD.
