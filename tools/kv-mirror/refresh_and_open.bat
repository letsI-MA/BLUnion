@echo off
REM Refresh blunion_profiles.db from Cloudflare KV, then open it in DB Browser for SQLite.
REM Bei Fehler im Mirror-Skript wird DB Browser NICHT geoeffnet, Fenster bleibt offen (pause).

setlocal
cd /d "%~dp0"

echo === Aktualisiere blunion_profiles.db aus Cloudflare KV ===
python mirror_kv_to_sqlite.py
if errorlevel 1 (
    echo.
    echo === Mirror fehlgeschlagen - DB Browser wird NICHT geoeffnet. ===
    pause
    exit /b 1
)

set "DBBROWSER=C:\Program Files\DB Browser for SQLite\DB Browser for SQLite.exe"
if not exist "%DBBROWSER%" (
    echo.
    echo DB Browser for SQLite nicht gefunden unter:
    echo   %DBBROWSER%
    echo blunion_profiles.db wurde trotzdem aktualisiert.
    pause
    exit /b 1
)

start "" "%DBBROWSER%" "%~dp0blunion_profiles.db"
endlocal
