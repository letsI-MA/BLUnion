/**
 * BLUnion Live-Sync Worker (Phase 1 + Phase 2 "Gruppenfinder").
 *
 * Ersetzt für den Alltagsfall "gemeinsam mit anderen Blue Mages in einer Party" den manuellen
 * BLU:-Code-Austausch (siehe ManualCodeSyncProvider.cs im Plugin) durch automatisches
 * Veröffentlichen/Abrufen des eigenen Spell-Status über diesen Worker + einen KV-Namespace.
 *
 * Bewusst OHNE Framework/Router-Bibliothek (siehe Aufgabenstellung "keine unnötigen
 * Abhängigkeiten") - für diese Endpunkte reicht ein einfacher switch/Regex-Abgleich völlig.
 *
 * Endpunkte (siehe README.md für Details/Beispiele):
 *   PUT    /profile/:world/:characterName   - eigenes Profil anlegen/aktualisieren
 *   GET    /profile/:world/:characterName   - Profil abrufen (öffentlich, kein Token nötig)
 *   DELETE /profile/:world/:characterName   - eigenes Profil löschen (X-Edit-Token-Header nötig)
 *   GET    /profiles/browse?dataCenter=<DC> - öffentlicher Gruppenfinder (Phase 2, siehe unten)
 *   PUT    /group/:groupId                  - Gruppen-Listung anlegen/aktualisieren (Phase 2)
 *   DELETE /group/:groupId                  - Gruppen-Listung löschen (X-Edit-Token-Header nötig)
 *   GET    /groups/browse?dataCenter=<DC>   - öffentlicher GRUPPEN-Gruppenfinder (Phase 2, siehe unten)
 *   POST   /discord/interactions            - Discord-Slash-Command "/blunion browse" (Phase 1,
 *                                              siehe DISCORD_INTEGRATION.md und
 *                                              handleDiscordInteractions unten) - rein lesend,
 *                                              reicht denselben Gruppen-Gruppenfinder oben nur als
 *                                              Discord-Embed formatiert durch, KEIN eigener
 *                                              Datenspeicher/keine neue KV-Struktur.
 *
 * RATE-LIMITING: die vier schreibenden Endpunkte (PUT/DELETE auf /profile und /group) sind seit
 * bestätigtem Fremd-Traffic auf dem öffentlichen, tokenlosen PUT über ein IP-basiertes
 * Rate-Limiting-Binding geschützt (siehe enforceWriteRateLimit + WRITE_RATE_LIMITER-Doc unten und
 * die Begründung in wrangler.toml). GET /profile sowie die beiden Browse-Endpunkte sind davon
 * BEWUSST ausgenommen - sie sind rein lesend/idempotent und die Browse-Endpunkte bereits über
 * withCache (siehe unten) vor wiederholter Last geschützt, ein zusätzliches Limit dort brächte
 * also kaum Zusatzschutz, aber das Risiko, legitime Nutzer beim Gruppenfinder-Auto-Polling
 * (siehe dortiger Kommentar) unnötig auszubremsen.
 *
 * KV-Key-Schema: "profile:<world>:<characterName>", beide Teile lowercased (siehe kvKey()) -
 * verhindert Duplikate durch abweichende Groß-/Kleinschreibung (das Spiel liefert Namen/Welten
 * nicht immer konsistent kapitalisiert). :world/:characterName in der URL sind
 * URI-komponenten-kodiert zu übergeben (Charakternamen können Leer-/Sonderzeichen enthalten).
 * Gruppen-Listungen liegen unter einem eigenen Key-Schema "group:<groupId>" (siehe groupKvKey()) -
 * groupId ist ein vom Client beim Erstellen generierter, zufälliger String (z.B.
 * crypto.randomUUID() im C#-Client), NICHT lowercased, weil es hier - anders als bei
 * world+characterName - keinen natürlichen Schlüssel gibt, den man auf diese Weise deduplizieren
 * müsste.
 *
 * WICHTIG (Phase 2 - Gruppenfinder ist KEIN separates Profil): visibility/availabilityTags/
 * note/wantedPlayerCount erweitern dasselbe StoredProfile, das schon Phase 1 anlegt. Es gibt
 * keinen eigenständigen "Gruppenfinder-Login" - Sichtbarkeit im Gruppenfinder setzt zwingend
 * voraus, dass für den Charakter bereits ein Live-Sync-Profil (mit editToken) existiert.
 *
 * WICHTIG (Gruppen-Listungen referenzieren bestehende Einzelprofile, statt sie zu duplizieren):
 * ein "group:"-Eintrag (siehe StoredGroupProfile) speichert je Mitglied NUR world+characterName,
 * KEINE eigene Kopie von dessen Spell-Bitmaske - die wird erst bei GET /groups/browse live aus
 * dem zugehörigen "profile:"-Eintrag jedes Mitglieds nachgeladen (siehe handleGroupsBrowse). Der
 * editToken einer Gruppen-Listung identifiziert dabei AUSSCHLIESSLICH deren Ersteller/
 * Veröffentlicher, NICHT die referenzierten Mitglieder (siehe Doc an handleGroupPut/
 * handleGroupDelete) - wer die Gruppe veröffentlicht, ist der einzige, der sie später
 * ändern/löschen kann.
 *
 * targetSpellIds (siehe StoredGroupProfile/isValidTargetSpellIds): die Spells, die die Gruppe
 * gemeinsam farmen möchte - reine IDs aus dem fest hinterlegten KNOWN_SPELL_IDS-Set (siehe
 * spellIds.ts), NICHT gegen die Mitgliederprofile abgeglichen (dieser Abgleich gegen den eigenen
 * Lernstatus passiert rein clientseitig, siehe GroupTargetSpellService im Plugin).
 *
 * Sowohl das Dalamud-Plugin ALS AUCH die Website (docs/index.html) sind Clients DIESES EINEN
 * Workers - kein zweites Backend, keine Parallelstruktur. Der optionale PUT-Body-Parameter
 * ttlHours (siehe resolveTtlSeconds) existiert eigens für die Website, die damit Profile mit
 * einer kürzeren als der Standard-Lebensdauer veröffentlichen kann.
 *
 * CACHING: GET /profiles/browse und GET /groups/browse sind jetzt für BROWSE_CACHE_TTL_SECONDS
 * serverseitig gecached (siehe withCache) - das Plugin pollt den Gruppenfinder-Tab automatisch
 * alle 15 Sekunden (siehe UI/MainWindow.cs GroupFinderAutoRefreshInterval), ohne diesen Cache
 * würde das direkt mit der Nutzerzahl skalieren (jeder offene Gruppenfinder-Tab = ein voller
 * KV-list()-Durchlauf alle 15s). PUT/DELETE bleiben UNVERÄNDERT live/ungecached - ein frisch
 * veröffentlichtes/gelöschtes Profil kann dadurch für bis zu BROWSE_CACHE_TTL_SECONDS Sekunden in
 * Browse-Ergebnissen leicht veraltet erscheinen. Bewusster Kompromiss (siehe Aufgabenstellung).
 */

import { base64UrlDecode, generateEditToken, hexToBytes, sha256Hex } from "./crypto";
import { createWebhookMessage, deleteWebhookMessage, editWebhookMessage, extractWebhookId } from "./discordWebhook";
import { lookupDataCenter } from "./worlds";
import { KNOWN_SPELL_IDS } from "./spellIds";

export interface Env {
  BLUNION_PROFILES: KVNamespace;
  /** Natives Workers-Rate-Limiting-Binding (siehe wrangler.toml-Doc und enforceWriteRateLimit
   * unten) - schützt AUSSCHLIESSLICH die vier schreibenden Endpunkte (PUT/DELETE auf /profile
   * und /group), nicht die GET-/Browse-Endpunkte (siehe dortige Begründung). */
  WRITE_RATE_LIMITER: RateLimit;
  /** Ed25519-Public-Key der Discord-Application (Hex-kodiert, exakt wie im Discord Developer
   * Portal unter "General Information -> Public Key" angezeigt) - einziges Geheimnis der neuen
   * Discord-Integration (siehe verifyDiscordSignature/handleDiscordInteractions unten und
   * DISCORD_INTEGRATION.md). In Produktion NIEMALS im Code/in wrangler.toml, sondern per
   *   wrangler secret put DISCORD_PUBLIC_KEY
   * gesetzt. Für lokale Entwicklung/Tests kommt der Wert stattdessen aus worker/.dev.vars (siehe
   * dortige .dev.vars.example) - ein von "wrangler secret put" gesetzter Wert überschreibt beim
   * Deploy einen gleichnamigen Eintrag aus wrangler.toml [vars] bzw. .dev.vars automatisch, das
   * ist genau das von Cloudflare dafür vorgesehene Muster (Dev-Default lokal, echtes Secret in
   * Produktion). */
  DISCORD_PUBLIC_KEY: string;
  /** Vier region-spezifische Discord-Webhook-URLs (Phase 1.5 "persistente Gruppen-Karten", siehe
   * DATA_CENTER_TO_REGION/syncGroupDiscordCard unten und DISCORD_INTEGRATION.md) - je EINE pro
   * FFXIV-Region (NICHT pro Data Center, siehe DATA_CENTER_TO_REGION-Doc), zeigt auf einen
   * Discord-Webhook im jeweiligen Regions-Kanal. Genau wie DISCORD_PUBLIC_KEY NIE im Code/in
   * wrangler.toml im Klartext, sondern per
   *   wrangler secret put DISCORD_WEBHOOK_NA   (analog für _EU/_JP/_OC)
   * gesetzt bzw. lokal über worker/.dev.vars (siehe .dev.vars.example).
   *
   * Anders als DISCORD_PUBLIC_KEY bewusst OPTIONAL: die Karten-Funktion ist reine
   * Zusatz-Funktionalität (siehe Aufgabenstellung "kein Discord-API-Fehler darf eine
   * Gruppen-Veröffentlichung/-Änderung/-Löschung fehlschlagen lassen") - eine (noch) nicht
   * konfigurierte Region lässt syncGroupDiscordCard für Gruppen auf dieser Region schlicht nichts
   * tun, statt den Worker zum Absturz zu bringen oder den PUT/DELETE abzulehnen. */
  DISCORD_WEBHOOK_NA?: string;
  DISCORD_WEBHOOK_EU?: string;
  DISCORD_WEBHOOK_JP?: string;
  DISCORD_WEBHOOK_OC?: string;
}

/** Feste Größe der Spell-Bitmaske - MUSS mit ManualCodeSyncProvider.BitmaskBytes im Plugin
 * übereinstimmen (16 Byte = 128 Bit, siehe dortiger Klassendoc). Anders als im "BLU:"-Sync-Code
 * enthält spellBitmaskBase64 hier NUR die Bitmaske (kein Namens-Präfixbyte) - der Charaktername
 * steht schon im URL-Pfad bzw. im gespeicherten JSON-Feld "characterName". */
const BITMASK_BYTES = 16;

/** 90 Tage in Sekunden - kompletter Cleanup-Mechanismus für Phase 1 (siehe README.md): wird bei
 * JEDEM Put neu gesetzt (KV expirationTtl ist relativ zum Put-Zeitpunkt), aktive Profile laufen
 * dadurch nie ab, inaktive verschwinden automatisch, kein Cron-Job nötig. Default, wenn im
 * PUT-Body kein (gültiges) ttlHours mitgeschickt wird (siehe resolveTtlSeconds) - genau das
 * bisherige Phase-1/2-Verhalten für das Plugin, das dieses Feld nie mitschickt. */
const PROFILE_TTL_SECONDS = 90 * 24 * 3600;

/** Optionales TTL-Override fürs PUT (siehe README.md/docs/index.html - die Website setzt hier
 * IMMER einen kürzeren Wert als die Plugin-Default-Lebensdauer, u.a. weil Testprofile/Kurzzeit-
 * Veröffentlichungen von der Web-Seite aus nicht 90 Tage lang herumliegen sollen). Bewusst
 * GECLAMPT statt mit 400 abgelehnt (siehe Aufgabenstellung: "das Feld ist eine Optimierung, kein
 * Sicherheitsfeature, muss also nicht hart validiert werden") - TTL_HOURS_MAX entspricht exakt
 * PROFILE_TTL_SECONDS (90 Tage), ein manipulierter Client kann sich über dieses Feld also KEINE
 * längere als die reguläre Lebensdauer erschleichen, nur eine kürzere anfordern. */
const TTL_HOURS_MIN = 1;
const TTL_HOURS_MAX = 2160; // 90 Tage * 24 Stunden, siehe PROFILE_TTL_SECONDS

/** TTL für das serverseitige Kurzzeit-Caching von GET /profiles/browse und GET /groups/browse
 * (siehe withCache) - bewusst etwas LÄNGER als das 15-Sekunden-Client-Polling-Intervall (siehe
 * UI/MainWindow.cs GroupFinderAutoRefreshInterval), damit mehrere kurz hintereinander
 * eintreffende Requests (von einem oder mehreren Nutzern) auf denselben Cache-Eintrag treffen,
 * statt jedes Mal neu über alle KV-Keys zu iterieren. */
const BROWSE_CACHE_TTL_SECONDS = 20;

const PROFILE_PATH = /^\/profile\/([^/]+)\/([^/]+)\/?$/;
const BROWSE_PATH = /^\/profiles\/browse\/?$/;

/** Analog zu PROFILE_PATH/BROWSE_PATH, aber für Gruppen-Listungen (siehe Klassendoc/
 * StoredGroupProfile) - :groupId ist EIN einzelnes Pfadsegment (kein "/", anders als world+
 * characterName bei PROFILE_PATH), da es sich um einen einzelnen opaken, vom Client generierten
 * String handelt. */
const GROUP_PATH = /^\/group\/([^/]+)\/?$/;
const GROUPS_BROWSE_PATH = /^\/groups\/browse\/?$/;

/** POST /discord/interactions - Discord-Slash-Command-Integration Phase 1 (siehe
 * DISCORD_INTEGRATION.md und handleDiscordInteractions unten). Rein lesend: reicht "/blunion
 * browse" an dieselbe handleGroupsBrowse-Kernlogik durch (siehe computeGroupsBrowse), KEIN
 * Linking/Schreiben (das ist explizit Phase 2, siehe dortige Doku). */
const DISCORD_INTERACTIONS_PATH = /^\/discord\/interactions\/?$/;

/** Ursprünglich für Phase 2 (Website/Gruppenfinder) vorbereitet, jetzt tatsächlich gebraucht
 * (siehe handleBrowse) - vom Plugin aus für die reinen /profile/-Endpunkte weiterhin nicht
 * zwingend nötig, kostet aber nichts, überall gesetzt zu sein. */
const CORS_HEADERS: Record<string, string> = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, PUT, DELETE, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, X-Edit-Token",
  "Access-Control-Max-Age": "86400",
};

/** Erlaubte Werte für availabilityTags (siehe handlePut/isValidAvailabilityTags) - bewusst
 * intern englisch gehalten, die Übersetzung für die UI passiert ausschließlich im Plugin über
 * UiStrings, nicht hier im Worker (siehe Aufgabenstellung). */
const ALLOWED_AVAILABILITY_TAGS = ["morning", "afternoon", "evening", "weekend", "flexible"] as const;

/** Serverseitige Obergrenze für "note" (siehe handlePut) - wird bei Überschreitung gekappt,
 * nicht abgelehnt (siehe Aufgabenstellung: "serverseitig kappen/validieren, nicht nur
 * clientseitig vertrauen" - das Plugin begrenzt die ImGui-Eingabe zwar schon selbst auf 60
 * Zeichen, ein direkter/manipulierter API-Aufruf könnte das aber umgehen). */
const NOTE_MAX_LENGTH = 60;

/** Sinnvoller Bereich für wantedPlayerCount (siehe handlePut) - 0 bedeutet "egal wie viele"
 * (siehe Aufgabenstellung), 8 ist die maximale Party-/Alliance-artige Gruppengröße, für die ein
 * Blue-Mage-Gruppenfinder überhaupt Sinn ergibt. */
const WANTED_PLAYER_COUNT_MIN = 0;
const WANTED_PLAYER_COUNT_MAX = 8;

/** Größe von StoredGroupProfile.members (siehe handleGroupPut) - mindestens 1 (eine "Gruppe" aus
 * 0 Mitgliedern wäre sinnlos), höchstens 8 (dieselbe maximale Party-/Alliance-artige Gruppengröße
 * wie WANTED_PLAYER_COUNT_MAX oben). */
const GROUP_MEMBER_COUNT_MIN = 1;
const GROUP_MEMBER_COUNT_MAX = 8;

/** Basis-URL der Web-Companion-Seite (docs/index.html, siehe README.md "browser companion" -
 * https://letsi-ma.github.io/BLUnion/) - für den Link-Button im Discord-Browse-Embed (siehe
 * buildGroupsBrowseEmbed), damit Discord-Nutzer ohne installiertes Plugin trotzdem eine Gruppe
 * ansehen können. Bewusst als eigene Konstante HIER (nicht im Env-Binding wie DISCORD_PUBLIC_KEY):
 * es ist eine öffentliche, feste GitHub-Pages-URL, kein Geheimnis, und identisch zu der bereits
 * bestehenden WORKER_BASE_URL-Konstante in docs/index.html - nur eben die URL der Website selbst,
 * nicht die des Workers/der API. */
const COMPANION_WEBSITE_URL = "https://letsi-ma.github.io/BLUnion/";

/** Von Discord vorgegebene Interaction-/Response-Typen (siehe
 * https://discord.com/developers/docs/interactions/receiving-and-responding) - hier NUR die für
 * Phase 1 tatsächlich gebrauchten Werte als benannte Konstanten hinterlegt, statt ein komplettes
 * SDK/Typpaket einzubinden (siehe Aufgabenstellung "keine unnötigen Abhängigkeiten"). */
const DISCORD_INTERACTION_TYPE_PING = 1;
const DISCORD_INTERACTION_TYPE_APPLICATION_COMMAND = 2;
const DISCORD_OPTION_TYPE_SUB_COMMAND = 1;
const DISCORD_RESPONSE_TYPE_PONG = 1;
const DISCORD_RESPONSE_TYPE_CHANNEL_MESSAGE_WITH_SOURCE = 4;
/** Aktuell ungenutzt (Phase 1 antwortet ausschließlich direkt, siehe handleDiscordBrowse) - schon
 * hier benannt hinterlegt, weil eine spätere Phase (z.B. schreibende Commands mit externem
 * API-Aufruf) auf denselben Konstantennamen zurückgreifen soll, statt den magischen Wert 5 dann
 * erneut irgendwo einzuführen. */
const DISCORD_RESPONSE_TYPE_DEFERRED_CHANNEL_MESSAGE_WITH_SOURCE = 5;

/** Obergrenze für StoredGroupProfile.targetSpellIds (siehe handleGroupPut/isValidTargetSpellIds) -
 * bewusst deutlich unter der Gesamtzahl bekannter Spells (siehe KNOWN_SPELL_IDS, aktuell 124):
 * eine "Ziel-Spell-Liste" für eine gemeinsame Farm-Session soll ein konkretes, erreichbares
 * Session-Ziel bleiben, kein Abbild der kompletten Spellliste. 0 (leeres Array) ist gültig - eine
 * Gruppe muss keine Ziel-Spells angeben. */
const GROUP_TARGET_SPELL_COUNT_MAX = 30;

/** FFXIV-Region ("physische" Server-Region, NICHT das einzelne Data Center) - siehe
 * DATA_CENTER_TO_REGION-Doc direkt unten für die eigentliche Begründung, warum die Discord-Karten
 * (Phase 1.5) pro REGION statt pro Data Center gebündelt werden. */
type Region = "NA" | "EU" | "JP" | "OC";

/** Alle vier Regionen als Liste (statt die vier Region-Literale an mehreren Stellen erneut
 * aufzuzählen, z.B. im Cron-Cleanup unten) - Reihenfolge ist beliebig, wird nirgends als Bedeutung
 * interpretiert. */
const ALL_REGIONS: readonly Region[] = ["NA", "EU", "JP", "OC"];

/** Data-Center -> Region-Zuordnung für die persistenten Discord-Gruppen-Karten (Phase 1.5, siehe
 * DISCORD_INTEGRATION.md/syncGroupDiscordCard unten) - EINZIGE, zentrale Stelle für diese Tabelle
 * (siehe Aufgabenstellung "nicht mehrfach im Code duplizieren").
 *
 * WARUM REGION statt Data Center die Gruppierungseinheit für die Discord-Kanäle ist: innerhalb
 * einer Region kann ein Spieler jederzeit per "World Visit" zwischen den Data Centern wechseln
 * (z.B. Aether <-> Crystal), zwischen Regionen dagegen nicht. Eine auf Aether veröffentlichte
 * Blue-Mage-Gruppe ist für einen Crystal-Spieler also potenziell genauso gut erreichbar wie eine
 * auf Crystal selbst - ein separater Kanal pro Data Center würde Gruppen künstlich über mehrere
 * Kanäle verstreuen, obwohl sie faktisch für dieselbe Spielerschaft sichtbar/erreichbar sein
 * sollten. Jede einzelne Karte zeigt trotzdem weiterhin das KONKRETE Data Center an (siehe
 * buildGroupCardEmbed) - nur die Kanal-/Webhook-Wahl selbst gruppiert nach Region.
 *
 * Bewusst OHNE "Shadow" (EU): das war ein 2024 eingeführtes, noch im selben Jahr wieder
 * geschlossenes TEMPORÄRES Data Center (Überlauf-Kapazität) - lookupDataCenter()/WORLD_DATA_CENTERS
 * (siehe worlds.ts) kannten es nie und werden es nie zurückliefern, ein gespeichertes
 * stored.dataCenter === "Shadow" kann also gar nicht vorkommen. Die 11 hier hinterlegten Data
 * Center sind damit vollständig - kein Platzhalter für ein zwölftes. */
const DATA_CENTER_TO_REGION: Readonly<Record<string, Region>> = {
  // North America
  Aether: "NA",
  Crystal: "NA",
  Dynamis: "NA",
  Primal: "NA",

  // Europe
  Chaos: "EU",
  Light: "EU",

  // Japan
  Elemental: "JP",
  Gaia: "JP",
  Mana: "JP",
  Meteor: "JP",

  // Oceania
  Materia: "OC",
};

/** Case-insensitiver Lookup analog zu lookupDataCenter() in worlds.ts (aus demselben Grund: die
 * tatsächliche Groß-/Kleinschreibung des gespeicherten dataCenter-Werts soll hier keine Rolle
 * spielen). null bei einem (noch) nicht in DATA_CENTER_TO_REGION hinterlegten Data Center - siehe
 * syncGroupDiscordCard, das diesen Fall wie "keine Region/kein Webhook konfiguriert" behandelt,
 * statt zu werfen. */
export function regionForDataCenter(dataCenter: string): Region | null {
  const normalized = dataCenter.toLowerCase();
  for (const [dc, region] of Object.entries(DATA_CENTER_TO_REGION)) {
    if (dc.toLowerCase() === normalized)
      return region;
  }
  return null;
}

/** Liefert die für eine Region konfigurierte Webhook-URL (siehe Env.DISCORD_WEBHOOK_NA-Doc oben) -
 * undefined, wenn diese Region (noch) nicht konfiguriert ist; der Aufrufer (syncGroupDiscordCard)
 * behandelt das als "Kartenfunktion für diese Region aktuell nicht verfügbar", nicht als Fehler. */
function getRegionWebhookUrl(env: Env, region: Region): string | undefined {
  switch (region) {
    case "NA": return env.DISCORD_WEBHOOK_NA;
    case "EU": return env.DISCORD_WEBHOOK_EU;
    case "JP": return env.DISCORD_WEBHOOK_JP;
    case "OC": return env.DISCORD_WEBHOOK_OC;
  }
}

/** Das in KV gespeicherte JSON (siehe Datenmodell in README.md). editTokenHash verlässt diese
 * Datei NIE in Richtung Client (siehe stripForResponse/stripForBrowseResponse).
 *
 * availabilityTags/note/wantedPlayerCount sind Phase-2-Felder (Gruppenfinder) - bei Profilen,
 * die noch von Phase 1 stammen (vor diesem Update angelegt/zuletzt aktualisiert), fehlen sie im
 * gespeicherten JSON schlicht (KV kennt kein Schema/keine Migration). Jede Stelle, die ein
 * StoredProfile liest, behandelt sie deshalb als optional (siehe "?" hier UND die "??"-Fallbacks
 * in handlePut/stripForResponse/stripForBrowseResponse) statt sich auf ihre Anwesenheit zu
 * verlassen.
 */
interface StoredProfile {
  characterName: string;
  world: string;
  dataCenter: string;
  spellBitmaskBase64: string;
  editTokenHash: string;
  visibility: "listed" | "unlisted";
  availabilityTags?: string[];
  note?: string;
  wantedPlayerCount?: number;
  createdAt: string;
  updatedAt: string;
}

interface PutRequestBody {
  spellBitmaskBase64?: unknown;
  editToken?: unknown;
  visibility?: unknown;
  availabilityTags?: unknown;
  note?: unknown;
  wantedPlayerCount?: unknown;
  ttlHours?: unknown;
}

/** Referenz auf ein Mitglied EINER Gruppen-Listung (siehe StoredGroupProfile) - identifiziert
 * ausschließlich per world+characterName, denselben beiden Feldern, die auch den KV-Key eines
 * "profile:"-Eintrags bilden (siehe kvKey()). Enthält bewusst NICHTS aus dem Einzelprofil selbst
 * (keine Bitmaske, kein dataCenter) - das wird erst bei GET /groups/browse live nachgeladen
 * (siehe handleGroupsBrowse). world ist hier bereits die KANONISCHE Schreibweise (siehe
 * handleGroupPut/lookupDataCenter), nicht zwingend die vom Client übergebene. */
interface GroupMember {
  world: string;
  characterName: string;
}

/** Das in KV unter "group:<groupId>" gespeicherte JSON (siehe Klassendoc oben zum
 * Referenz-statt-Kopie-Ansatz und README.md für das vollständige Datenmodell). editTokenHash
 * verlässt diese Datei NIE in Richtung Client (siehe stripForGroupResponse), genau wie bei
 * StoredProfile.
 *
 * WICHTIG: editTokenHash identifiziert NUR den Ersteller/Veröffentlicher DIESER Gruppen-Listung -
 * NICHT die referenzierten members[]. Wer die Gruppe per PUT anlegt, ist der einzige, der sie
 * später per PUT/DELETE ändern bzw. löschen kann; die Mitglieder selbst haben darüber KEINEN
 * eigenen Zugriff (siehe Doc an handleGroupPut/handleGroupDelete). Das ist eine bewusste
 * Design-Entscheidung, kein Bug - falls das künftig geändert werden soll (z.B. soll jedes
 * Mitglied die Gruppen-Listung löschen dürfen), müsste der Token stattdessen an alle Mitglieder
 * verteilt oder durch ein anderes Berechtigungsmodell ersetzt werden.
 *
 * dataCenter wird - wie bei StoredProfile - server-seitig hergeleitet (hier vom world des ERSTEN
 * Mitglieds, siehe handleGroupPut), nicht vom Client übergeben. availabilityTags/note/
 * wantedPlayerCount sind exakt dieselben Phase-2-Felder wie bei StoredProfile, inklusive
 * derselben ALLOWED_AVAILABILITY_TAGS/NOTE_MAX_LENGTH/WANTED_PLAYER_COUNT_MIN/MAX-Regeln - daher
 * hier ebenfalls optional (siehe StoredProfile-Doc zur Rückwärtskompatibilität).
 *
 * targetSpellIds: die Spells, die die Gruppe gemeinsam farmen möchte (siehe UI/MainWindow.
 * GroupFinder.cs DrawGroupPublishSection) - anders als members[] KEINE Referenz auf andere
 * KV-Einträge, sondern reine IDs aus dem festen KNOWN_SPELL_IDS-Set (siehe spellIds.ts), gegen das
 * handleGroupPut validiert. Optional aus demselben Rückwärtskompatibilitätsgrund wie die übrigen
 * Phase-2-Felder - Gruppen-Listungen von vor diesem Feature haben es im gespeicherten JSON schlicht
 * nicht.
 *
 * discordCard (Phase 1.5, siehe DISCORD_INTEGRATION.md/syncGroupDiscordCard weiter unten): rein
 * additives, optionales Feld - hält fest, ÜBER WELCHE Discord-Nachricht (welcher Webhook/welche
 * Region, welche Message-ID) diese Gruppe aktuell eine persistente Kanal-Karte hat, damit ein
 * späteres Update dieselbe Nachricht per editWebhookMessage aktualisieren statt eine zweite,
 * doppelte anzulegen. Fehlt bei alten Gruppen-Listungen von vor diesem Feature UND bei Gruppen, für
 * deren Region (noch) kein Webhook konfiguriert ist - beides schlicht "hat aktuell keine Karte",
 * siehe syncGroupDiscordCard, KEIN Fehlerzustand. */
interface StoredGroupProfile {
  groupId: string;
  members: GroupMember[];
  editTokenHash: string;
  visibility: "listed" | "unlisted";
  availabilityTags?: string[];
  note?: string;
  wantedPlayerCount?: number;
  targetSpellIds?: number[];
  dataCenter: string;
  discordCard?: DiscordCard;
  createdAt: string;
  updatedAt: string;
}

/** Siehe StoredGroupProfile.discordCard-Doc oben. channelWebhookId identifiziert NUR, ÜBER WELCHEN
 * Webhook zuletzt gepostet wurde (siehe extractWebhookId in discordWebhook.ts) - NIE der Token
 * selbst, der bleibt ausschließlich im jeweiligen Env.DISCORD_WEBHOOK_*-Secret (siehe Env-Doc oben)
 * und landet nie in KV. */
interface DiscordCard {
  region: Region;
  channelWebhookId: string;
  messageId: string;
}

interface PutGroupRequestBody {
  members?: unknown;
  editToken?: unknown;
  visibility?: unknown;
  availabilityTags?: unknown;
  note?: unknown;
  wantedPlayerCount?: unknown;
  targetSpellIds?: unknown;
  ttlHours?: unknown;
}

/** Minimale, nur für Phase 1 gebrauchte Typisierung der eingehenden Discord-Interaction (siehe
 * handleDiscordInteractions unten) - absichtlich kein vollständiges Interaction-Schema (Discord
 * schickt deutlich mehr Felder, u.a. guild_id/member/channel_id), da diese hier nicht ausgewertet
 * werden. Kein separates SDK/Typpaket (siehe Aufgabenstellung "keine unnötigen Abhängigkeiten"). */
interface DiscordInteraction {
  type: number;
  data?: DiscordInteractionCommandData;
}

/** "options" ist bei einem Sub-Command-Aufruf ("/blunion browse ...") verschachtelt: das äußere
 * data.options[] enthält EIN Element vom Typ SUB_COMMAND ("browse"), dessen EIGENE options[]
 * wiederum die tatsächlichen Parameter des Sub-Commands (hier "datacenter") trägt - siehe
 * findDiscordSubcommand/findDiscordStringOption. */
interface DiscordInteractionCommandData {
  name: string;
  options?: DiscordInteractionOption[];
}

interface DiscordInteractionOption {
  name: string;
  type: number;
  value?: string | number | boolean;
  options?: DiscordInteractionOption[];
}

/** Response-Shape von handleGroupsBrowse/computeGroupsBrowse (siehe dort) - hier separat
 * typisiert, weil handleDiscordBrowse das per JSON.parse zurückerhaltene Ergebnis wieder in ein
 * Discord-Embed umbaut (siehe buildGroupsBrowseEmbed) und dafür Feldnamen/-typen braucht. */
interface DiscordBrowseGroupMember {
  world: string;
  characterName: string;
}

interface DiscordBrowseGroup {
  groupId: string;
  members: DiscordBrowseGroupMember[];
  availabilityTags: string[];
  note: string;
  wantedPlayerCount: number;
}

/** Liefert die tatsächlich zu setzende expirationTtl (Sekunden) aus dem optionalen
 * ttlHours-Body-Feld (siehe TTL_HOURS_MIN/MAX-Doc) - fehlt es oder ist es kein gültiger
 * endlicher Zahlenwert, greift unverändert PROFILE_TTL_SECONDS (bisheriges Verhalten,
 * insbesondere für das Plugin, das dieses Feld nie mitschickt). Math.min/Math.max statt eines
 * if/else-Kaskaden-Clamps - bei nur zwei Grenzen knapper und genauso klar. */
function resolveTtlSeconds(ttlHours: unknown): number {
  if (typeof ttlHours !== "number" || !Number.isFinite(ttlHours))
    return PROFILE_TTL_SECONDS;

  const clampedHours = Math.min(Math.max(ttlHours, TTL_HOURS_MIN), TTL_HOURS_MAX);
  return clampedHours * 3600;
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8", ...CORS_HEADERS },
  });
}

function errorResponse(status: number, message: string): Response {
  return jsonResponse({ error: message }, status);
}

/** Serverseitiges Kurzzeit-Caching für GET-Endpunkte über die Standard-Workers-Cache-API
 * (caches.default) - bewusst KEINE neue Abhängigkeit, keine KV-basierte Eigenimplementierung.
 * NUR für die beiden Browse-Endpunkte genutzt (siehe handleBrowse/handleGroupsBrowse), NIEMALS
 * für PUT/DELETE - Schreiboperationen dürfen nie aus dem Cache beantwortet werden.
 *
 * WICHTIG: Cloudflares Cache-API ist PER EDGE-STANDORT, kein global geteilter Cache - ein
 * Request an einen anderen PoP trifft also ggf. noch nicht denselben Cache-Eintrag. Das
 * reduziert die Last trotzdem spürbar (die meisten Requests eines Nutzers/einer Region landen
 * wiederholt am selben PoP), auch ohne perfekte globale Deduplizierung.
 *
 * response.clone() vor cache.put() ist zwingend: der Response-Body ist ein einmal lesbarer
 * Stream, cache.put() UND der an den Aufrufer zurückgegebene Response würden sich sonst denselben
 * (bereits konsumierten) Stream teilen. ctx.waitUntil() sorgt dafür, dass der Worker nicht auf das
 * Schreiben in den Cache wartet, bevor die Response an den Client geht, der Cache-Put aber trotzdem
 * zuverlässig abgeschlossen wird, bevor die Instanz ggf. beendet wird. */
async function withCache(
  request: Request,
  ctx: ExecutionContext,
  compute: () => Promise<Response>,
): Promise<Response> {
  const cache = caches.default;
  const cached = await cache.match(request);
  if (cached)
    return cached;

  const response = await compute();
  if (response.ok) {
    const cacheableResponse = new Response(response.body, response);
    cacheableResponse.headers.set("Cache-Control", `public, max-age=${BROWSE_CACHE_TTL_SECONDS}`);
    ctx.waitUntil(cache.put(request, cacheableResponse.clone()));
    return cacheableResponse;
  }

  return response;
}

/** IP-basiertes Basis-Rate-Limiting für die vier schreibenden Endpunkte (PUT/DELETE auf /profile
 * und /group, siehe deren Aufrufstellen unten) über das native Cloudflare-Workers-Rate-Limiting-
 * Binding WRITE_RATE_LIMITER (siehe Env/wrangler.toml) - KEINE KV-Eigenimplementierung nötig
 * (siehe Aufgabenstellung "einfaches Rate-Limiting, keine unnötigen Abhängigkeiten"), das Binding
 * leistet exakt das ohne zusätzliche KV-Reads/Writes pro Request.
 *
 * Bewusst NICHT die Dashboard-"Rate Limiting Rules": die sind ein Zonen-/WAF-Feature und setzen
 * eine bei Cloudflare verwaltete, eigene Zone (Custom Domain) voraus - dieser Worker läuft aber
 * unter *.workers.dev (siehe wrangler.toml), ohne eigene Zone. Das Rate-Limiting-BINDING hängt
 * dagegen direkt am Worker selbst und funktioniert unabhängig von Zone/Custom-Domain.
 *
 * LIMIT-WAHL (siehe wrangler.toml [ratelimits.simple]): 20 Schreibzugriffe/IP alle 60 Sekunden.
 * Großzügig genug für normale Nutzung - ein Spieler pusht bei jedem gelernten Spell erneut (siehe
 * PROFILE_TTL_SECONDS-Doc oben), dazu kommen gelegentliche Gruppenfinder-Aktualisierungen/
 * -Löschungen - aber eng genug, um massenhaftes Anlegen/Löschen vieler Profile von einer
 * einzelnen IP (das eigentliche Missbrauchsszenario bei einem öffentlichen, tokenlosen
 * PUT-Endpoint) abzuwürgen. 60 statt der ebenfalls erlaubten 10 Sekunden gewählt (siehe
 * RateLimitOptions-Doc), weil ein Minutenfenster die aussagekräftigere Grenze zwischen normaler
 * Nutzung und Abuse zieht - ein Spieler, der z.B. innerhalb von 2 Sekunden nacheinander mehrere
 * Spells lernt und dabei mehrfach pusht, würde bei einem 10-Sekunden-Fenster eher versehentlich
 * anschlagen.
 *
 * KEY = CF-Connecting-IP (von Cloudflare selbst am Edge gesetzt, vom Client NICHT fälschbar)
 * statt z.B. world+characterName - begrenzt damit nicht nur Spam GEGEN ein einzelnes Profil,
 * sondern das massenhafte ANLEGEN vieler verschiedener Profile von derselben Quelle. Fehlt der
 * Header (z.B. lokal in `wrangler dev` ohne echten Cloudflare-Edge-Request davor), wird NICHT
 * limitiert statt mit 500 zu antworten - lokale Entwicklung soll dadurch nicht blockiert werden,
 * hinter dem echten Edge in Produktion ist der Header immer gesetzt.
 *
 * Laut Cloudflare-Doku ist das Binding "permissive, eventually consistent" (pro Edge-Standort
 * gezählt, kein exakter globaler Zähler) - für den hier verlangten Basisschutz gegen
 * Missbrauch/Spam völlig ausreichend, NICHT gedacht/geeignet als exaktes Abrechnungssystem.
 *
 * Gibt bei Überschreitung direkt eine fertige 429-Response zurück (Aufrufer muss dann NUR noch
 * `if (rateLimited) return rateLimited;` prüfen), bei null darf normal fortgefahren werden -
 * gleiches Frühzeitig-Rückgabe-Muster wie die bestehende Validierung in handlePut/handleGroupPut. */
async function enforceWriteRateLimit(env: Env, request: Request): Promise<Response | null> {
  const ip = request.headers.get("CF-Connecting-IP");
  if (!ip)
    return null;

  const { success } = await env.WRITE_RATE_LIMITER.limit({ key: ip });
  if (!success)
    return errorResponse(429, "Zu viele Anfragen von dieser IP - bitte kurz warten und erneut versuchen.");

  return null;
}

function kvKey(world: string, characterName: string): string {
  return `profile:${world.toLowerCase()}:${characterName.toLowerCase()}`;
}

/** KV-Key für eine Gruppen-Listung - anders als kvKey() bewusst NICHT lowercased: groupId ist
 * ein vom Client generierter, zufälliger String (siehe StoredGroupProfile-Doc) ohne natürlichen
 * Schlüssel, den man auf diese Weise deduplizieren müsste. */
function groupKvKey(groupId: string): string {
  return `group:${groupId}`;
}

/** KV-Key für den "welche Gruppen haben aktuell eine offene Discord-Karte in dieser Region"-Index
 * (Phase 1.5, siehe DiscordCardIndexEntry-Doc/cleanupOrphanedDiscordCards weiter unten für die
 * ausführliche Begründung, WARUM dieser zusätzliche Index überhaupt nötig ist). */
function discordCardsIndexKey(region: Region): string {
  return `discordcards:${region}`;
}

/** EIN Eintrag im "discordcards:<region>"-Index (siehe discordCardsIndexKey) - absichtlich NUR
 * groupId+messageId, keine weiteren Gruppendaten (die stehen, solange die Gruppe existiert, ohnehin
 * schon im zugehörigen "group:<groupId>"-Eintrag; siehe cleanupOrphanedDiscordCards, das genau
 * diesen Zusammenhang nutzt).
 *
 * WARUM DIESER INDEX ÜBERHAUPT NÖTIG IST (siehe Aufgabenstellung "bitte VOR der Implementierung
 * kurz dokumentieren"): Gruppen laufen nicht per explizitem Löschen ab, sondern über KVs
 * expirationTtl (siehe PROFILE_TTL_SECONDS/resolveTtlSeconds oben) - KV löst dabei KEINEN Code aus,
 * der Key verschwindet still, ohne jede Benachrichtigung (siehe Klassendoc am Dateianfang). Ein
 * KV.list({ prefix: "group:" })-Durchlauf (wie ihn z.B. computeGroupsBrowse nutzt) zeigt deshalb
 * IMMER nur noch existierende Gruppen - genau die bereits abgelaufenen Einträge, um die es beim
 * Karten-Aufräumen geht, sind darin per Definition NICHT mehr enthalten. Ohne einen SEPARATEN,
 * eigenständig gepflegten Index gäbe es also keine Möglichkeit, im Nachhinein zu erkennen "Gruppe X
 * hatte mal eine Karte, X existiert aber nicht mehr, also muss die Karte weg" - die Information
 * "X hatte mal eine Karte" wäre mit dem group:X-Eintrag selbst mitverschwunden.
 *
 * "discordcards:<region>" wird deshalb bei JEDEM Anlegen/Entfernen einer Karte separat mitgepflegt
 * (siehe addDiscordCardIndexEntry/removeDiscordCardIndexEntry) und hat bewusst KEIN expirationTtl -
 * sonst könnte der Index-Eintrag selbst VOR dem zugehörigen group:-Eintrag verschwinden, und der
 * Cron-Vergleich (siehe cleanupOrphanedDiscordCards) würde genau die Fälle verpassen, die er lösen
 * soll. Der Index bleibt dadurch zwangsläufig "eventually consistent" mit den group:-Einträgen
 * (siehe auch der bestehende Kommentar zum nativen Rate-Limiting-Binding zum selben Thema) - für
 * einen täglichen Aufräum-Cron (siehe wrangler.toml [triggers]) völlig ausreichend. */
interface DiscordCardIndexEntry {
  groupId: string;
  messageId: string;
}

/** Trägt einen {groupId, messageId}-Eintrag in den Index der gegebenen Region ein (siehe
 * DiscordCardIndexEntry-Doc) - entfernt zuerst einen eventuell vorhandenen ALTEN Eintrag für
 * dieselbe groupId (z.B. bei einem erneuten Erstellen nach einem Regionswechsel, siehe
 * syncGroupDiscordCard), damit pro Gruppe/Region nie mehr als ein Eintrag existiert. */
async function addDiscordCardIndexEntry(env: Env, region: Region, groupId: string, messageId: string): Promise<void> {
  const key = discordCardsIndexKey(region);
  const existing = (await env.BLUNION_PROFILES.get<DiscordCardIndexEntry[]>(key, "json")) ?? [];
  const withoutGroup = existing.filter((entry) => entry.groupId !== groupId);
  withoutGroup.push({ groupId, messageId });
  await env.BLUNION_PROFILES.put(key, JSON.stringify(withoutGroup));
}

/** Entfernt den Eintrag für groupId aus dem Index der gegebenen Region, falls vorhanden - tut
 * bewusst NICHTS (kein KV-Put), wenn kein passender Eintrag existiert, um keinen unnötigen
 * Schreibzugriff auszulösen. */
async function removeDiscordCardIndexEntry(env: Env, region: Region, groupId: string): Promise<void> {
  const key = discordCardsIndexKey(region);
  const existing = await env.BLUNION_PROFILES.get<DiscordCardIndexEntry[]>(key, "json");
  if (!existing)
    return;

  const filtered = existing.filter((entry) => entry.groupId !== groupId);
  if (filtered.length === existing.length)
    return;

  await env.BLUNION_PROFILES.put(key, JSON.stringify(filtered));
}

/** Bewusst als eigene, kleine Funktion statt der Versuchung nachzugeben, einfach das gespeicherte
 * Objekt "minus editTokenHash" per Destrukturierung durchzureichen - so ist beim Hinzufügen eines
 * künftigen internen Felds nicht automatisch die Gefahr da, es versehentlich mit rauszugeben,
 * weil hier explizit aufgelistet wird, was rausgeht (Allowlist statt Blocklist). Für GET/PUT auf
 * das EIGENE Profil (per world+characterName) - für den Gruppenfinder-Listing-Endpoint siehe
 * stripForBrowseResponse, die bewusst NICHT dataCenter/visibility mit ausliefert (siehe dort). */
function stripForResponse(stored: StoredProfile) {
  return {
    characterName: stored.characterName,
    world: stored.world,
    dataCenter: stored.dataCenter,
    spellBitmaskBase64: stored.spellBitmaskBase64,
    visibility: stored.visibility,
    availabilityTags: stored.availabilityTags ?? [],
    note: stored.note ?? "",
    wantedPlayerCount: stored.wantedPlayerCount ?? 0,
    updatedAt: stored.updatedAt,
  };
}

/** Analog zu stripForResponse, aber für GET /profiles/browse (siehe handleBrowse): dataCenter
 * ist dort redundant (der Aufrufer hat es selbst als Query-Parameter übergeben, alle Ergebnisse
 * teilen es sich) und visibility ebenso (immer "listed", sonst wäre der Eintrag gar nicht erst
 * im Ergebnis) - beide bewusst weggelassen statt sie redundant mitzuschicken (siehe
 * Aufgabenstellung: exaktes Response-Shape für den Browse-Endpoint). editTokenHash verlässt auch
 * hier NIE die Datei, siehe Allowlist-Prinzip oben. */
function stripForBrowseResponse(stored: StoredProfile) {
  return {
    characterName: stored.characterName,
    world: stored.world,
    spellBitmaskBase64: stored.spellBitmaskBase64,
    availabilityTags: stored.availabilityTags ?? [],
    note: stored.note ?? "",
    wantedPlayerCount: stored.wantedPlayerCount ?? 0,
    updatedAt: stored.updatedAt,
  };
}

/** Analog zu stripForResponse, aber für PUT/DELETE-Antworten auf /group/:groupId (siehe
 * handleGroupPut) - Allowlist-Prinzip wie oben, editTokenHash verlässt auch hier NIE die Datei.
 * Für den GET /groups/browse-Response-Shape siehe stattdessen handleGroupsBrowse direkt: der
 * baut die members[] dort individuell zusammen (inkl. nachgeladener spellBitmaskBase64 je
 * Mitglied), stripForGroupResponse liefert das rohe StoredGroupProfile.members (nur world+
 * characterName, ohne Bitmaske) - das ist für die eigene PUT-Bestätigung ausreichend. */
function stripForGroupResponse(stored: StoredGroupProfile) {
  return {
    groupId: stored.groupId,
    members: stored.members,
    dataCenter: stored.dataCenter,
    visibility: stored.visibility,
    availabilityTags: stored.availabilityTags ?? [],
    note: stored.note ?? "",
    wantedPlayerCount: stored.wantedPlayerCount ?? 0,
    targetSpellIds: stored.targetSpellIds ?? [],
    updatedAt: stored.updatedAt,
  };
}

function isValidBitmaskBase64(value: unknown): value is string {
  if (typeof value !== "string" || value.length === 0)
    return false;

  try {
    return base64UrlDecode(value).length === BITMASK_BYTES;
  } catch {
    return false;
  }
}

function isValidVisibility(value: unknown): value is "listed" | "unlisted" {
  return value === "listed" || value === "unlisted";
}

/** Einfaches Array.includes() statt einer Validierungsbibliothek (siehe Aufgabenstellung "keine
 * neuen Abhängigkeiten") - bei nur 5 erlaubten Werten völlig ausreichend. Leeres Array ist
 * gültig (Spieler hat noch keine Verfügbarkeit ausgewählt). */
function isValidAvailabilityTags(value: unknown): value is string[] {
  return Array.isArray(value) && value.every(
    (tag) => typeof tag === "string" && (ALLOWED_AVAILABILITY_TAGS as readonly string[]).includes(tag));
}

function isValidWantedPlayerCount(value: unknown): value is number {
  return typeof value === "number"
    && Number.isInteger(value)
    && value >= WANTED_PLAYER_COUNT_MIN
    && value <= WANTED_PLAYER_COUNT_MAX;
}

/** Analog zu isValidAvailabilityTags, aber gegen KNOWN_SPELL_IDS statt ALLOWED_AVAILABILITY_TAGS
 * geprüft (siehe handleGroupPut) - jede ID muss eine tatsächlich existierende Spell-ID sein, dazu
 * die Obergrenze GROUP_TARGET_SPELL_COUNT_MAX. Leeres Array ist gültig (siehe dortige Doc). */
function isValidTargetSpellIds(value: unknown): value is number[] {
  return Array.isArray(value)
    && value.length <= GROUP_TARGET_SPELL_COUNT_MAX
    && value.every((id) => typeof id === "number" && Number.isInteger(id) && KNOWN_SPELL_IDS.has(id));
}

/** Reine Form-/Typ-Prüfung für EIN members[]-Element aus dem /group/:groupId-PUT-Body (siehe
 * handleGroupPut) - prüft nur, dass world/characterName als nicht-leere Strings vorhanden sind.
 * Ob world tatsächlich über lookupDataCenter auflösbar ist, prüft bewusst NICHT diese Funktion,
 * sondern der Aufrufer direkt - der braucht dafür eine spezifischere Fehlermeldung (welche World
 * genau unbekannt ist), die ein reiner boolean-Type-Guard hier nicht liefern könnte. */
function isValidRawGroupMember(value: unknown): value is { world: string; characterName: string } {
  if (typeof value !== "object" || value === null)
    return false;

  const candidate = value as Record<string, unknown>;
  return typeof candidate.world === "string" && candidate.world.length > 0
    && typeof candidate.characterName === "string" && candidate.characterName.length > 0;
}

async function handleGet(env: Env, world: string, characterName: string): Promise<Response> {
  const stored = await env.BLUNION_PROFILES.get<StoredProfile>(kvKey(world, characterName), "json");

  // Erwarteter Fall für Spieler ohne Live-Sync (siehe Aufgabenstellung) - bewusst kein Log.
  if (!stored)
    return errorResponse(404, "Kein Profil für diese World/diesen Charakternamen gefunden.");

  return jsonResponse(stripForResponse(stored));
}

async function handlePut(env: Env, request: Request, world: string, characterName: string): Promise<Response> {
  const rateLimited = await enforceWriteRateLimit(env, request);
  if (rateLimited)
    return rateLimited;

  let body: PutRequestBody;
  try {
    body = await request.json();
  } catch {
    return errorResponse(400, "Ungültiger oder fehlender JSON-Body.");
  }

  if (!isValidBitmaskBase64(body.spellBitmaskBase64)) {
    return errorResponse(
      400,
      `spellBitmaskBase64 fehlt oder hat nicht die erwartete Länge (${BITMASK_BYTES} Bytes, ` +
        "Base64 URL-safe ohne Padding).",
    );
  }

  const dcLookup = lookupDataCenter(world);
  if (!dcLookup)
    return errorResponse(400, `Unbekannte World "${world}".`);

  const key = kvKey(world, characterName);
  const existing = await env.BLUNION_PROFILES.get<StoredProfile>(key, "json");
  const now = new Date().toISOString();

  // Alle vier Gruppenfinder-Felder sind im Body OPTIONAL (siehe Aufgabenstellung): fehlt ein
  // Feld, bleibt der bisherige Wert unverändert (bei einem neuen Profil greift stattdessen der
  // jeweilige Default) - so leert ein reiner Spell-Status-Push aus Phase 1 (der diese Felder gar
  // nicht kennt) die Gruppenfinder-Angaben NICHT versehentlich. Nur ein tatsächlich im Body
  // vorhandener, aber ungültiger Wert führt zu 400 - ein fehlendes Feld nie.
  let visibility: "listed" | "unlisted";
  if (body.visibility === undefined) {
    visibility = existing?.visibility ?? "unlisted";
  } else if (isValidVisibility(body.visibility)) {
    visibility = body.visibility;
  } else {
    return errorResponse(400, 'visibility muss "listed" oder "unlisted" sein.');
  }

  let availabilityTags: string[];
  if (body.availabilityTags === undefined) {
    availabilityTags = existing?.availabilityTags ?? [];
  } else if (isValidAvailabilityTags(body.availabilityTags)) {
    availabilityTags = body.availabilityTags;
  } else {
    return errorResponse(
      400, `availabilityTags enthält ungültige Werte (erlaubt: ${ALLOWED_AVAILABILITY_TAGS.join(", ")}).`);
  }

  let note: string;
  if (body.note === undefined) {
    note = existing?.note ?? "";
  } else if (typeof body.note === "string") {
    // Gekappt statt abgelehnt (siehe Konstantendoc NOTE_MAX_LENGTH) - das Plugin begrenzt die
    // Eingabe zwar schon selbst, ein direkter API-Aufruf könnte das aber umgehen.
    note = body.note.slice(0, NOTE_MAX_LENGTH);
  } else {
    return errorResponse(400, "note muss ein String sein.");
  }

  let wantedPlayerCount: number;
  if (body.wantedPlayerCount === undefined) {
    wantedPlayerCount = existing?.wantedPlayerCount ?? 0;
  } else if (isValidWantedPlayerCount(body.wantedPlayerCount)) {
    wantedPlayerCount = body.wantedPlayerCount;
  } else {
    return errorResponse(
      400,
      `wantedPlayerCount muss eine ganze Zahl zwischen ${WANTED_PLAYER_COUNT_MIN} und ${WANTED_PLAYER_COUNT_MAX} sein.`,
    );
  }

  let editTokenHash: string;
  let createdAt: string;
  // Nur bei einem NEU angelegten Profil gesetzt - der Klartext-Token wird genau einmal
  // zurückgegeben (siehe Aufgabenstellung), danach existiert er serverseitig nur noch als Hash.
  let plaintextEditTokenForResponse: string | undefined;

  if (existing) {
    const providedToken = typeof body.editToken === "string" ? body.editToken : null;
    if (!providedToken)
      return errorResponse(409, "Profil existiert bereits - editToken erforderlich, um es zu aktualisieren.");

    const providedHash = await sha256Hex(providedToken);
    if (providedHash !== existing.editTokenHash)
      return errorResponse(409, "editToken stimmt nicht mit dem gespeicherten Profil überein.");

    editTokenHash = existing.editTokenHash;
    createdAt = existing.createdAt;
  } else {
    const newToken = generateEditToken();
    editTokenHash = await sha256Hex(newToken);
    plaintextEditTokenForResponse = newToken;
    createdAt = now;
  }

  const record: StoredProfile = {
    characterName,
    world: dcLookup.canonicalWorld,
    dataCenter: dcLookup.dataCenter,
    spellBitmaskBase64: body.spellBitmaskBase64 as string,
    editTokenHash,
    visibility,
    availabilityTags,
    note,
    wantedPlayerCount,
    createdAt,
    updatedAt: now,
  };

  // expirationTtl bei JEDEM Put neu gesetzt (siehe Konstantendoc oben) - das ist Absicht, kein
  // Bug: aktive Profile (die bei jedem gelernten Spell erneut gepusht werden) sollen NIE
  // ablaufen, nur wirklich inaktive nach der jeweils gültigen TTL (siehe resolveTtlSeconds -
  // PROFILE_TTL_SECONDS ohne ttlHours im Body, sonst der geclampte Override).
  await env.BLUNION_PROFILES.put(key, JSON.stringify(record), { expirationTtl: resolveTtlSeconds(body.ttlHours) });

  const responseBody: Record<string, unknown> = stripForResponse(record);
  if (plaintextEditTokenForResponse)
    responseBody.editToken = plaintextEditTokenForResponse;

  return jsonResponse(responseBody, existing ? 200 : 201);
}

/** GET /profiles/browse?dataCenter=<DC> - öffentlicher Gruppenfinder (Phase 2). Liefert alle
 * Profile mit visibility === "listed" auf dem angegebenen Data Center.
 *
 * Bewusst OHNE Sekundär-Index (z.B. "dcindex:<DC>:<key>"): iteriert stattdessen über ALLE
 * "profile:"-Keys per list() und filtert danach in-memory auf dataCenter+visibility. Für die bei
 * Phase 2 zu erwartende Nutzerzahl unkritisch - ein Index wäre hier verfrühte Optimierung (siehe
 * Aufgabenstellung). Sollte die Zahl der Profile insgesamt über ca. 1000 wachsen, lohnt sich ein
 * echter DC-Index (z.B. ein zusätzlicher KV-Key pro Data Center mit einer Liste betroffener
 * Profil-Keys, bei jedem PUT/DELETE mitgepflegt) - dann müsste hier nicht mehr jedes einzelne
 * Profil unabhängig vom Data Center gelesen werden. */
async function handleBrowse(env: Env, request: Request, ctx: ExecutionContext): Promise<Response> {
  // NUR die eigentliche Berechnung läuft hinter withCache (siehe dortigen Kommentar) - der
  // "dataCenter fehlt"-400-Fehler unten wird über response.ok in withCache ohnehin nie gecached,
  // eine Sonderbehandlung dafür ist deshalb nicht nötig.
  return withCache(request, ctx, async () => {
    const url = new URL(request.url);
    const dataCenter = url.searchParams.get("dataCenter");
    if (!dataCenter)
      return errorResponse(400, 'Query-Parameter "dataCenter" fehlt.');

    const normalizedDataCenter = dataCenter.toLowerCase();
    const results: ReturnType<typeof stripForBrowseResponse>[] = [];

    // list() liefert maximal 1000 Keys pro Aufruf (Cloudflare-KV-Limit) - bei mehr Profilen als
    // das über "cursor" paginiert weiterlesen, bis list_complete true ist.
    let cursor: string | undefined;
    do {
      const listResult = await env.BLUNION_PROFILES.list({ prefix: "profile:", cursor });

      for (const listedKey of listResult.keys) {
        const stored = await env.BLUNION_PROFILES.get<StoredProfile>(listedKey.name, "json");
        if (!stored)
          continue; // Zwischen list() und get() gelöscht/abgelaufen - überspringen statt Fehler.

        if (stored.visibility === "listed" && stored.dataCenter.toLowerCase() === normalizedDataCenter)
          results.push(stripForBrowseResponse(stored));
      }

      cursor = listResult.list_complete ? undefined : listResult.cursor;
    } while (cursor);

    return jsonResponse(results);
  });
}

async function handleDelete(env: Env, request: Request, world: string, characterName: string): Promise<Response> {
  const rateLimited = await enforceWriteRateLimit(env, request);
  if (rateLimited)
    return rateLimited;

  const token = request.headers.get("X-Edit-Token");
  if (!token)
    return errorResponse(403, 'Header "X-Edit-Token" fehlt.');

  const key = kvKey(world, characterName);
  const existing = await env.BLUNION_PROFILES.get<StoredProfile>(key, "json");
  if (!existing)
    return errorResponse(404, "Kein Profil für diese World/diesen Charakternamen gefunden.");

  const providedHash = await sha256Hex(token);
  if (providedHash !== existing.editTokenHash)
    return errorResponse(403, "editToken stimmt nicht mit dem gespeicherten Profil überein.");

  await env.BLUNION_PROFILES.delete(key);
  return jsonResponse({ deleted: true });
}

/** PUT /group/:groupId - Gruppen-Listung anlegen/aktualisieren (Phase 2, siehe Klassendoc/
 * StoredGroupProfile). Ablauf 1:1 wie handlePut für Einzelprofile (Existenz-Check,
 * editToken-Erzeugung/-Vergleich über generateEditToken/sha256Hex, dieselbe 409-Antwort bei
 * fehlendem/falschem Token, dieselbe TTL-Logik über resolveTtlSeconds/PROFILE_TTL_SECONDS,
 * dieselbe visibility/availabilityTags/note/wantedPlayerCount-Validierung) - der einzige
 * inhaltliche Unterschied ist die members[]-Validierung/-Auflösung unten anstelle von
 * spellBitmaskBase64.
 *
 * WICHTIG (Edit-Token-Besitzmodell): der editToken identifiziert NUR den Ersteller/
 * Veröffentlicher DIESER Gruppen-Listung, NICHT die referenzierten Mitglieder - wer die Gruppe
 * per PUT anlegt, ist der einzige, der sie später ändern/löschen kann (kein geteilter Zugriff
 * für alle Mitglieder). Das ist eine bewusste Design-Entscheidung, kein Bug - falls das künftig
 * geändert werden soll (z.B. soll jedes Mitglied die Gruppen-Listung löschen dürfen), müsste der
 * Token stattdessen an alle Mitglieder verteilt oder durch ein anderes Berechtigungsmodell
 * (z.B. eigene Tokens pro Mitglied) ersetzt werden. */
async function handleGroupPut(env: Env, request: Request, groupId: string, ctx: ExecutionContext): Promise<Response> {
  const rateLimited = await enforceWriteRateLimit(env, request);
  if (rateLimited)
    return rateLimited;

  let body: PutGroupRequestBody;
  try {
    body = await request.json();
  } catch {
    return errorResponse(400, "Ungültiger oder fehlender JSON-Body.");
  }

  if (!Array.isArray(body.members) || body.members.length < GROUP_MEMBER_COUNT_MIN
      || body.members.length > GROUP_MEMBER_COUNT_MAX) {
    return errorResponse(
      400, `members muss ein Array mit ${GROUP_MEMBER_COUNT_MIN}-${GROUP_MEMBER_COUNT_MAX} Einträgen sein.`);
  }

  // dataCenter wird - wie bei handlePut - server-seitig hergeleitet, hier vom WORLD DES ERSTEN
  // Mitglieds (siehe Aufgabenstellung), nicht vom Client übergeben. Jedes einzelne members[].world
  // muss unabhängig davon über lookupDataCenter auflösbar sein, sonst 400 (gleiche
  // Fehlerbehandlung wie beim bestehenden Einzel-PUT).
  const members: GroupMember[] = [];
  let dataCenter: string | undefined;

  for (const rawMember of body.members) {
    if (!isValidRawGroupMember(rawMember))
      return errorResponse(400, "Jedes members[]-Element benötigt world und characterName als nicht-leeren String.");

    const memberDcLookup = lookupDataCenter(rawMember.world);
    if (!memberDcLookup)
      return errorResponse(400, `Unbekannte World "${rawMember.world}" in members[].`);

    members.push({ world: memberDcLookup.canonicalWorld, characterName: rawMember.characterName });

    if (dataCenter === undefined)
      dataCenter = memberDcLookup.dataCenter;
  }

  const key = groupKvKey(groupId);
  const existing = await env.BLUNION_PROFILES.get<StoredGroupProfile>(key, "json");
  const now = new Date().toISOString();

  // Rest 1:1 wie handlePut - alle vier Gruppenfinder-Felder sind im Body optional, fehlt eines,
  // bleibt der bisherige Wert unverändert (bzw. der jeweilige Default bei einer neuen
  // Gruppen-Listung). Nur ein tatsächlich im Body vorhandener, aber ungültiger Wert führt zu 400.
  let visibility: "listed" | "unlisted";
  if (body.visibility === undefined) {
    visibility = existing?.visibility ?? "unlisted";
  } else if (isValidVisibility(body.visibility)) {
    visibility = body.visibility;
  } else {
    return errorResponse(400, 'visibility muss "listed" oder "unlisted" sein.');
  }

  let availabilityTags: string[];
  if (body.availabilityTags === undefined) {
    availabilityTags = existing?.availabilityTags ?? [];
  } else if (isValidAvailabilityTags(body.availabilityTags)) {
    availabilityTags = body.availabilityTags;
  } else {
    return errorResponse(
      400, `availabilityTags enthält ungültige Werte (erlaubt: ${ALLOWED_AVAILABILITY_TAGS.join(", ")}).`);
  }

  let note: string;
  if (body.note === undefined) {
    note = existing?.note ?? "";
  } else if (typeof body.note === "string") {
    note = body.note.slice(0, NOTE_MAX_LENGTH);
  } else {
    return errorResponse(400, "note muss ein String sein.");
  }

  let wantedPlayerCount: number;
  if (body.wantedPlayerCount === undefined) {
    wantedPlayerCount = existing?.wantedPlayerCount ?? 0;
  } else if (isValidWantedPlayerCount(body.wantedPlayerCount)) {
    wantedPlayerCount = body.wantedPlayerCount;
  } else {
    return errorResponse(
      400,
      `wantedPlayerCount muss eine ganze Zahl zwischen ${WANTED_PLAYER_COUNT_MIN} und ${WANTED_PLAYER_COUNT_MAX} sein.`,
    );
  }

  // Gleiches optional/nur-bei-Vorhandensein-validiert-Muster wie die übrigen Phase-2-Felder oben
  // (siehe StoredGroupProfile-Doc) - anders als bei note wird hier NICHT gekappt/normalisiert,
  // sondern bei ungültigen IDs komplett abgelehnt (wie bei availabilityTags): eine erfundene
  // Spell-ID ist ein Korrektheitsproblem, kein reines Längenlimit.
  let targetSpellIds: number[];
  if (body.targetSpellIds === undefined) {
    targetSpellIds = existing?.targetSpellIds ?? [];
  } else if (isValidTargetSpellIds(body.targetSpellIds)) {
    targetSpellIds = body.targetSpellIds;
  } else {
    return errorResponse(
      400,
      `targetSpellIds muss ein Array aus höchstens ${GROUP_TARGET_SPELL_COUNT_MAX} gültigen, bekannten Spell-IDs sein.`,
    );
  }

  let editTokenHash: string;
  let createdAt: string;
  let plaintextEditTokenForResponse: string | undefined;

  if (existing) {
    const providedToken = typeof body.editToken === "string" ? body.editToken : null;
    if (!providedToken)
      return errorResponse(409, "Gruppen-Listung existiert bereits - editToken erforderlich, um sie zu aktualisieren.");

    const providedHash = await sha256Hex(providedToken);
    if (providedHash !== existing.editTokenHash)
      return errorResponse(409, "editToken stimmt nicht mit der gespeicherten Gruppen-Listung überein.");

    editTokenHash = existing.editTokenHash;
    createdAt = existing.createdAt;
  } else {
    const newToken = generateEditToken();
    editTokenHash = await sha256Hex(newToken);
    plaintextEditTokenForResponse = newToken;
    createdAt = now;
  }

  const record: StoredGroupProfile = {
    groupId,
    members,
    editTokenHash,
    visibility,
    availabilityTags,
    note,
    wantedPlayerCount,
    targetSpellIds,
    dataCenter: dataCenter!,
    // Unverändert aus "existing" übernommen (siehe DiscordCard-Doc) - discordCard wird
    // AUSSCHLIESSLICH von syncGroupDiscordCard weiter unten geschrieben, NIE hier direkt gesetzt.
    // Würde man es hier stattdessen weglassen (bzw. auf undefined setzen), ginge die Referenz auf
    // eine bereits bestehende Karte für das kurze Zeitfenster bis zum Abschluss des
    // Hintergrund-Syncs verloren - bei einem (theoretisch) fehlschlagenden Sync sogar dauerhaft,
    // mit einer verwaisten Karte in Discord und einer doppelt angelegten beim nächsten Update als
    // Folge.
    discordCard: existing?.discordCard,
    createdAt,
    updatedAt: now,
  };

  // expirationTtl bei JEDEM Put neu gesetzt, exakt wie bei handlePut (siehe dortigen Kommentar) -
  // ABER: anders als bei Einzelprofilen (die bei jedem gelernten Spell automatisch erneut
  // gepusht werden) gibt es für die Gruppen-Listung selbst KEINEN automatischen Refresh-Trigger
  // (siehe README.md) - eine einmal veröffentlichte Gruppe verschwindet also nach der TTL
  // automatisch, wenn niemand erneut PUT aufruft. In einer eigenen Variable (statt wie bisher
  // inline berechnet), weil syncGroupDiscordCard weiter unten dieselbe TTL für ihren eigenen,
  // NACHGELAGERTEN Put braucht (siehe dortiger Kommentar) - nicht zweimal aus body.ttlHours neu
  // ableiten, sondern exakt denselben Wert wiederverwenden.
  const ttlSeconds = resolveTtlSeconds(body.ttlHours);
  await env.BLUNION_PROFILES.put(key, JSON.stringify(record), { expirationTtl: ttlSeconds });

  // Discord-Kartensynchronisierung (Phase 1.5, siehe syncGroupDiscordCard-Doc) BEWUSST NACH dem
  // eigentlichen KV-Put UND über ctx.waitUntil statt awaited im Request-Pfad selbst (siehe
  // Aufgabenstellung: "Fehler dürfen die Response an den ursprünglichen Caller NICHT
  // beeinflussen") - das Plugin/die Website bekommt seine Antwort dadurch weder verzögert noch
  // riskiert es, dass ein Discord-Ausfall den PUT scheitern lässt.
  ctx.waitUntil(syncGroupDiscordCard(env, key, record, ttlSeconds));

  const responseBody: Record<string, unknown> = stripForGroupResponse(record);
  if (plaintextEditTokenForResponse)
    responseBody.editToken = plaintextEditTokenForResponse;

  return jsonResponse(responseBody, existing ? 200 : 201);
}

/** Kernlogik von GET /groups/browse (Nachschlagen+Filtern+members-Anreicherung), OHNE das
 * HTTP-Cache-Wrapping von handleGroupsBrowse (siehe withCache dort) - aus handleGroupsBrowse
 * herausgelöst (siehe Klassendoc "minimal refaktorieren"), damit der neue Discord-Slash-Command
 * "/blunion browse" (siehe handleDiscordBrowse unten) exakt dieselbe Logik nutzen kann, ohne sie
 * zu duplizieren. Verhalten 1:1 identisch zum vorherigen Code innerhalb des withCache-Callbacks -
 * nur ausgeschnitten, nicht verändert; GET /groups/browse selbst verhält sich dadurch unverändert. */
async function computeGroupsBrowse(env: Env, dataCenter: string | null): Promise<Response> {
  if (!dataCenter)
    return errorResponse(400, 'Query-Parameter "dataCenter" fehlt.');

  const normalizedDataCenter = dataCenter.toLowerCase();
  const results: Record<string, unknown>[] = [];

  let cursor: string | undefined;
  do {
    const listResult = await env.BLUNION_PROFILES.list({ prefix: "group:", cursor });

    for (const listedKey of listResult.keys) {
      const stored = await env.BLUNION_PROFILES.get<StoredGroupProfile>(listedKey.name, "json");
      if (!stored)
        continue; // Zwischen list() und get() gelöscht/abgelaufen - überspringen statt Fehler.

      if (stored.visibility !== "listed" || stored.dataCenter.toLowerCase() !== normalizedDataCenter)
        continue;

      // Pro Mitglied das zugehörige Einzelprofil nachladen (siehe Funktionsdoc oben). Fehlt es
      // (gelöscht/abgelaufen/nie gepusht), wird das Mitglied TROTZDEM aufgelistet, nur mit
      // spellBitmaskBase64: null - NICHT der ganze Gruppen-Treffer verworfen (analog zum
      // bestehenden "Zwischen list() und get() gelöscht"-Muster oben, hier auf Mitglieder- statt
      // Eintrags-Ebene angewendet).
      const members = await Promise.all(stored.members.map(async (member) => {
        const memberProfile = await env.BLUNION_PROFILES.get<StoredProfile>(
          kvKey(member.world, member.characterName), "json");

        return {
          world: member.world,
          characterName: member.characterName,
          spellBitmaskBase64: memberProfile?.spellBitmaskBase64 ?? null,
        };
      }));

      results.push({
        groupId: stored.groupId,
        members,
        availabilityTags: stored.availabilityTags ?? [],
        note: stored.note ?? "",
        wantedPlayerCount: stored.wantedPlayerCount ?? 0,
        targetSpellIds: stored.targetSpellIds ?? [],
      });
    }

    cursor = listResult.list_complete ? undefined : listResult.cursor;
  } while (cursor);

  return jsonResponse(results);
}

/** GET /groups/browse?dataCenter=<DC> - öffentlicher Gruppen-Gruppenfinder (Phase 2) - dünner
 * HTTP-Wrapper um computeGroupsBrowse (siehe dort für die eigentliche Logik): liest "dataCenter"
 * aus der Query und legt NUR die Berechnung selbst hinter withCache (siehe dortigen Kommentar zum
 * selben Muster bei handleBrowse) - der "dataCenter fehlt"-400-Fehler wird über response.ok in
 * withCache ohnehin nie gecached. */
async function handleGroupsBrowse(env: Env, request: Request, ctx: ExecutionContext): Promise<Response> {
  return withCache(request, ctx, () => {
    const url = new URL(request.url);
    return computeGroupsBrowse(env, url.searchParams.get("dataCenter"));
  });
}

/** DELETE /group/:groupId - X-Edit-Token-Header, exakt wie handleDelete für Einzelprofile.
 *
 * WICHTIG: löscht AUSSCHLIESSLICH den "group:"-Eintrag (Mitgliederliste/Tags/Notiz) - rührt NIE
 * an den referenzierten "profile:"-Einträgen der Mitglieder, die bleiben unabhängig davon
 * bestehen (das ist der ganze Punkt des Referenz-statt-Kopie-Ansatzes, siehe Klassendoc/
 * StoredGroupProfile). */
async function handleGroupDelete(
  env: Env, request: Request, groupId: string, ctx: ExecutionContext,
): Promise<Response> {
  const rateLimited = await enforceWriteRateLimit(env, request);
  if (rateLimited)
    return rateLimited;

  const token = request.headers.get("X-Edit-Token");
  if (!token)
    return errorResponse(403, 'Header "X-Edit-Token" fehlt.');

  const key = groupKvKey(groupId);
  const existing = await env.BLUNION_PROFILES.get<StoredGroupProfile>(key, "json");
  if (!existing)
    return errorResponse(404, "Keine Gruppen-Listung mit dieser groupId gefunden.");

  const providedHash = await sha256Hex(token);
  if (providedHash !== existing.editTokenHash)
    return errorResponse(403, "editToken stimmt nicht mit der gespeicherten Gruppen-Listung überein.");

  await env.BLUNION_PROFILES.delete(key);

  // Discord-Karten-Aufräumen (Phase 1.5, siehe Aufgabenstellung Punkt 5) NACH dem KV-Delete, über
  // ctx.waitUntil statt awaited (siehe identische Begründung in handleGroupPut) - "existing" wurde
  // bereits VOR dem Delete gelesen und trägt damit noch die zu löschende discordCard, falls
  // vorhanden.
  if (existing.discordCard)
    ctx.waitUntil(removeGroupDiscordCard(env, groupId, existing.discordCard));

  return jsonResponse({ deleted: true });
}

/** Verifiziert die Ed25519-Signatur eines eingehenden Discord-Interaction-Requests (siehe
 * https://discord.com/developers/docs/interactions/overview#setting-up-an-endpoint) - MUSS als
 * ALLERERSTER Schritt in handleDiscordInteractions passieren, VOR jeder weiteren Verarbeitung des
 * Bodys (siehe Aufgabenstellung), sonst könnte ein Angreifer beliebige, nicht von Discord
 * stammende "Interactions" einschleusen.
 *
 * Bewusst über die Web Crypto API (SubtleCrypto) statt Node-"crypto": Node-"crypto" ist in der
 * Workers-Runtime nicht verfügbar (siehe Aufgabenstellung), SubtleCrypto dagegen schon immer.
 * signature/publicKey kommen als Hex-Strings (siehe Discord-Doku bzw. hier env.DISCORD_PUBLIC_KEY,
 * siehe Env-Doc oben) - importKey erwartet dafür rohe Bytes, siehe hexToBytes (crypto.ts). Die zu
 * verifizierende Nachricht ist laut Discord-Doku exakt "timestamp + rawBody" (String-Konkatenation,
 * dann UTF-8-kodiert), NICHT nur der Body allein - deshalb braucht diese Funktion den rohen,
 * NOCH NICHT geparsten Body als eigenes Argument (siehe Aufrufstelle: request.text() statt
 * request.json(), damit exakt dieselben Bytes signiert/verifiziert werden, die Discord gesendet
 * hat, unabhängig von JSON.stringify-Rundungsfehlern beim Re-Serialisieren).
 *
 * Gibt bei JEDEM Problem (fehlender Header, kein DISCORD_PUBLIC_KEY konfiguriert, ungültiges Hex,
 * falsche Signatur) einheitlich false zurück statt zu werfen - der Aufrufer muss dadurch nur EINEN
 * Fall behandeln ("ungültig -> 401"), egal aus welchem Grund die Verifikation fehlschlug. */
async function verifyDiscordSignature(env: Env, request: Request, rawBody: string): Promise<boolean> {
  const signatureHeader = request.headers.get("X-Signature-Ed25519");
  const timestampHeader = request.headers.get("X-Signature-Timestamp");
  if (!signatureHeader || !timestampHeader || !env.DISCORD_PUBLIC_KEY)
    return false;

  try {
    const publicKey = await crypto.subtle.importKey(
      "raw",
      hexToBytes(env.DISCORD_PUBLIC_KEY),
      { name: "Ed25519" },
      false,
      ["verify"],
    );

    const message = new TextEncoder().encode(timestampHeader + rawBody);
    return await crypto.subtle.verify({ name: "Ed25519" }, publicKey, hexToBytes(signatureHeader), message);
  } catch {
    // Ungültiges Hex (falsche Länge/Zeichen) in Header ODER Secret landet hier (siehe
    // hexToBytes-Doc) - wie jeder andere Verifikationsfehlschlag als "ungültige Signatur"
    // behandelt, nicht als Serverfehler (kein 500).
    return false;
  }
}

/** Baut EIN "field" für das Discord-Browse-Embed aus einer Gruppe (siehe buildGroupsBrowseEmbed) -
 * Gruppenname existiert nicht als eigenes Feld (siehe StoredGroupProfile), "note" übernimmt diese
 * Rolle im UI. Mitgliederliste als "World CharacterName" pro Zeile (siehe Aufgabenstellung) -
 * bewusst OHNE spellBitmaskBase64 (für Menschen im Discord-Embed nicht lesbar/nützlich, anders als
 * fürs Plugin) und OHNE editToken/sonstige interne Felder (die stehen ohnehin nicht im Ergebnis
 * von computeGroupsBrowse, siehe dort). */
function buildGroupEmbedField(group: DiscordBrowseGroup): { name: string; value: string; inline: boolean } {
  const memberList = group.members
    .map((member) => `${member.world} ${member.characterName}`)
    .join("\n");

  // wantedPlayerCount === 0 bedeutet "egal wie viele" (siehe WANTED_PLAYER_COUNT_MIN-Doc oben) -
  // dafür nur die aktuelle Mitgliederzahl zeigen statt eines verwirrenden "3/0".
  const memberCountLabel = group.wantedPlayerCount > 0
    ? `${group.members.length}/${group.wantedPlayerCount}`
    : `${group.members.length}`;

  const availabilityLabel = group.availabilityTags.length > 0 ? group.availabilityTags.join(", ") : "-";

  return {
    name: group.note.length > 0 ? group.note : "(ohne Notiz)",
    value: `Mitglieder (${memberCountLabel}):\n${memberList}\n\nVerfügbarkeit: ${availabilityLabel}`,
    inline: false,
  };
}

/** Baut das Embed für EINE persistente Gruppen-Karte in einem Regions-Kanal (Phase 1.5, siehe
 * syncGroupDiscordCard unten) - WIEDERVERWENDET bewusst buildGroupEmbedField (siehe
 * Aufgabenstellung Punkt 3 "Kartenformat soll sich zwischen /blunion browse und den persistenten
 * Kanal-Karten nicht auseinanderentwickeln") statt eine zweite, eigene Notiz/Mitglieder/
 * Verfügbarkeits-Formatierung zu pflegen. StoredGroupProfile.members ist strukturell bereits
 * exakt DiscordBrowseGroupMember (world+characterName, siehe dortige Interfaces) - keine
 * Umwandlung nötig.
 *
 * Anders als buildGroupsBrowseEmbed (das MEHRERE Gruppen eines Data Centers in einem Embed
 * auflistet) zeigt eine Kanal-Karte GENAU EINE Gruppe - das konkrete Data Center (wichtig: der
 * Kanal selbst ist ja nach REGION, nicht DC gruppiert, siehe DATA_CENTER_TO_REGION-Doc) steht
 * deshalb hier im Embed-Titel statt wie beim Browse-Embed einmal außen für alle Treffer gemeinsam. */
function buildGroupCardEmbed(record: StoredGroupProfile): Record<string, unknown> {
  const field = buildGroupEmbedField({
    groupId: record.groupId,
    members: record.members,
    availabilityTags: record.availabilityTags ?? [],
    note: record.note ?? "",
    wantedPlayerCount: record.wantedPlayerCount ?? 0,
  });

  return {
    title: `Blue Mage Gruppe auf ${record.dataCenter}`,
    color: 0x2b6cb0,
    fields: [field],
  };
}

/** Legt für eine (bereits als "listed" bestätigte) Gruppe eine NEUE Discord-Karte an - gemeinsam
 * von syncGroupDiscordCard für den "noch keine Karte"- UND den "Region gewechselt"-Fall genutzt
 * (siehe dort). Pflegt bei Erfolg zusätzlich den discordcards:<region>-Index mit (siehe
 * addDiscordCardIndexEntry/DiscordCardIndexEntry-Doc) - bei einem Fehlschlag (siehe
 * createWebhookMessage: liefert dann null) bleibt der Index unangetastet und die Gruppe hat
 * schlicht (weiterhin) keine Karte, siehe Rückgabewert undefined. */
async function createGroupDiscordCard(
  env: Env, region: Region, webhookUrl: string, record: StoredGroupProfile,
): Promise<DiscordCard | undefined> {
  const messageId = await createWebhookMessage(webhookUrl, buildGroupCardEmbed(record));
  if (!messageId)
    return undefined;

  await addDiscordCardIndexEntry(env, region, record.groupId, messageId);
  return { region, channelWebhookId: extractWebhookId(webhookUrl) ?? "", messageId };
}

/** Entfernt die Discord-Karte EINER Gruppe vollständig (Nachricht löschen + Index-Eintrag
 * entfernen) - gemeinsam genutzt von syncGroupDiscordCard (Gruppe wird unlisted/Region wechselt),
 * handleGroupDelete (Gruppe wird komplett gelöscht) UND cleanupOrphanedDiscordCards (Cron-Aufräumen
 * verwaister Karten nach stillem TTL-Ablauf) - alle drei Stellen sollen sich exakt gleich
 * verhalten, siehe jeweilige Aufrufstellen. Wirft nie (siehe deleteWebhookMessage/
 * removeDiscordCardIndexEntry - beide fehlerisoliert). */
async function removeGroupDiscordCard(env: Env, groupId: string, card: DiscordCard): Promise<void> {
  const webhookUrl = getRegionWebhookUrl(env, card.region);
  if (webhookUrl)
    await deleteWebhookMessage(webhookUrl, card.messageId);

  await removeDiscordCardIndexEntry(env, card.region, groupId);
}

function discordCardsEqual(a: DiscordCard | undefined, b: DiscordCard | undefined): boolean {
  if (a === b)
    return true;
  if (!a || !b)
    return false;

  return a.region === b.region && a.channelWebhookId === b.channelWebhookId && a.messageId === b.messageId;
}

/** Nach einem erfolgreichen /group/:groupId-PUT (siehe handleGroupPut) im Hintergrund aufgerufen
 * (siehe dortiges ctx.waitUntil) - legt/aktualisiert/entfernt die persistente Discord-Kanal-Karte
 * für diese Gruppe (Phase 1.5, siehe Aufgabenstellung Punkt 4). Läuft KOMPLETT nach der bereits
 * abgeschickten Antwort an den Aufrufer ab; jeder Fehler bleibt innerhalb dieser Funktion (siehe
 * discordWebhook.ts-Klassendoc - keine der dort exportierten Funktionen wirft).
 *
 * `key`/`ttlSeconds` kommen 1:1 von handleGroupPut (dieselbe expirationTtl wie beim ursprünglichen
 * Put) - der abschließende KV-Put hier unten schreibt NUR ein ggf. geändertes discordCard-Feld,
 * alle anderen Felder bleiben exakt `record` wie vom aufrufenden PUT bereits gespeichert. */
async function syncGroupDiscordCard(
  env: Env, key: string, record: StoredGroupProfile, ttlSeconds: number,
): Promise<void> {
  const existingCard = record.discordCard;
  const region = regionForDataCenter(record.dataCenter);
  const webhookUrl = region ? getRegionWebhookUrl(env, region) : undefined;

  let nextCard: DiscordCard | undefined;

  if (record.visibility !== "listed") {
    // Nicht (mehr) gelistet - eine ggf. vorhandene Karte entfernen, keine neue anlegen (siehe
    // Aufgabenstellung Punkt 4). Wichtig: über existingCard.region gehen, NICHT über die (evtl.
    // andere) aktuell berechnete "region" - die alte Karte hängt ja im WEBHOOK DER ALTEN Region.
    if (existingCard)
      await removeGroupDiscordCard(env, record.groupId, existingCard);

    nextCard = undefined;
  } else if (!region || !webhookUrl) {
    // Kein Webhook für diese Region konfiguriert (z.B. lokale Entwicklung ohne alle vier Secrets,
    // siehe Env-Doc, oder ein Data Center ohne Region-Zuordnung) - die Kartenfunktion ist rein
    // optional (siehe Aufgabenstellung), also nichts tun statt zu werfen. Eine eventuell
    // BESTEHENDE Karte bleibt dabei unangetastet (sie wurde ja unter einem damals funktionierenden
    // Webhook angelegt) statt sie hier fälschlich als "weg" zu behandeln.
    nextCard = existingCard;
  } else if (!existingCard) {
    nextCard = await createGroupDiscordCard(env, region, webhookUrl, record);
  } else if (existingCard.region !== region) {
    // Gruppe auf ein anderes Data Center/eine andere Region verschoben (siehe Aufgabenstellung
    // Punkt 4) - alte Karte im ALTEN Webhook löschen (editWebhookMessage über einen ANDEREN Webhook
    // hinweg funktioniert bei Discord nicht), neue im NEUEN Webhook anlegen.
    await removeGroupDiscordCard(env, record.groupId, existingCard);
    nextCard = await createGroupDiscordCard(env, region, webhookUrl, record);
  } else {
    // Gleiche Region - bestehende Nachricht aktualisieren statt eine zweite anzulegen.
    await editWebhookMessage(webhookUrl, existingCard.messageId, buildGroupCardEmbed(record));
    nextCard = existingCard; // region/channelWebhookId/messageId unverändert.
  }

  // Nur erneut in KV schreiben, wenn sich discordCard tatsächlich geändert hat - der allermeiste
  // Fall ("gleiche Region, nur Inhalt editiert" ODER "kein Webhook konfiguriert") braucht gar
  // keinen zweiten Put.
  if (!discordCardsEqual(existingCard, nextCard)) {
    const updated: StoredGroupProfile = { ...record, discordCard: nextCard };
    await env.BLUNION_PROFILES.put(key, JSON.stringify(updated), { expirationTtl: ttlSeconds });
  }
}

/** Discord-Embeds erlauben höchstens 25 "fields" (harte API-Grenze) - mehr Treffer würden von
 * Discord komplett abgelehnt statt nur gekappt. Phase 1 kappt deshalb selbst und weist im
 * description-Feld auf die Kappung hin, statt sich auf Discord zu verlassen. */
const DISCORD_EMBED_MAX_GROUP_FIELDS = 25;

/** Baut das komplette Discord-Embed für "/blunion browse" aus dem (bereits gefilterten) Ergebnis
 * von computeGroupsBrowse (siehe handleDiscordBrowse) - siehe buildGroupEmbedField für ein
 * einzelnes Gruppen-Feld. dataCenter kommt hier NUR für die Titelzeile zum Einsatz (das Ergebnis
 * selbst enthält kein dataCenter-Feld je Gruppe, siehe DiscordBrowseGroup/computeGroupsBrowse -
 * alle Treffer teilen ohnehin dasselbe, vom Aufrufer angegebene Data Center). */
function buildGroupsBrowseEmbed(dataCenter: string, groups: DiscordBrowseGroup[]): Record<string, unknown> {
  const shownGroups = groups.slice(0, DISCORD_EMBED_MAX_GROUP_FIELDS);

  return {
    title: `Blue Mage Gruppen auf ${dataCenter}`,
    description: groups.length > shownGroups.length
      ? `Zeige ${shownGroups.length} von ${groups.length} gelisteten Gruppen.`
      : undefined,
    color: 0x2b6cb0,
    fields: shownGroups.map(buildGroupEmbedField),
  };
}

/** Action-Row mit einem Link-Button zur bestehenden Web-Companion-Seite (siehe
 * COMPANION_WEBSITE_URL-Doc oben) - style 5 ("Link") braucht laut Discord-API KEINE custom_id
 * (anders als z.B. ein Button, der eine eigene Interaction auslösen würde) und öffnet die URL
 * direkt im Browser. Als Konstante statt bei jedem Aufruf neu gebaut, da sie sich nie ändert. */
const DISCORD_WEBSITE_LINK_COMPONENTS = [
  {
    type: 1, // Action Row
    components: [
      {
        type: 2, // Button
        style: 5, // Link
        label: "Companion-Website öffnen",
        url: COMPANION_WEBSITE_URL,
      },
    ],
  },
];

/** Baut eine direkte Discord-Interaction-Response (type 4, siehe DISCORD_RESPONSE_TYPE_
 * CHANNEL_MESSAGE_WITH_SOURCE-Doc oben) mit reinem Text-Inhalt - für Fehlermeldungen/leere
 * Ergebnisse (siehe handleDiscordBrowse), wo ein leeres Embed keinen Mehrwert hätte (siehe
 * Aufgabenstellung "klare, freundliche Meldung statt leerem Embed"). */
function discordMessageResponse(content: string): Response {
  return jsonResponse({
    type: DISCORD_RESPONSE_TYPE_CHANNEL_MESSAGE_WITH_SOURCE,
    data: { content },
  });
}

/** "/blunion browse [datacenter]" - reicht dieselbe Kernlogik wie GET /groups/browse (siehe
 * computeGroupsBrowse) durch und formatiert das Ergebnis als Discord-Embed (siehe
 * buildGroupsBrowseEmbed). Nur visibility==="listed"-Gruppen sind überhaupt im Ergebnis von
 * computeGroupsBrowse enthalten (siehe dort) - hier also nichts zusätzlich zu tun, um das
 * sicherzustellen (siehe Aufgabenstellung Punkt 4).
 *
 * dataCenter ist die vom Discord-Nutzer als Sub-Command-Option übergebene "datacenter"-Option
 * (siehe handleDiscordApplicationCommand) - anders als beim HTTP-Endpoint hier absichtlich KEIN
 * hartes 400, sondern eine normale Chat-Antwort bei fehlendem/unbekanntem Wert (siehe
 * Aufgabenstellung Punkt 4 "Fehlermeldung analog zum bestehenden Verhalten von GET
 * /groups/browse"): eine rohe HTTP-4xx-Response würde Discord nur als "App hat nicht geantwortet"
 * anzeigen, keine lesbare Fehlermeldung im Kanal. */
async function handleDiscordBrowse(env: Env, dataCenter: string | undefined): Promise<Response> {
  if (!dataCenter) {
    return discordMessageResponse(
      'Bitte ein Data Center angeben, z.B. "/blunion browse datacenter:Aether".',
    );
  }

  const browseResponse = await computeGroupsBrowse(env, dataCenter);
  if (!browseResponse.ok) {
    const errorBody = await browseResponse.json().catch(() => null) as { error?: string } | null;
    return discordMessageResponse(errorBody?.error ?? "Beim Abrufen der Gruppen ist ein Fehler aufgetreten.");
  }

  const groups = await browseResponse.json<DiscordBrowseGroup[]>();
  if (groups.length === 0)
    return discordMessageResponse(`Keine öffentlich gelisteten Gruppen auf "${dataCenter}" gefunden.`);

  return jsonResponse({
    type: DISCORD_RESPONSE_TYPE_CHANNEL_MESSAGE_WITH_SOURCE,
    data: {
      embeds: [buildGroupsBrowseEmbed(dataCenter, groups)],
      components: DISCORD_WEBSITE_LINK_COMPONENTS,
    },
  });
}

/** Sucht in den options[] einer Command-Interaction den Sub-Command mit dem gegebenen Namen (siehe
 * DiscordInteractionCommandData-Doc oben zur Verschachtelung) - DISCORD_OPTION_TYPE_SUB_COMMAND
 * (1) grenzt das gegen andere, potenzielle Top-Level-Optionen ab. */
function findDiscordSubcommand(
  data: DiscordInteractionCommandData | undefined,
  name: string,
): DiscordInteractionOption | undefined {
  return data?.options?.find((option) => option.type === DISCORD_OPTION_TYPE_SUB_COMMAND && option.name === name);
}

/** Liest den String-Wert EINER benannten Option aus den options[] eines Sub-Commands (siehe
 * findDiscordSubcommand) - z.B. "datacenter" aus "/blunion browse datacenter:Aether". undefined,
 * wenn die Option fehlt oder (sollte nie passieren, Discord validiert den Typ serverseitig gegen
 * die registrierte Command-Definition) keinen String-Wert trägt. */
function findDiscordStringOption(options: DiscordInteractionOption[] | undefined, name: string): string | undefined {
  const option = options?.find((candidate) => candidate.name === name);
  return typeof option?.value === "string" ? option.value : undefined;
}

/** Dispatcht eine APPLICATION_COMMAND-Interaction (siehe handleDiscordInteractions) - Phase 1
 * kennt ausschließlich "/blunion browse" (siehe Aufgabenstellung: "noch KEIN Linking, KEIN
 * /blunion link, KEINE Schreiboperationen"). Unbekannte Commands/Sub-Commands enden in einer
 * normalen Chat-Antwort statt eines Fehlerstatus (siehe handleDiscordBrowse-Doc zum selben Grund):
 * kann bei einer künftig geänderten Command-Registrierung auftreten (z.B. ein Sub-Command wird
 * umbenannt, aber der alte Command bleibt in einem Server noch gecached), soll dann aber nicht wie
 * ein Serverfehler wirken. */
async function handleDiscordApplicationCommand(env: Env, interaction: DiscordInteraction): Promise<Response> {
  if (interaction.data?.name !== "blunion")
    return discordMessageResponse(`Unbekannter Command "${interaction.data?.name ?? "?"}".`);

  const browseSubcommand = findDiscordSubcommand(interaction.data, "browse");
  if (!browseSubcommand)
    return discordMessageResponse('Unbekannter Sub-Command - aktuell wird nur "browse" unterstützt.');

  const dataCenter = findDiscordStringOption(browseSubcommand.options, "datacenter");
  return handleDiscordBrowse(env, dataCenter);
}

/** POST /discord/interactions - Einstiegspunkt der neuen, rein lesenden Discord-Integration Phase
 * 1 (siehe DISCORD_INTERACTIONS_PATH/DISCORD_INTEGRATION.md). Reihenfolge ist bewusst so und NICHT
 * vertauschbar (siehe Aufgabenstellung "Signaturverifikation als ERSTER Schritt"):
 *   1. rawBody per request.text() lesen (EINMAL, siehe verifyDiscordSignature-Doc zum
 *      Body-ist-ein-einmal-lesbarer-Stream-Problem - JSON.parse danach arbeitet auf demselben
 *      bereits gelesenen String, kein zweiter request.json()-Aufruf nötig/möglich).
 *   2. Signatur verifizieren - JEDER Fehlschlag (inkl. fehlender Header) führt SOFORT zu 401, OHNE
 *      den Body weiter zu verarbeiten (kein JSON.parse, kein Dispatch).
 *   3. ERST DANACH den (jetzt vertrauenswürdigen) Body als JSON parsen und nach Interaction-Typ
 *      dispatchen (PING -> PONG, APPLICATION_COMMAND -> handleDiscordApplicationCommand, alles
 *      andere -> Fehlerantwort ohne Crash). */
async function handleDiscordInteractions(env: Env, request: Request): Promise<Response> {
  const rawBody = await request.text();

  const isValidSignature = await verifyDiscordSignature(env, request, rawBody);
  if (!isValidSignature)
    return new Response("Ungültige Anfrage-Signatur.", { status: 401 });

  let interaction: DiscordInteraction;
  try {
    interaction = JSON.parse(rawBody);
  } catch {
    return errorResponse(400, "Ungültiger JSON-Body.");
  }

  switch (interaction.type) {
    case DISCORD_INTERACTION_TYPE_PING:
      // Von Discord beim Einrichten der Interactions Endpoint URL im Developer Portal gefordert
      // (siehe DISCORD_INTEGRATION.md) - OHNE eine korrekte PONG-Antwort verweigert Discord das
      // Speichern der Endpoint-URL.
      return jsonResponse({ type: DISCORD_RESPONSE_TYPE_PONG });

    case DISCORD_INTERACTION_TYPE_APPLICATION_COMMAND:
      return handleDiscordApplicationCommand(env, interaction);

    default:
      return errorResponse(400, `Unbekannter Interaction-Typ ${interaction.type}.`);
  }
}

/** Cron-Cleanup verwaister Discord-Karten (Phase 1.5, siehe scheduled-Handler unten und die
 * ausführliche "warum ein separater Index nötig ist"-Begründung an DiscordCardIndexEntry oben) -
 * pro Region den discordcards:<region>-Index durchgehen und für jeden {groupId, messageId}-Eintrag
 * per get() prüfen, ob "group:<groupId>" noch existiert. Existiert die Gruppe noch, ist ihre Karte
 * ohnehin bereits über syncGroupDiscordCard aktuell gehalten - hier nichts zu tun. Existiert sie
 * NICHT mehr (stiller TTL-Ablauf ohne vorheriges DELETE), wird die Karte über dieselbe
 * removeGroupDiscordCard-Funktion entfernt, die auch der explizite DELETE-Pfad nutzt (siehe
 * handleGroupDelete) - identisches Verhalten aus einer einzigen Stelle.
 *
 * "channelWebhookId" im übergebenen DiscordCard-Objekt ist hier bewusst ein Platzhalter (""): der
 * Index (siehe DiscordCardIndexEntry) speichert ihn gar nicht erst mit, weil removeGroupDiscordCard
 * ihn ohnehin nicht braucht (nur region+messageId fließen in deleteWebhookMessage/
 * getRegionWebhookUrl ein) - eine vierte, hier nutzlose Kopie des Werts zu pflegen wäre unnötig. */
async function cleanupOrphanedDiscordCards(env: Env): Promise<void> {
  for (const region of ALL_REGIONS) {
    const entries = await env.BLUNION_PROFILES.get<DiscordCardIndexEntry[]>(discordCardsIndexKey(region), "json");
    if (!entries || entries.length === 0)
      continue;

    for (const entry of entries) {
      const stillExists = await env.BLUNION_PROFILES.get(groupKvKey(entry.groupId));
      if (stillExists === null)
        await removeGroupDiscordCard(env, entry.groupId, { region, channelWebhookId: "", messageId: entry.messageId });
    }
  }
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    if (request.method === "OPTIONS")
      return new Response(null, { status: 204, headers: CORS_HEADERS });

    const url = new URL(request.url);

    // Vor dem PROFILE_PATH-Abgleich geprüft, obwohl "/profiles/browse" ohnehin nie auf
    // PROFILE_PATH matchen würde (dessen erstes Segment ist literal "profile", nicht
    // "profiles") - explizit hier oben als eigener Zweig, damit der Gruppenfinder-Endpoint im
    // Code genauso sichtbar/prominent ist wie die drei /profile/-Endpunkte, nicht als
    // Nebenbemerkung im 404-Fallback versteckt.
    if (BROWSE_PATH.test(url.pathname)) {
      if (request.method !== "GET")
        return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);

      return handleBrowse(env, request, ctx);
    }

    // Analog zu BROWSE_PATH oben, nur für den Gruppen-Gruppenfinder (siehe handleGroupsBrowse) -
    // ebenfalls VOR dem GROUP_PATH-Abgleich geprüft (aus demselben Grund: "/groups/browse" würde
    // wegen des zusätzlichen "s" ohnehin nie auf GROUP_PATH matchen, aber explizit oben bleibt
    // der Endpoint im Code genauso sichtbar wie /group/:groupId).
    if (GROUPS_BROWSE_PATH.test(url.pathname)) {
      if (request.method !== "GET")
        return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);

      return handleGroupsBrowse(env, request, ctx);
    }

    // Neue, rein lesende Discord-Slash-Command-Integration Phase 1 (siehe
    // DISCORD_INTEGRATION.md/handleDiscordInteractions) - ebenfalls als eigener, sichtbarer Zweig
    // VOR den PROFILE_PATH/GROUP_PATH-Regex-Abgleichen (die für "/discord/interactions" ohnehin
    // nie matchen würden, siehe Begründung bei BROWSE_PATH/GROUPS_BROWSE_PATH oben).
    if (DISCORD_INTERACTIONS_PATH.test(url.pathname)) {
      if (request.method !== "POST")
        return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);

      return handleDiscordInteractions(env, request);
    }

    const profileMatch = url.pathname.match(PROFILE_PATH);

    if (profileMatch) {
      // decodeURIComponent statt der rohen Pfadsegmente: Charakternamen enthalten oft
      // Leerzeichen ("Y'shtola Rhul") und ggf. Apostrophe/Unicode - das Plugin MUSS beim Aufbau
      // der URL encodeURIComponent verwenden (siehe LiveSyncService.cs), hier entsprechend
      // wieder dekodiert.
      const world = decodeURIComponent(profileMatch[1]!);
      const characterName = decodeURIComponent(profileMatch[2]!);

      if (world.length === 0 || characterName.length === 0)
        return errorResponse(400, "world und characterName dürfen nicht leer sein.");

      switch (request.method) {
        case "GET":
          return handleGet(env, world, characterName);
        case "PUT":
          return handlePut(env, request, world, characterName);
        case "DELETE":
          return handleDelete(env, request, world, characterName);
        default:
          return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);
      }
    }

    const groupMatch = url.pathname.match(GROUP_PATH);

    if (groupMatch) {
      const groupId = decodeURIComponent(groupMatch[1]!);

      if (groupId.length === 0)
        return errorResponse(400, "groupId darf nicht leer sein.");

      switch (request.method) {
        case "PUT":
          return handleGroupPut(env, request, groupId, ctx);
        case "DELETE":
          return handleGroupDelete(env, request, groupId, ctx);
        default:
          return errorResponse(405, `Methode "${request.method}" wird für diesen Endpoint nicht unterstützt.`);
      }
    }

    return errorResponse(
      404,
      'Unbekannter Endpoint - erwartet wird "/profile/:world/:characterName", "/profiles/browse", ' +
        '"/group/:groupId", "/groups/browse" oder "/discord/interactions".',
    );
  },

  /** Cloudflare Cron Trigger (siehe wrangler.toml [triggers], Phase 1.5) - räumt Discord-Karten von
   * Gruppen auf, deren KV-Eintrag still per expirationTtl abgelaufen ist, OHNE dass vorher DELETE
   * /group/:groupId aufgerufen wurde (siehe Klassendoc am Dateianfang "KV löst dabei KEINEN Code
   * aus" und die ausführliche Begründung an DiscordCardIndexEntry oben, WARUM das ohne den
   * discordcards:<region>-Index gar nicht erkennbar wäre). Über ctx.waitUntil, damit der
   * Cron-Aufruf selbst nicht auf die komplette Bereinigung warten muss, um als "erfolgreich
   * angenommen" zu gelten - Fehler einzelner Lösch-Calls sind ohnehin bereits in
   * removeGroupDiscordCard/discordWebhook.ts abgefangen. */
  async scheduled(_controller: ScheduledController, env: Env, ctx: ExecutionContext): Promise<void> {
    ctx.waitUntil(cleanupOrphanedDiscordCards(env));
  },
};
