/**
 * Discord-Webhook-HTTP-Hilfsfunktionen für die persistenten Gruppen-Karten (Phase 1.5, siehe
 * DISCORD_INTEGRATION.md) - eigenes Modul analog zu crypto.ts/worlds.ts (reine Helfer ohne
 * Env-Zugriff, nur rohe HTTP-Calls gegen die Discord-Webhook-API, siehe
 * https://discord.com/developers/docs/resources/webhook#execute-webhook).
 *
 * WICHTIG (siehe Aufgabenstellung Punkt 3): KEINE dieser Funktionen wirft bei einem Discord-API-
 * Fehler (4xx/5xx, z.B. 404 weil die Nachricht bereits von Hand im Kanal gelöscht wurde) oder
 * einem Netzwerkfehler - Discord ist für den eigentlichen Gruppen-PUT/DELETE reine
 * Zusatz-Funktionalität, keine Voraussetzung (siehe index.ts: syncGroupDiscordCard/
 * removeGroupDiscordCard laufen über ctx.waitUntil, NACHDEM die Antwort an Plugin/Website bereits
 * feststeht). Jeder Fehlschlag wird NUR geloggt (console.error, sichtbar in "wrangler tail"), der
 * Aufrufer bekommt stattdessen einen "hat nicht geklappt"-Rückgabewert (null/false) und macht mit
 * dem KV-Ergebnis normal weiter.
 */

/** Austauschbar für Tests (siehe worker/test/discordWebhook.test.ts) - echte Discord-Webhook-Calls
 * sind aus der workerd-Testlaufzeit heraus NICHT gegen einen lokal gestarteten Test-HTTP-Server
 * umleitbar (fetch() innerhalb von workerd kann keine vom Node-Testrunner-Prozess per node:http
 * gebundene Listener-Sockets erreichen - ausprobiert, schlägt mit "Network connection lost" fehl),
 * und ein echter Discord-Webhook/Netzwerkzugriff in automatisierten Tests wäre ohnehin unerwünscht
 * (Flakiness, echte Discord-Secrets nötig). Tests ersetzen diese Variable deshalb per
 * setDiscordFetchForTests() durch eine In-Memory-Fake-Implementierung, die Requests aufzeichnet
 * und kontrollierte Response-Objekte zurückgibt - Produktionscode ruft immer ganz normal das
 * globale fetch() auf. */
let discordFetch: typeof fetch = fetch;

/** NUR für Tests (siehe discordFetch-Doc oben) - ersetzt/setzt die verwendete fetch-Implementierung
 * zurück. Absichtlich der einzige Test-Hook in diesem Modul, statt fetchImpl durch jede
 * aufrufende Funktion in index.ts hindurchzureichen (das würde handleGroupPut/handleGroupDelete/
 * den Cron-Handler unnötig verkomplizieren, siehe Aufgabenstellung "keine unnötigen
 * Refactorings"). */
export function setDiscordFetchForTests(fetchImpl: typeof fetch): void {
  discordFetch = fetchImpl;
}

async function safeDiscordFetch(url: string, init: RequestInit, context: string): Promise<Response | null> {
  try {
    const response = await discordFetch(url, init);
    if (!response.ok) {
      const bodyText = await response.text().catch(() => "");
      console.error(`[discordWebhook] ${context} fehlgeschlagen: HTTP ${response.status} ${bodyText}`);
      return null;
    }
    return response;
  } catch (error) {
    console.error(`[discordWebhook] ${context} fehlgeschlagen (Netzwerkfehler):`, error);
    return null;
  }
}

/** Erstellt eine neue Nachricht über den gegebenen Webhook (POST .../webhooks/{id}/{token}) und
 * liefert deren Discord-Message-ID zurück (nötig, um sie später per editWebhookMessage/
 * deleteWebhookMessage wiederzufinden - siehe StoredGroupProfile.discordCard in index.ts).
 * "?wait=true" lässt Discord mit dem fertigen Message-Objekt (inkl. "id") statt nur 204 No Content
 * antworten. Liefert null bei jedem Fehlschlag (siehe Klassendoc oben) - der Aufrufer behandelt das
 * wie "keine Karte konnte angelegt werden", ohne selbst zu werfen. */
export async function createWebhookMessage(webhookUrl: string, embed: Record<string, unknown>): Promise<string | null> {
  const response = await safeDiscordFetch(
    `${webhookUrl}?wait=true`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ embeds: [embed] }),
    },
    "createWebhookMessage",
  );
  if (!response)
    return null;

  try {
    const body = await response.json() as { id?: unknown };
    return typeof body.id === "string" ? body.id : null;
  } catch {
    return null;
  }
}

/** Aktualisiert eine bestehende Webhook-Nachricht (PATCH .../webhooks/{id}/{token}/messages/{id}) -
 * für eine Gruppe, deren Karte schon existiert und sich nur inhaltlich geändert hat (siehe
 * syncGroupDiscordCard in index.ts: gleiche Region, discordCard bereits vorhanden). */
export async function editWebhookMessage(
  webhookUrl: string,
  messageId: string,
  embed: Record<string, unknown>,
): Promise<boolean> {
  const response = await safeDiscordFetch(
    `${webhookUrl}/messages/${encodeURIComponent(messageId)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ embeds: [embed] }),
    },
    "editWebhookMessage",
  );
  return response !== null;
}

/** Löscht eine bestehende Webhook-Nachricht (DELETE .../webhooks/{id}/{token}/messages/{id}) - u.a.
 * wenn eine Gruppe auf "unlisted" gesetzt/gelöscht wird, oder wenn eine verwaiste Karte per Cron
 * aufgeräumt wird (siehe removeGroupDiscordCard/cleanupOrphanedDiscordCards in index.ts). Ein 404
 * (Nachricht schon von Hand im Kanal gelöscht) zählt hier wie jeder andere Discord-Fehler NICHT als
 * harter Fehler (siehe safeDiscordFetch/Klassendoc oben) - der Aufrufer räumt seinen eigenen
 * Zustand (KV-Feld/Index-Eintrag) trotzdem auf. */
export async function deleteWebhookMessage(webhookUrl: string, messageId: string): Promise<boolean> {
  const response = await safeDiscordFetch(
    `${webhookUrl}/messages/${encodeURIComponent(messageId)}`,
    { method: "DELETE" },
    "deleteWebhookMessage",
  );
  return response !== null;
}

/** Extrahiert die Webhook-ID (erstes Pfadsegment nach "/webhooks/") aus einer vollständigen
 * Discord-Webhook-URL (".../api/webhooks/<id>/<token>") - für StoredGroupProfile.discordCard.
 * channelWebhookId (siehe index.ts): so kann sich ein KV-Eintrag merken, ÜBER welchen Webhook seine
 * Karte zuletzt gepostet wurde, OHNE den Token selbst (der Teil des Secrets ist) mit in KV
 * abzulegen. Liefert null bei einer nicht erkennbaren URL - der Aufrufer speichert dann einfach
 * einen leeren String (rein informativ, keine Logik hängt an einem konkreten Wert). */
export function extractWebhookId(webhookUrl: string): string | null {
  const match = webhookUrl.match(/\/webhooks\/(\d+)\//);
  return match ? match[1]! : null;
}
