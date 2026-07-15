WeeklyPlanner — Distribuzione Windows M5.1
=======================================

AVVIO
-----
Eseguire WeeklyPlanner.App.exe. La finestra principale viene aperta massimizzata.
Al primo avvio l'app richiede il percorso locale del database SQLite e il nome utente.

PACCHETTI
---------
- portable: richiede il runtime .NET 10 x64 già installato sulla macchina;
- self-contained: include il runtime .NET necessario e non richiede un'installazione .NET separata.

Entrambi i pacchetti sono distribuzioni a cartella: estrarre tutto lo ZIP prima dell'avvio.
Non eseguire l'app direttamente dall'anteprima dell'archivio.

DATI LOCALI
-----------
Il pacchetto non contiene database, impostazioni o log dell'utente.
Percorsi predefiniti:
- database: %LOCALAPPDATA%\WeeklyPlanner\Data\weeklyplanner.db
- impostazioni: %APPDATA%\WeeklyPlanner\settings.json
- log: %LOCALAPPDATA%\WeeklyPlanner\Logs
- backup manuali e preventivi: %LOCALAPPDATA%\WeeklyPlanner\Backups\Manual
- backup automatici delle migrazioni: %LOCALAPPDATA%\WeeklyPlanner\Backups\Migrations

BACKUP E VERIFICA
-----------------
Consultare la cartella Documentazione:
- MANUALE-UTENTE.md
- BACKUP-RIPRISTINO.md
- SMOKE-TEST-M5.1.md
- RELEASE-NOTES-M5.1.md
- RELEASE-CHECKLIST-M5.1.md
