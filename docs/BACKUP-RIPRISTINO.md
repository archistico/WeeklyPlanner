# WeeklyPlanner — Backup e ripristino manuale

## 1. Ambito

Questa procedura protegge il database SQLite locale e, facoltativamente, le impostazioni della
postazione. Deve essere eseguita con **tutte le istanze di WeeklyPlanner chiuse**.

Il percorso del database è visibile in **Diagnostica** e nelle **Impostazioni**. Il percorso
predefinito è:

```text
%LOCALAPPDATA%\WeeklyPlanner\Data\weeklyplanner.db
```

Le impostazioni locali sono normalmente in:

```text
%APPDATA%\WeeklyPlanner\settings.json
```

## 2. Perché occorre chiudere l'app

SQLite può usare file laterali durante le transazioni:

```text
weeklyplanner.db-wal
weeklyplanner.db-shm
weeklyplanner.db-journal
```

Copiare il solo file `.db` mentre un'istanza è attiva può produrre un backup incompleto. Con tutte
le istanze chiuse, copiare il database e gli eventuali file laterali presenti come un unico insieme.

## 3. Creazione del backup

1. Chiudere tutte le finestre di WeeklyPlanner.
2. In Gestione attività verificare che `WeeklyPlanner.App.exe` non sia più in esecuzione.
3. Aprire la cartella che contiene il database.
4. Creare una cartella di backup con data e ora, ad esempio:

   ```text
   WeeklyPlanner-backup-2026-07-15-2230
   ```

5. Copiare nella cartella:
   - `weeklyplanner.db`;
   - `weeklyplanner.db-wal`, se ancora presente;
   - `weeklyplanner.db-journal`, se ancora presente;
   - facoltativamente `weeklyplanner.db-shm`, utile solo per conservare l'insieme originale ma non
     necessario al ripristino perché SQLite lo rigenera.
6. Copiare facoltativamente `%APPDATA%\WeeklyPlanner\settings.json` per conservare utente, tema,
   polling e percorso configurato.
7. Conservare il backup su un supporto diverso dal disco che contiene il database operativo.

I backup creati automaticamente prima delle migrazioni si trovano in:

```text
%LOCALAPPDATA%\WeeklyPlanner\Backups\Migrations
```

Questi backup proteggono gli aggiornamenti dello schema, ma non sostituiscono una politica di backup
periodica dei dati correnti.

## 4. Verifica del backup

- controllare che il file `.db` abbia dimensione maggiore di zero;
- verificare che la data dei file corrisponda al momento del backup;
- mantenere insieme database e file laterali eventualmente copiati;
- non rinominare singolarmente i file laterali.

Per una verifica completa, ripristinare una copia in un percorso temporaneo e aprirla da una
postazione di test.

## 5. Ripristino

1. Chiudere tutte le istanze di WeeklyPlanner.
2. Annotare il percorso del database attivo.
3. Creare una copia di sicurezza della situazione corrente prima di sovrascrivere qualsiasi file.
4. Eliminare dal percorso di destinazione gli eventuali file laterali correnti:

   ```text
   weeklyplanner.db-wal
   weeklyplanner.db-shm
   weeklyplanner.db-journal
   ```

5. Copiare dal backup il file `.db` nel percorso configurato.
6. Copiare anche gli eventuali file `-wal` e `-journal` appartenenti allo stesso backup. Non
   ripristinare il file `-shm`: SQLite lo ricrea automaticamente.
7. Ripristinare `settings.json` soltanto quando si desidera recuperare anche le preferenze locali.
8. Avviare WeeklyPlanner.
9. Aprire **Diagnostica** e verificare:
   - percorso del database;
   - versione dello schema;
   - stato operativo;
   - assenza di errori di integrità o migrazione.
10. Controllare alcune card, fasce, priorità e voci di cronologia.

## 6. Ripristino su un percorso differente

È preferibile copiare il backup nel nuovo percorso e selezionare quel file dalle Impostazioni o
nell'onboarding. Non modificare manualmente il contenuto binario del database.

I percorsi UNC di rete e i percorsi relativi non sono supportati. Il database deve restare su un
disco locale.

## 7. Problemi durante il ripristino

Non continuare a usare il database quando l'app segnala un errore di integrità. Conservare:

- backup originale;
- copia del database che non si apre;
- riferimento errore `WP-XXXXXX`;
- log presenti in `%LOCALAPPDATA%\WeeklyPlanner\Logs`.
