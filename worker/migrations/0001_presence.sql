-- Online-Status für gelistete Group-Finder-Einträge (siehe handlePresencePost/computePlayersBrowse in
-- src/index.ts). Nur Profile mit sharePresence = true haben eine Zeile; gesetzt ausschließlich vom
-- eigenen Plugin des Spielers. D1 statt KV wegen der KV-Schreiblimits (ein Lebenszeichen pro Stunde
-- und Spieler).
--
-- world/name_lower/data_center jeweils kleingeschrieben (wie kvKey im Worker).
-- last_seen = Unix-Zeit in Millisekunden des letzten Signals (online ODER offline).
CREATE TABLE IF NOT EXISTS presence (
  world TEXT NOT NULL,
  name_lower TEXT NOT NULL,
  data_center TEXT NOT NULL,
  online INTEGER NOT NULL,
  last_seen INTEGER NOT NULL,
  PRIMARY KEY (world, name_lower)
);

-- Browse holt alle Zeilen eines Data Centers mit einer Abfrage; der Cron löscht nach last_seen.
CREATE INDEX IF NOT EXISTS presence_data_center ON presence (data_center);
CREATE INDEX IF NOT EXISTS presence_last_seen ON presence (last_seen);
