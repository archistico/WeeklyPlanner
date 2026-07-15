# ADR-0021 — Backup, ripristino e integrità dalla UI

- **Stato:** Accettata
- **Data:** 15 luglio 2026
- **Milestone:** M5.1
- **Schema SQLite:** v5 invariato

## Contesto

M4 documentava backup e ripristino come procedure manuali. Copiare direttamente un database SQLite
mentre WeeklyPlanner o un'altra istanza lo stanno usando può però produrre una copia incoerente o
lasciare file laterali non allineati.

Il ripristino non deve sostituire il file operativo mentre polling, heartbeat o repository possono
ancora aprire connessioni.

## Decisione

### Backup

I backup manuali vengono creati tramite l'API SQLite Online Backup:

- la board può restare aperta;
- il backup è un file `.db` autonomo;
- il file viene verificato con `PRAGMA integrity_check` e `pragma_foreign_key_check`;
- vengono registrati schema, dimensione, data e tipo di backup;
- i backup sono conservati in `%LOCALAPPDATA%\WeeklyPlanner\Backups\Manual`.

### Ripristino

Il restore è differito al riavvio:

1. la UI verifica il backup selezionato;
2. controlla che non esistano altre istanze attive sullo stesso database;
3. scrive atomicamente una richiesta di restore;
4. avvia una nuova istanza e chiude coordinatamente la board corrente;
5. la nuova istanza acquisisce un lock esclusivo del restore e attende la chiusura di tutte le istanze;
6. crea il backup preventivo del database corrente immediatamente prima della sostituzione;
7. applica il restore prima di creare `BoardViewModel` o aprire repository;
8. elimina i sidecar `-wal`, `-shm` e `-journal`;
9. verifica nuovamente integrità e compatibilità;
10. se la verifica finale fallisce, ripristina atomicamente il database precedente.

Un backup con schema superiore a quello supportato viene mostrato come incompatibile. Gli schemi
precedenti supportati possono essere ripristinati e vengono migrati dal normale initializer al
successivo avvio della board.

### Coordinamento delle istanze

Ogni board registra un marker locale associato a:

- percorso normalizzato del database;
- sessione applicativa;
- processo;
- data di registrazione.

Il marker viene rimosso durante lo shutdown coordinato. I marker di processi non più attivi vengono
ripuliti automaticamente. Il restore viene rifiutato quando un'altra sessione viva usa lo stesso
database. Un lock di filesystem impedisce inoltre a due nuovi processi di elaborare contemporaneamente
la stessa richiesta; un processo bloccato non apre la board.

## Conseguenze

- nessuna migrazione funzionale;
- backup e restore sono accessibili senza strumenti esterni;
- il restore richiede un riavvio controllato;
- i backup preventivi non vengono eliminati automaticamente;
- i backup pianificati, cloud e cifrati restano fuori da M5.1.
