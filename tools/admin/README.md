# BLUnion Moderation (Admin-Skript)

Kleines Kommandozeilen-Werkzeug, um Fake-Profile und -Gruppen aus dem Live-Sync-Backend zu
entfernen. Es spricht die `/admin/*`-Endpunkte des Workers an (siehe `handleAdminRequest` in
`worker/src/index.ts`). Python 3, **nur Standardbibliothek** - kein `pip install` nötig.

## Einrichtung

1. Ein langes Zufalls-Token erzeugen, z.B.:

   ```
   python -c "import secrets; print(secrets.token_urlsafe(48))"
   ```

2. Im Worker als Secret setzen (im Ordner `worker/`):

   ```
   npx wrangler secret put ADMIN_TOKEN
   ```

3. Hier `.env.example` nach `.env` kopieren und `WORKER_URL` sowie dasselbe `ADMIN_TOKEN`
   eintragen. `.env` ist per `.gitignore` ausgeschlossen.

## Verwendung

```
python blunion_admin.py show-profile <World> "<Vorname Nachname>"
python blunion_admin.py delete-profile <World> "<Vorname Nachname>"
python blunion_admin.py delete-group <groupId>
```

- `show-profile` zeigt den gespeicherten Eintrag inkl. `createdAt`/`updatedAt` (ohne
  `editTokenHash`).
- `delete-profile` zeigt den Eintrag zuerst an und fragt dann `wirklich löschen? (j/n)`.
- `delete-group` fragt ebenfalls vorher nach. Die `groupId` steht z.B. in der Browse-Antwort
  (`GET /groups/browse?dataCenter=...`) oder im KV-Mirror (`tools/kv-mirror`).

Gelöscht wird mit derselben Logik wie bei einem normalen Löschen durch den Besitzer - also
inklusive der zugehörigen Discord-Karte. Profilnamen werden dabei nicht auf die FFXIV-Namensregeln
geprüft, damit sich auch Altlasten mit ungültigen Namen entfernen lassen.

## Hinweise

- Ohne gültiges Token (oder ohne gesetztes Secret) antwortet der Worker mit demselben 404 wie
  für einen unbekannten Pfad - die Admin-Endpunkte sollen von außen nicht erkennbar sein. Das
  Skript meldet in dem Fall einen Hinweis auf Token/URL.
- Die Endpunkte teilen sich das Rate-Limit der schreibenden Endpunkte (20 Anfragen/Minute pro IP);
  auch ein Limit-Treffer sieht wie ein 404 aus.
- Die Endpunkte sind absichtlich nicht per CORS freigegeben - nur per Skript nutzbar, nicht aus
  dem Browser.
