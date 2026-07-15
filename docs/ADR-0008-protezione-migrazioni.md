# ADR-0008 — Protezione preventiva delle migrazioni SQLite

## Stato

Accettata in M3.4.

## Contesto

Le migrazioni di WeeklyPlanner sono applicate automaticamente all'avvio e ogni versione viene
confermata in una transazione distinta. Questo permette di individuare con precisione la versione
raggiunta, ma una migrazione successiva fallita potrebbe lasciare il database a una versione
intermedia. Prima della futura migrazione v4, che introdurrà cataloghi e storico, è necessario
proteggere il file locale esistente.

Una semplice copia del file mentre SQLite è aperto non è una strategia sufficiente. Il backup deve
essere prodotto tramite l'API SQLite della connessione e deve essere verificato prima di iniziare
l'upgrade.

## Decisione

Per ogni database già esistente con schema precedente a quello atteso:

1. vengono selezionate e validate tutte le migrazioni mancanti;
2. vengono eseguiti `PRAGMA integrity_check` e `PRAGMA foreign_key_check`;
3. viene creato un backup coerente tramite `SqliteConnection.BackupDatabase`;
4. il backup viene nuovamente sottoposto ai controlli di integrità;
5. le migrazioni vengono applicate in ordine, una transazione per versione;
6. il database aggiornato viene verificato;
7. in caso di errore, la connessione viene chiusa e il backup sostituisce il file migrato;
8. il file ripristinato viene verificato e deve riportare la versione schema originaria.

Il backup resta disponibile anche dopo un rollback riuscito. Un errore di migrazione viene quindi
segnalato all'utente, ma il database precedente rimane utilizzabile da una versione compatibile
dell'applicazione.

I backup sono salvati nella cartella locale:

```text
%LOCALAPPDATA%\WeeklyPlanner\Backups\Migrations
```

Vengono mantenuti gli ultimi cinque file. La retention è best-effort: un problema durante la pulizia
dei backup più vecchi non invalida una migrazione già conclusa con successo.

Per un database creato nello stesso avvio non esiste ancora uno stato utente da preservare. Se la
creazione fallisce, il file parziale e gli eventuali sidecar SQLite vengono rimossi, permettendo un
nuovo tentativo pulito.

## Conseguenze

### Positive

- nessun upgrade fallito può lasciare silenziosamente un database esistente a una versione
  intermedia;
- il backup viene creato da SQLite e non tramite copia non coordinata del file;
- corruzione e foreign key non valide bloccano la migrazione prima della prima modifica;
- il rollback è verificato sia per integrità sia per versione schema;
- la futura migrazione v4 può essere sviluppata sopra una procedura di recupero già testata.

### Costi e limiti

- l'avvio che applica una migrazione richiede spazio aggiuntivo pari circa alla dimensione del
  database;
- `integrity_check` può richiedere tempo su database molto grandi;
- se falliscono sia la migrazione sia il ripristino, WeeklyPlanner segnala un errore di recovery e
  conserva il backup preventivo per un intervento manuale;
- M3.4 protegge gli upgrade automatici, ma non sostituisce ancora il futuro backup/restore manuale
  previsto per l'MVP.
