#!/usr/bin/env python3
"""
BLUnion Live-Sync KV-Mirror (reines Dev-/Debug-Tool, siehe README.md im selben Ordner).

Spiegelt alle Profile aus dem Cloudflare-KV-Namespace BLUNION_PROFILES (Backend des
Live-Sync-Features, siehe worker/src/index.ts) in eine lokale SQLite-Datei
(blunion_profiles.db), damit man sie z.B. mit "DB Browser for SQLite" bequem
durchsuchen/filtern/sortieren kann, statt einzeln über die Cloudflare-API oder das
Dashboard nachzuschauen.

KEIN Teil des eigentlichen Plugins/Workers/Website - analog zu Services/DevTestFixtures.cs
im .NET-Projekt bewusst dauerhaft im Repo belassen, klar als Dev-Only markiert. Greift nur
LESEND auf die Cloudflare-KV-REST-API zu (List Keys + Bulk Get) - verändert oder löscht
nichts in Cloudflare KV selbst.

Nutzt AUSSCHLIESSLICH die Python-Standardbibliothek (urllib/json/sqlite3/...) - kein
"pip install" nötig, siehe README.md ("Technik"-Abschnitt zur Begründung).

Ablauf:
  1. list_all_keys(): GET .../storage/kv/namespaces/{id}/keys, paginiert über den
     "cursor"-Parameter, bis keine weitere Seite mehr kommt (KV liefert maximal 1000 Keys
     pro Aufruf - bei den erwarteten tausenden Profilen reicht eine einzelne Seite nicht).
  2. bulk_get_values(): dieselben Keys in 100er-Gruppen per Bulk-Get-Endpoint abgerufen
     (POST .../bulk/get) - NICHT pro Key ein einzelner GET-Aufruf, das wäre bei tausenden
     Profilen unnötig langsam und würde unnötig viele Requests erzeugen.
  3. parse_profile(): jeder Rohwert als JSON geparst, nur die in PROFILE_FIELDS gelisteten
     Felder übernommen (Allowlist). Nicht parsebare/leere Werte liefern None statt eine
     Exception zu werfen - werden vom Aufrufer als "übersprungen" gezählt, brechen das
     Skript aber nicht ab.
  4. upsert_profile(): INSERT OR REPLACE in die Tabelle "profiles" der lokalen SQLite-Datei
     (Primary Key kv_key) - ein wiederholter Lauf aktualisiert bestehende Zeilen statt
     Duplikate anzulegen.
  5. remove_stale_rows(): Zeilen, deren kv_key beim AKTUELLEN Lauf nicht mehr unter den
     KV-Keys war (abgelaufen/gelöscht), werden aus der lokalen DB entfernt - der Spiegel
     soll den echten Cloudflare-Stand widerspiegeln, nicht nur wachsen.
"""

from __future__ import annotations

import json
import sqlite3
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any

# --- Konfiguration ---------------------------------------------------------------------

# Unkritisch (siehe README.md/Aufgabenstellung) - Account-/Namespace-Id sind keine Geheimnisse,
# anders als der API-Token, der NIE hier im Code landet (siehe load_api_token()).
ACCOUNT_ID = "9696b2bdb49fc07c674a5fe367d83cd2"
NAMESPACE_ID = "f0830d1c36fc475ba0dca0093dc6a528"

CLOUDFLARE_API_BASE = "https://api.cloudflare.com/client/v4"

# KV-Key-Schema laut worker/src/index.ts kvKey(): "profile:<world>:<characterName>" - der
# Namespace könnte theoretisch künftig auch andere Key-Präfixe enthalten, dieses Tool
# interessiert sich nur für Live-Sync-Profile.
KEY_PREFIX = "profile:"

# Von Cloudflare dokumentiertes Maximum an Keys pro List-Keys-Aufruf.
LIST_PAGE_LIMIT = 1000

# Gruppengröße für den Bulk-Get-Endpoint (siehe Aufgabenstellung: Gruppen von jeweils 100 -
# das ist zugleich das von Cloudflare dokumentierte Maximum pro Bulk-Get-Aufruf).
BULK_GET_BATCH_SIZE = 100

SCRIPT_DIR = Path(__file__).resolve().parent
ENV_FILE = SCRIPT_DIR / ".env"
DB_FILE = SCRIPT_DIR / "blunion_profiles.db"

# Feldliste 1:1 aus worker/src/index.ts (StoredProfile-Interface) übernommen - bewusst NICHT
# neu geraten. availabilityTags/note/wantedPlayerCount sind dort optional (Phase-1-Profile
# kennen sie noch nicht, siehe dortiger Kommentar) - profile.get(...) liefert für sie in dem
# Fall einfach None, siehe upsert_profile().
PROFILE_FIELDS = (
    "characterName",
    "world",
    "dataCenter",
    "spellBitmaskBase64",
    "editTokenHash",
    "visibility",
    "availabilityTags",
    "note",
    "wantedPlayerCount",
    "createdAt",
    "updatedAt",
)


class ConfigError(RuntimeError):
    """Für erwartbare Konfigurationsfehler (fehlender/leerer API-Token) - main() fängt das mit
    einer klaren Meldung ab statt einen rohen Stacktrace zu zeigen."""


def load_api_token() -> str:
    """Liest CLOUDFLARE_API_TOKEN aus tools/kv-mirror/.env (siehe README.md, Format
    "CLOUDFLARE_API_TOKEN=xxx", eine Zeile - der Token selbst landet NIE im Code/Repo, siehe
    .gitignore im selben Ordner). Bewusst ein simpler Parser statt einer .env-Bibliothek
    (python-dotenv o.ä.) - genau EIN erwarteter Schlüssel, das lohnt keine zusätzliche
    Abhängigkeit (siehe Aufgabenstellung: nur Standardbibliothek)."""
    if not ENV_FILE.exists():
        raise ConfigError(
            f'Keine .env-Datei gefunden ("{ENV_FILE}"). Siehe README.md im selben Ordner, '
            'Abschnitt "Einrichtung", für die Erstellung eines Cloudflare-API-Tokens und das '
            "erwartete .env-Format."
        )

    token: str | None = None
    for raw_line in ENV_FILE.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        key, sep, value = line.partition("=")
        if sep and key.strip() == "CLOUDFLARE_API_TOKEN":
            token = value.strip().strip('"').strip("'")
            break

    if not token:
        raise ConfigError(
            f'"{ENV_FILE}" enthält keinen (gültigen) CLOUDFLARE_API_TOKEN-Eintrag. Siehe '
            "README.md im selben Ordner für das erwartete Format."
        )

    return token


def cloudflare_request(
    path_and_query: str, token: str, *, method: str = "GET", body: dict[str, Any] | None = None
) -> dict[str, Any]:
    """Ein einzelner Cloudflare-API-Aufruf über urllib (Standardbibliothek, siehe Moduldoc) -
    zentrale Stelle für Auth-Header/JSON-Encoding/Fehlerbehandlung, von list_all_keys() und
    bulk_get_values() genutzt statt zweimal eigenständig implementiert."""
    url = f"{CLOUDFLARE_API_BASE}{path_and_query}"
    data = json.dumps(body).encode("utf-8") if body is not None else None

    request = urllib.request.Request(url, data=data, method=method)
    request.add_header("Authorization", f"Bearer {token}")
    request.add_header("Content-Type", "application/json")

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as ex:
        detail = ex.read().decode("utf-8", errors="replace")
        raise RuntimeError(
            f"Cloudflare-API-Aufruf fehlgeschlagen ({method} {path_and_query}): HTTP {ex.code} - {detail}"
        ) from ex
    except urllib.error.URLError as ex:
        raise RuntimeError(f"Cloudflare-API-Aufruf fehlgeschlagen ({method} {path_and_query}): {ex.reason}") from ex

    if not payload.get("success", False):
        raise RuntimeError(f"Cloudflare-API meldet Fehler ({method} {path_and_query}): {payload.get('errors')}")

    return payload


def list_all_keys(token: str) -> list[str]:
    """Listet ALLE Keys mit Prefix "profile:" im Namespace auf - paginiert über den
    "cursor"-Parameter, bis keine weitere Seite mehr kommt (siehe Aufgabenstellung: NICHT
    annehmen, dass alles in einer Seite passt - bei tausenden Profilen bricht das)."""
    keys: list[str] = []
    cursor: str | None = None

    while True:
        params: dict[str, str] = {"limit": str(LIST_PAGE_LIMIT), "prefix": KEY_PREFIX}
        if cursor:
            params["cursor"] = cursor

        payload = cloudflare_request(
            f"/accounts/{ACCOUNT_ID}/storage/kv/namespaces/{NAMESPACE_ID}/keys?{urllib.parse.urlencode(params)}",
            token,
        )

        keys.extend(entry["name"] for entry in payload.get("result", []))

        # Cloudflare liefert bei der letzten Seite entweder gar kein "cursor"-Feld oder einen
        # leeren String - beides beendet die Schleife hier gleichermaßen.
        cursor = (payload.get("result_info") or {}).get("cursor") or None
        if not cursor:
            break

    return keys


def bulk_get_values(token: str, keys: list[str]) -> dict[str, str | None]:
    """Liest die Werte zu ALLEN übergebenen Keys über den Bulk-Get-Endpoint, in Gruppen von je
    BULK_GET_BATCH_SIZE (siehe Aufgabenstellung: NICHT pro Key ein einzelner GET-Aufruf).
    Liefert ein Dict key -> Rohwert (String, noch NICHT als JSON geparst - siehe
    parse_profile()); fehlt ein Key in der Antwort (z.B. zwischen List und Bulk-Get gelöscht),
    fehlt er hier einfach im Ergebnis-Dict statt einer Exception."""
    values: dict[str, str | None] = {}

    for offset in range(0, len(keys), BULK_GET_BATCH_SIZE):
        batch = keys[offset : offset + BULK_GET_BATCH_SIZE]
        payload = cloudflare_request(
            f"/accounts/{ACCOUNT_ID}/storage/kv/namespaces/{NAMESPACE_ID}/bulk/get",
            token,
            method="POST",
            body={"keys": batch},
        )
        values.update(payload.get("result", {}).get("values", {}))

    return values


def parse_profile(raw_value: str | None) -> dict[str, Any] | None:
    """Parst einen rohen KV-Wert als JSON und liefert nur die in PROFILE_FIELDS gelisteten
    Felder zurück (Allowlist - ergänzt der Worker künftig ein internes Feld, landet das nicht
    automatisch ungeprüft in der lokalen DB). Liefert None bei leerem/nicht-parsebarem Wert
    oder wenn das JSON kein Objekt ist, statt eine Exception zu werfen - main() zählt das als
    übersprungen (siehe Aufgabenstellung: einzelne kaputte Einträge sollen den Lauf nicht
    abbrechen)."""
    if not raw_value:
        return None
    try:
        parsed = json.loads(raw_value)
    except json.JSONDecodeError:
        return None
    if not isinstance(parsed, dict):
        return None
    return {field: parsed.get(field) for field in PROFILE_FIELDS}


def ensure_schema(connection: sqlite3.Connection) -> None:
    """Legt die Tabelle "profiles" an, falls sie noch nicht existiert - eine Spalte pro
    StoredProfile-Feld (siehe PROFILE_FIELDS) plus kv_key (Primary Key) und mirroredAt
    (Zeitpunkt DIESES Spiegel-Laufs, nicht Teil von StoredProfile - praktisch in DB Browser,
    um zu sehen, wie aktuell die lokale Kopie eines Eintrags ist)."""
    connection.execute(
        """
        CREATE TABLE IF NOT EXISTS profiles (
            kv_key TEXT PRIMARY KEY,
            characterName TEXT,
            world TEXT,
            dataCenter TEXT,
            spellBitmaskBase64 TEXT,
            editTokenHash TEXT,
            visibility TEXT,
            availabilityTags TEXT,
            note TEXT,
            wantedPlayerCount INTEGER,
            createdAt TEXT,
            updatedAt TEXT,
            mirroredAt TEXT
        )
        """
    )


def upsert_profile(connection: sqlite3.Connection, kv_key: str, profile: dict[str, Any], mirrored_at: str) -> None:
    # Als JSON-Array-String gespeichert (siehe Aufgabenstellung: "deine Wahl") statt simplem
    # Komma-Join - kurze Arrays wie ["evening","weekend"] bleiben in DB Browser gut lesbar,
    # ohne dass ein (aktuell nicht vorkommender, aber theoretisch möglicher) Tag mit Komma im
    # Namen mit dem Trennzeichen kollidieren könnte.
    availability_tags = profile.get("availabilityTags")
    availability_tags_json = json.dumps(availability_tags) if availability_tags is not None else None

    connection.execute(
        """
        INSERT OR REPLACE INTO profiles (
            kv_key, characterName, world, dataCenter, spellBitmaskBase64, editTokenHash,
            visibility, availabilityTags, note, wantedPlayerCount, createdAt, updatedAt, mirroredAt
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            kv_key,
            profile.get("characterName"),
            profile.get("world"),
            profile.get("dataCenter"),
            profile.get("spellBitmaskBase64"),
            profile.get("editTokenHash"),
            profile.get("visibility"),
            availability_tags_json,
            profile.get("note"),
            profile.get("wantedPlayerCount"),
            profile.get("createdAt"),
            profile.get("updatedAt"),
            mirrored_at,
        ),
    )


def remove_stale_rows(connection: sqlite3.Connection, current_keys: set[str]) -> int:
    """Entfernt Zeilen, deren kv_key beim AKTUELLEN Lauf nicht mehr unter den KV-Keys war
    (abgelaufen/gelöscht) - siehe Aufgabenstellung: der Spiegel soll den echten Cloudflare-
    Stand widerspiegeln, nicht nur wachsen. Liefert die Anzahl entfernter Zeilen für die
    Konsolen-Zusammenfassung."""
    existing_keys = {row[0] for row in connection.execute("SELECT kv_key FROM profiles")}
    stale_keys = existing_keys - current_keys
    if stale_keys:
        connection.executemany("DELETE FROM profiles WHERE kv_key = ?", [(key,) for key in stale_keys])
    return len(stale_keys)


def main() -> int:
    start_time = time.monotonic()

    try:
        token = load_api_token()
    except ConfigError as ex:
        print(f"Fehler: {ex}", file=sys.stderr)
        return 1

    print(f'Liste Keys mit Prefix "{KEY_PREFIX}" im Namespace {NAMESPACE_ID}...')
    try:
        keys = list_all_keys(token)
    except RuntimeError as ex:
        print(f"Fehler beim Auflisten der Keys: {ex}", file=sys.stderr)
        return 1

    print(f"{len(keys)} Keys gefunden. Lade Werte per Bulk-Get (Gruppen à {BULK_GET_BATCH_SIZE})...")
    try:
        raw_values = bulk_get_values(token, keys)
    except RuntimeError as ex:
        print(f"Fehler beim Abrufen der Werte: {ex}", file=sys.stderr)
        return 1

    connection = sqlite3.connect(DB_FILE)
    try:
        ensure_schema(connection)

        mirrored_at = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
        mirrored_count = 0
        skipped_count = 0
        current_keys: set[str] = set()

        for key in keys:
            current_keys.add(key)
            profile = parse_profile(raw_values.get(key))
            if profile is None:
                skipped_count += 1
                continue
            upsert_profile(connection, key, profile, mirrored_at)
            mirrored_count += 1

        removed_count = remove_stale_rows(connection, current_keys)
        connection.commit()
    finally:
        connection.close()

    elapsed_seconds = time.monotonic() - start_time

    print()
    print("--- Zusammenfassung ---")
    print(f"Gefundene Keys:              {len(keys)}")
    print(f"Erfolgreich gespiegelt:      {mirrored_count}")
    print(f"Übersprungen (ungültig):     {skipped_count}")
    print(f"Entfernt (nicht mehr in KV): {removed_count}")
    print(f"Laufzeit:                    {elapsed_seconds:.1f}s")
    print(f"Datenbank:                   {DB_FILE}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
