/**
 * World -> Data Center-Zuordnung, fest hinterlegt statt über einen externen Dienst abgefragt.
 *
 * Warum hart hinterlegt statt z.B. per Lumina/XIVAPI-Abfrage: die FFXIV-Weltenliste ändert sich
 * praktisch nie (neue Welten kommen alle paar Jahre in ganzen DC-Batches dazu, bestehende werden
 * nicht umbenannt) - ein externer Abruf wäre für diesen Umfang unnötiger Aufwand/eine unnötige
 * zusätzliche Fehlerquelle (Netzwerk-Abhängigkeit *innerhalb* der Netzwerk-Abhängigkeit). Bei
 * einer künftigen DC-Erweiterung (z.B. neues NA-DC) muss diese Tabelle manuell nachgezogen werden.
 *
 * Nur für die "dataCenter"-Feld-Befüllung gebraucht (siehe index.ts) - der eigentliche Profil-
 * Lookup in Phase 1 läuft ausschließlich über world+characterName (siehe KV-Key-Schema), die
 * dataCenter-Angabe ist rein für das spätere Phase-2-Browsing vorbereitet.
 *
 * Keys sind die KANONISCHE Schreibweise (wie im Spiel angezeigt) - der Lookup in
 * {@link lookupDataCenter} vergleicht selbst case-insensitive, damit die tatsächliche
 * Groß-/Kleinschreibung des Aufrufers keine Rolle spielt.
 */
export const WORLD_DATA_CENTERS: Readonly<Record<string, string>> = {
  // Aether (NA)
  Adamantoise: "Aether",
  Cactuar: "Aether",
  Faerie: "Aether",
  Gilgamesh: "Aether",
  Jenova: "Aether",
  Midgardsormr: "Aether",
  Sargatanas: "Aether",
  Siren: "Aether",

  // Crystal (NA)
  Balmung: "Crystal",
  Brynhildr: "Crystal",
  Coeurl: "Crystal",
  Diabolos: "Crystal",
  Goblin: "Crystal",
  Malboro: "Crystal",
  Mateus: "Crystal",
  Zalera: "Crystal",

  // Dynamis (NA)
  Halicarnassus: "Dynamis",
  Maduin: "Dynamis",
  Marilith: "Dynamis",
  Seraph: "Dynamis",
  Cuchulainn: "Dynamis",
  Golem: "Dynamis",
  Kraken: "Dynamis",
  Rafflesia: "Dynamis",

  // Primal (NA)
  Behemoth: "Primal",
  Excalibur: "Primal",
  Exodus: "Primal",
  Famfrit: "Primal",
  Hyperion: "Primal",
  Lamia: "Primal",
  Leviathan: "Primal",
  Ultros: "Primal",

  // Chaos (EU)
  Cerberus: "Chaos",
  Louisoix: "Chaos",
  Moogle: "Chaos",
  Omega: "Chaos",
  Phantom: "Chaos",
  Ragnarok: "Chaos",
  Sagittarius: "Chaos",
  Spriggan: "Chaos",

  // Light (EU)
  Alpha: "Light",
  Lich: "Light",
  Odin: "Light",
  Phoenix: "Light",
  Raiden: "Light",
  Shiva: "Light",
  Twintania: "Light",
  Zodiark: "Light",

  // Elemental (JP)
  Aegis: "Elemental",
  Atomos: "Elemental",
  Carbuncle: "Elemental",
  Garuda: "Elemental",
  Gungnir: "Elemental",
  Kujata: "Elemental",
  Tonberry: "Elemental",
  Typhon: "Elemental",

  // Gaia (JP)
  Alexander: "Gaia",
  Bahamut: "Gaia",
  Durandal: "Gaia",
  Fenrir: "Gaia",
  Ifrit: "Gaia",
  Ridill: "Gaia",
  Tiamat: "Gaia",
  Ultima: "Gaia",

  // Mana (JP)
  Anima: "Mana",
  Asura: "Mana",
  Chocobo: "Mana",
  Hades: "Mana",
  Ixion: "Mana",
  Masamune: "Mana",
  Pandaemonium: "Mana",
  Titan: "Mana",

  // Meteor (JP)
  Belias: "Meteor",
  Mandragora: "Meteor",
  Ramuh: "Meteor",
  Shinryu: "Meteor",
  Unicorn: "Meteor",
  Valefor: "Meteor",
  Yojimbo: "Meteor",
  Zeromus: "Meteor",

  // Materia (OCE)
  Bismarck: "Materia",
  Ravana: "Materia",
  Sephirot: "Materia",
  Sophia: "Materia",
  Zurvan: "Materia",
};

export interface DataCenterLookupResult {
  /** Kanonische Schreibweise der World (aus der Tabelle, nicht die vom Aufrufer übergebene
   * Groß-/Kleinschreibung) - wird so auch im gespeicherten Profil abgelegt, damit z.B.
   * "gilgamesh" und "Gilgamesh" garantiert dasselbe "world"-Feld im JSON-Value ergeben, auch
   * wenn der KV-Key selbst (siehe index.ts) ohnehin schon lowercase ist. */
  canonicalWorld: string;
  dataCenter: string;
}

/** Case-insensitiver Lookup - liefert null bei unbekannter World (siehe index.ts: führt zu
 * einer 400-Antwort statt eines geratenen/leeren dataCenter-Werts). */
export function lookupDataCenter(world: string): DataCenterLookupResult | null {
  const normalized = world.toLowerCase();
  for (const [canonicalWorld, dataCenter] of Object.entries(WORLD_DATA_CENTERS)) {
    if (canonicalWorld.toLowerCase() === normalized)
      return { canonicalWorld, dataCenter };
  }
  return null;
}
