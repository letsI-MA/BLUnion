#!/usr/bin/env python3
"""
BLUnion Moderations-Skript (siehe README.md im selben Ordner).

Spricht die /admin/*-Endpunkte des Live-Sync-Workers an (siehe handleAdminRequest in
worker/src/index.ts), um Fake-Profile und -Gruppen zu entfernen:

    python blunion_admin.py show-profile <World> "<Vorname Nachname>"
    python blunion_admin.py delete-profile <World> "<Vorname Nachname>"
    python blunion_admin.py delete-group <groupId>

WORKER_URL und ADMIN_TOKEN kommen aus tools/admin/.env (per .gitignore ausgeschlossen, Vorlage:
.env.example). Nutzt AUSSCHLIESSLICH die Python-Standardbibliothek - wie tools/kv-mirror kein
"pip install" nötig.

Der Worker antwortet bei falschem/fehlendem Token (oder nicht gesetztem ADMIN_TOKEN-Secret)
bewusst mit demselben 404 wie für einen unbekannten Pfad - das Skript kann "nicht gefunden" und
"nicht berechtigt" deshalb nur über den Antwort-Body unterscheiden (siehe describe_not_found).
"""

from __future__ import annotations

import json
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any

ENV_FILE = Path(__file__).resolve().parent / ".env"
REQUIRED_KEYS = ("WORKER_URL", "ADMIN_TOKEN")
REQUEST_TIMEOUT_SECONDS = 20

USAGE = """Verwendung:
  python blunion_admin.py show-profile <World> "<Vorname Nachname>"
  python blunion_admin.py delete-profile <World> "<Vorname Nachname>"
  python blunion_admin.py delete-group <groupId>"""


class ConfigError(Exception):
    pass


def load_config() -> dict[str, str]:
    """Simpler KEY=VALUE-Parser für tools/admin/.env (wie load_api_token in tools/kv-mirror)."""
    if not ENV_FILE.exists():
        raise ConfigError(
            f'Keine .env-Datei gefunden ("{ENV_FILE}"). .env.example nach .env kopieren und '
            "WORKER_URL/ADMIN_TOKEN eintragen (siehe README.md)."
        )

    config: dict[str, str] = {}
    for raw_line in ENV_FILE.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        key, sep, value = line.partition("=")
        if sep:
            config[key.strip()] = value.strip().strip('"').strip("'")

    missing = [key for key in REQUIRED_KEYS if not config.get(key)]
    if missing:
        raise ConfigError(f'"{ENV_FILE}" enthält keinen Wert für: {", ".join(missing)}.')

    config["WORKER_URL"] = config["WORKER_URL"].rstrip("/")
    return config


def admin_request(config: dict[str, str], method: str, path: str) -> tuple[int, Any]:
    """Liefert (HTTP-Status, geparster JSON-Body oder None) - wirft nicht bei 4xx/5xx."""
    request = urllib.request.Request(
        config["WORKER_URL"] + path,
        method=method,
        headers={
            "Authorization": f"Bearer {config['ADMIN_TOKEN']}",
            "User-Agent": "blunion-admin/1.0",
        },
    )

    try:
        with urllib.request.urlopen(request, timeout=REQUEST_TIMEOUT_SECONDS) as response:
            status, raw = response.status, response.read()
    except urllib.error.HTTPError as error:
        status, raw = error.code, error.read()

    try:
        body = json.loads(raw.decode("utf-8")) if raw else None
    except (UnicodeDecodeError, json.JSONDecodeError):
        body = None

    return status, body


def profile_path(world: str, character_name: str) -> str:
    return (
        "/admin/profile/"
        + urllib.parse.quote(world, safe="")
        + "/"
        + urllib.parse.quote(character_name, safe="")
    )


def describe_not_found(body: Any) -> str:
    # Autorisierte 404-Antworten enthalten "key" (siehe handleAdminRequest); der generische 404
    # für Unberechtigte nicht.
    if isinstance(body, dict) and "key" in body:
        return f'Nicht gefunden (KV-Key "{body["key"]}").'
    return (
        "404 ohne Admin-Antwort - entweder ist ADMIN_TOKEN falsch, das Secret im Worker nicht "
        "gesetzt, WORKER_URL falsch oder das Rate-Limit greift (kurz warten)."
    )


def confirm(question: str) -> bool:
    answer = input(f"{question} wirklich löschen? (j/n) ").strip().lower()
    return answer in ("j", "ja", "y", "yes")


def print_json(body: Any) -> None:
    print(json.dumps(body, indent=2, ensure_ascii=False))


def show_profile(config: dict[str, str], world: str, character_name: str) -> int:
    status, body = admin_request(config, "GET", profile_path(world, character_name))
    if status == 200:
        print_json(body)
        return 0
    if status == 404:
        print(describe_not_found(body), file=sys.stderr)
        return 1
    print(f"Fehler: HTTP {status} {body}", file=sys.stderr)
    return 1


def delete_profile(config: dict[str, str], world: str, character_name: str) -> int:
    # Vorher anzeigen, damit man sieht, was man löscht (und ob es überhaupt existiert).
    if show_profile(config, world, character_name) != 0:
        return 1

    if not confirm(f'Profil "{character_name}" ({world})'):
        print("Abgebrochen.")
        return 0

    status, body = admin_request(config, "DELETE", profile_path(world, character_name))
    if status == 200:
        print("Gelöscht (inkl. Discord-Karte, falls vorhanden).")
        return 0
    print(describe_not_found(body) if status == 404 else f"Fehler: HTTP {status} {body}", file=sys.stderr)
    return 1


def delete_group(config: dict[str, str], group_id: str) -> int:
    if not confirm(f'Gruppe "{group_id}"'):
        print("Abgebrochen.")
        return 0

    status, body = admin_request(config, "DELETE", "/admin/group/" + urllib.parse.quote(group_id, safe=""))
    if status == 200:
        print("Gelöscht (inkl. Discord-Karte, falls vorhanden).")
        return 0
    print(describe_not_found(body) if status == 404 else f"Fehler: HTTP {status} {body}", file=sys.stderr)
    return 1


def main(argv: list[str]) -> int:
    commands = {
        "show-profile": (2, lambda config, args: show_profile(config, args[0], args[1])),
        "delete-profile": (2, lambda config, args: delete_profile(config, args[0], args[1])),
        "delete-group": (1, lambda config, args: delete_group(config, args[0])),
    }

    if len(argv) < 1 or argv[0] not in commands or len(argv) - 1 != commands[argv[0]][0]:
        print(USAGE, file=sys.stderr)
        return 2

    try:
        config = load_config()
    except ConfigError as error:
        print(f"Konfigurationsfehler: {error}", file=sys.stderr)
        return 2

    try:
        return commands[argv[0]][1](config, argv[1:])
    except urllib.error.URLError as error:
        print(f"Netzwerkfehler: {error.reason}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
