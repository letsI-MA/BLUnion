<p align="center"><img src="images/icon.png" width="160" alt="BLUnion Logo"></p>

# BLUnion

Ein Dalamud-Plugin für FINAL FANTASY XIV, das Gruppen von Blaumagiern hilft, gemeinsam herauszufinden, was sie als Nächstes lernen.

<p align="center">
  <a href="#installation"><b>Installieren</b></a> ·
  <a href="#was-es-kann">Funktionen</a> ·
  <a href="#so-funktioniert-der-sync">Sync</a> ·
  <a href="#gruppenfinder">Gruppenfinder</a> ·
  <a href="#was-gespeichert-wird">Datenschutz</a> ·
  <a href="#discord">Discord</a> ·
  <a href="#bekannte-lücken">Bekannte Lücken</a>
</p>

<p align="center">
  <a href="README.md">English</a> ·
  <b>Deutsch</b> ·
  <a href="README.fr.md">Français</a> ·
  <a href="README.ja.md">日本語</a>
</p>

> Dies ist eine Übersetzung der [englischen README](README.md). Sie kann hinter der englischen Fassung zurückliegen - im Zweifel gilt die englische Version.

Wer schon mal versucht hat, mit Freunden eine Spell-Jagd als Blaumagier zu organisieren, kennt das: Jemand fragt „Moment, wer braucht eigentlich noch Diamondback?“, keiner weiß es mehr, und am Ende vergleicht man Spellbooks von Hand. Genau diesen Teil übernimmt BLUnion.

Das Plugin liest deine gelernten Spells direkt aus dem Spiel (ohne Abtippen), vergleicht deinen Fortschritt mit deiner Party und zeigt dir, wo du die fehlenden Spells tatsächlich bekommst - Monster, Gebiet und, falls bekannt, Koordinaten. Dazu gibt es den Tab „Lernplan“, der fehlende Spells nach Monster gruppiert: Wenn ein Gegner drei Spells beibringt, die deine Gruppe braucht, siehst du das auf einen Blick, statt Spell für Spell zu suchen.

Angefangen hat es als Tool für mich und ein paar Freunde. Inzwischen nutzen es auch andere, was mich sehr freut - es bleibt aber ein Hobbyprojekt, an dem ich in meiner Freizeit arbeite. Hier und da kann es also noch etwas holpern.

## Was es kann

- Erkennt deine Party und wer darin Blaumagier ist
- Liest deinen eigenen Spell-Status automatisch aus (über Dalamuds `IUnlockState`)
- Übersicht mit dem durchschnittlichen Fortschritt deiner Party und einem „nächsten besten Ziel“ - dem Spell, der den meisten Leuten auf einmal hilft
- Vergleicht mehrere Spieler und sortiert fehlende Spells danach, wie viele sie wirklich brauchen
- Spellbook mit allen 124 Spells, filterbar nach gelernt/fehlend
- Loadouts für alle 32 Stages der Großen Maskerade sowie Rollen-Loadouts (Verteidiger, Heiler, Off-Heiler, Angreifer), jeweils mit Anzeige, wie viele der benötigten Spells du schon hast
- Zeigt für jeden Spell, wo du ihn bekommst - Monster, Gebiet oder Inhalt und für die meisten Open-World-Quellen auch Kartenkoordinaten
- Klickbare Orte: Ein Inhaltsname öffnet den Duty Finder mit diesem Inhalt ausgewählt (bei Sammeleinträgen wie „Thornmarch (Hard oder Extreme)“ wählst du zuerst die Variante), ein Open-World-Gebiet teleportiert dich zum passenden Aetheryten und setzt eine Markierung auf der Karte. Karten-Symbole öffnen die Karte mit Markierung oder schreiben den Kartenlink in dein eigenes Chatfenster
- Filter nach Name oder Spell-Nummer (`58`, `#058`, wie du willst)
- Option, reine Totem-Spells auszublenden, wenn du die gerade nicht jagst
- Gruppenfinder, um andere Blaumagier auf deinem Data Center zu finden
- Sync per kurzem Export-/Import-Code oder optional per Live-Sync, damit ihr gar keine Codes mehr tauschen müsst
- Komplette Oberfläche auf Deutsch, Englisch, Französisch und Japanisch
- Eine [Browser-Version](https://letsi-ma.github.io/BLUnion/) für alle, die das Plugin gar nicht installieren möchten

### Was es im Spiel tut (und was nicht)

BLUnion automatisiert kein Gameplay. Ein Klick im Plugin löst immer nur genau eine Sache aus, und nur wenn du klickst:

- **Inhaltsnamen** öffnen das Duty-Finder-Fenster mit dem Inhalt ausgewählt. Anmelden musst du dich weiterhin selbst.
- **Open-World-Gebiete** teleportieren dich einmal zum Aetheryten, der dem Monster am nächsten ist (oder zum Haupt-Aetheryten des Gebiets), und setzen dann die Kartenmarkierung. Das kostet Gil wie über das Teleport-Fenster, wird vom Spiel überall blockiert, wo Teleportieren nicht erlaubt ist (Inhalte, Kampf, Handwerk ...), und wird nie wiederholt. Du willst das nicht? Schalte in den Settings „Teleport per Klick auf Ortsnamen“ aus, dann sind die Namen reiner Text.
- **Karten-Symbole** öffnen die Karte mit Markierung oder schreiben den Kartenlink in dein eigenes Chatfenster. Dabei wird nichts an andere gesendet.

## So funktioniert der Sync

Es gibt zwei Wege, nimm den, der dir lieber ist.

Standard sind Codes. Du exportierst deinen Status als kurzen `BLU:...`-Code und schickst ihn, wie du willst - Party-Chat, Discord, egal. Standardmäßig erkennt das Plugin Codes, die in deinem Chat auftauchen, und nimmt sie automatisch in den Vergleich auf. In einer Party heißt es also meist nur: „Alle einmal auf Exportieren klicken“. Das kannst du in den Settings abschalten. Dabei ist kein Server beteiligt, alles bleibt auf deinem Rechner.

Wenn du in den Settings Live-Sync einschaltest, wird dein Spell-Status auf einen kleinen Server hochgeladen, und das Plugin holt sich den Status der anderen Blaumagier in deiner Party selbst. Keine Codes mehr, und neue Spells sehen die anderen kurz nachdem du sie gelernt hast.

Ohne das Dalamud-Plugin macht die [Browser-Version](https://letsi-ma.github.io/BLUnion/) dasselbe im Browser - Code erzeugen, fremde Codes lesen oder den Gruppenfinder nutzen.

## Gruppenfinder

Baut auf Live-Sync auf. Du kannst dich in eine öffentliche Liste für dein Data Center eintragen, mit einer kurzen Notiz, wann du meistens spielst (morgens, abends, Wochenende, ...), wie viele Mitspieler du suchst und welche Spells du farmen willst. Andere können die Liste durchsuchen, nach den Spells filtern, die sie brauchen, und sehen sofort, was sie zu einer Gruppe beitragen würden und was ihnen noch fehlen würde. Ein Klick nimmt einen Spieler in deinen Vergleich auf.

Wenn du zustimmst, deinen Online-Status zu teilen, sehen andere in der Liste, ob du gerade online bist.

Im Moment enthält der Gruppenfinder nur Einträge einzelner Spieler - Gruppeneinträge sind abgeschaltet.

Öffentlich ist nur, was du selbst veröffentlichst, und du kannst deinen Eintrag jederzeit löschen.

Charakternamen im Gruppenfinder werden nicht überprüft - theoretisch könnte jemand unter fremdem Namen veröffentlichen. Wenn dir ein gefälschter Eintrag auffällt oder jemand deinen Namen verwendet, melde es auf dem [Discord](https://discord.gg/uGW7kBG67), dann wird er entfernt.

## Was gespeichert wird

Nur relevant, wenn du Live-Sync oder den Gruppenfinder nutzt - sonst verlässt nichts deinen PC.

- Charaktername, Welt und Data Center
- Welche Spells du gelernt hast (als kompakte Bitmaske)
- Wenn du einen Gruppenfinder-Eintrag veröffentlichst: deine Notiz, Verfügbarkeit, gewünschte Mitspielerzahl und Ziel-Spells
- Ein gehashter Bearbeitungs-Token, damit nur du dein Profil ändern oder löschen kannst
- Nur wenn du „Online-Status teilen“ einschaltest (standardmäßig aus) und deinen Eintrag im Gruppenfinder listest: ob du online bist und wann du zuletzt gesehen wurdest, auf 15 Minuten gerundet. Das Signal kommt nur von deinem eigenen Plugin (Login, Logout und einmal pro Stunde, während du spielst). Diese Daten werden zusammen mit deinem Profil gelöscht, wenn du die Option ausschaltest oder nach 30 Tagen ohne Signal

Beim ersten Öffnen fragt das Plugin, ob du Live-Sync oder nur manuelle Codes nutzen möchtest; das (und den Online-Status) kannst du jederzeit in den Settings ändern.

Profile laufen nach 90 Tagen ohne Aktualisierung von selbst ab, und „Mein Profil löschen“ in den Settings entfernt deins sofort. Namen werden nicht mit dem Spiel abgeglichen; Missbrauch (z. B. ein Profil unter deinem Namen, das nicht von dir ist) kannst du auf dem [Discord](https://discord.gg/uGW7kBG67) melden. Das Backend ist ein Cloudflare Worker, der Code liegt in [`worker/`](worker/).

## Installation

Am einfachsten über das eigene Plugin-Repository:

1. Im Spiel: `/xlsettings` → Experimental → Custom Plugin Repositories
2. Hinzufügen: `https://raw.githubusercontent.com/letsI-MA/BLUnion/main/pluginmaster.json`
3. Speichern. BLUnion erscheint jetzt unter den verfügbaren Plugins in `/xlplugins`, Updates inklusive.

Lieber selbst bauen? Repo klonen, `dotnet build`, dann in Dalamuds Dev Plugin Locations auf die erzeugte DLL zeigen.

In beiden Fällen öffnet `/blunion` das Fenster.

## Woher die Daten stammen

Spellnamen, Monster- und Ortsnamen werden zur Laufzeit in deiner gewählten Sprache direkt aus den Spieldateien gelesen. Details zu den Quellen stehen in der [englischen README](README.md#where-the-data-comes-from).

## Discord

Es gibt einen Community-Discord: https://discord.gg/uGW7kBG67.

Jeder öffentliche Gruppenfinder-Eintrag bekommt dort automatisch eine eigene Karte in einem von vier Regions-Kanälen (Nordamerika, Europa, Japan, Ozeanien). Außerdem gibt es zwei Slash-Befehle: `/blunion browse` zeigt die Liste direkt in Discord, `/blunion online` zeigt, wer auf einem Data Center gerade online ist (nur Einträge, die ihren Online-Status teilen). Discord ist vorerst nur zum Lesen da - Einträge erstellen kannst du dort nicht.

## Bekannte Lücken

- Kartenkoordinaten für Open-World-Quellen werden von Hand gepflegt (sie lassen sich nicht aus den Spieldateien lesen), deshalb fehlen sie noch bei ein paar Orten
- Neue Gruppenfinder-Einträge können bis zu etwa einer Minute brauchen, bis andere sie sehen (das liegt am Speicher dahinter, ist also kein Bug)

## Lizenz

Siehe [LICENSE](LICENSE).

# Unite. Learn. Mimic.

---

Inoffizielles Fanprojekt, nicht mit SQUARE ENIX verbunden. FINAL FANTASY XIV © SQUARE ENIX CO., LTD.
