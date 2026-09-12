#!/usr/bin/env node
/**
 * Registriert "/blunion browse" als GUILD-Command (NICHT global, siehe DISCORD_INTEGRATION.md
 * Punkt "Noch KEINE globale Command-Registrierung") für genau EINEN Testserver. Bewusst ein
 * eigenständiges Node-Skript statt Teil des Workers: läuft einmalig lokal von Hand, braucht dafür
 * den Bot-Token deiner Discord-Application - der NIE im Worker/in KV/in wrangler.toml landet, nur
 * hier kurz aus der lokalen Umgebung gelesen (siehe Aufgabenstellung "keine Secrets im Code").
 *
 * Guild-Commands (anders als globale Commands) sind SOFORT nach dem Registrieren nutzbar - genau
 * das richtige Verhalten für einen Testserver während der Entwicklung. Eine spätere globale
 * Registrierung (für alle Server, auf denen der Bot läuft) ist explizit NICHT Teil dieser Phase.
 *
 * Erwartet folgende Umgebungsvariablen (siehe DISCORD_INTEGRATION.md für die Portal-Fundstellen):
 *   DISCORD_APPLICATION_ID  - "General Information" -> "Application ID"
 *   DISCORD_BOT_TOKEN       - "Bot"-Tab -> "Reset Token" / "Token"
 *   DISCORD_TEST_GUILD_ID   - Rechtsklick auf deinen Testserver in Discord -> "ID kopieren"
 *                             (dafür muss in Discord unter Einstellungen -> Erweitert der
 *                             "Entwicklermodus" aktiviert sein)
 *
 * Aufruf (PowerShell):
 *   $env:DISCORD_APPLICATION_ID = "..."
 *   $env:DISCORD_BOT_TOKEN = "..."
 *   $env:DISCORD_TEST_GUILD_ID = "..."
 *   node worker/scripts/register-discord-commands.mjs
 *
 * Aufruf (bash):
 *   DISCORD_APPLICATION_ID=... DISCORD_BOT_TOKEN=... DISCORD_TEST_GUILD_ID=... \
 *     node worker/scripts/register-discord-commands.mjs
 *
 * Nutzt ausschließlich globales fetch() (Node 18+, siehe worker/package.json-Voraussetzungen für
 * wrangler/vitest ohnehin) - keine zusätzliche Abhängigkeit.
 */

const { DISCORD_APPLICATION_ID, DISCORD_BOT_TOKEN, DISCORD_TEST_GUILD_ID } = process.env;

if (!DISCORD_APPLICATION_ID || !DISCORD_BOT_TOKEN || !DISCORD_TEST_GUILD_ID) {
  console.error(
    "Bitte DISCORD_APPLICATION_ID, DISCORD_BOT_TOKEN und DISCORD_TEST_GUILD_ID als Umgebungsvariablen " +
      "setzen (siehe Kommentar am Dateianfang bzw. DISCORD_INTEGRATION.md).",
  );
  process.exit(1);
}

// "/blunion browse [datacenter] [type]" - als Sub-Command unter dem Top-Level-Command "blunion"
// (type 1 = SUB_COMMAND), damit eine spätere Phase 2 (z.B. "/blunion link") denselben
// Top-Level-Command um weitere Sub-Commands ergänzen kann, ohne einen bestehenden Command zu
// ersetzen/zu brechen. "type" (Gruppen/Spieler) ist eine STRING-Option mit choices statt eines
// eigenen Sub-Commands - dieselbe Kernlogik (browse), nur ein anderer Ergebnis-/Embed-Typ.
const commandDefinition = {
  name: "blunion",
  description: "BLUnion Gruppenfinder",
  options: [
    {
      name: "browse",
      description: "Öffentlich gelistete Blue-Mage-Gruppen oder Spieler-Gesuche auf einem Data Center anzeigen",
      type: 1, // SUB_COMMAND
      options: [
        {
          name: "datacenter",
          description: 'Data Center, z.B. "Aether", "Chaos", "Light" (optional)',
          type: 3, // STRING
          required: false,
        },
        {
          name: "type",
          description: 'Was anzeigen: Gruppen (Standard) oder einzelne Spieler-Gesuche (optional)',
          type: 3, // STRING
          required: false,
          choices: [
            { name: "Gruppen", value: "groups" },
            { name: "Spieler", value: "players" },
          ],
        },
      ],
    },
  ],
};

const url =
  `https://discord.com/api/v10/applications/${DISCORD_APPLICATION_ID}/guilds/${DISCORD_TEST_GUILD_ID}/commands`;

const response = await fetch(url, {
  method: "POST",
  headers: {
    "Content-Type": "application/json",
    Authorization: `Bot ${DISCORD_BOT_TOKEN}`,
  },
  body: JSON.stringify(commandDefinition),
});

const body = await response.json();

if (!response.ok) {
  console.error(`Discord-API-Fehler (${response.status}):`, body);
  process.exit(1);
}

console.log("Guild-Command erfolgreich registriert:");
console.log(JSON.stringify(body, null, 2));
