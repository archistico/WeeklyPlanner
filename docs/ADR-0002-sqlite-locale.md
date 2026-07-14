# ADR-0002 — SQLite locale senza server

- **Stato:** accettata
- **Data:** 14 luglio 2026
- **Decision owner:** progetto WeeklyPlanner

## Contesto

Il prototipo iniziale era orientato a una board condivisa fra più computer tramite un unico file
SQLite su cartella di rete. Questa modalità dipende dal locking del filesystem remoto e non può
essere considerata una base affidabile.

Per la fase corrente si vuole mantenere l'applicazione semplice, senza introdurre un processo server
o un database client/server.

## Decisione

WeeklyPlanner usa un singolo file **SQLite locale alla macchina** che esegue l'applicazione.

Il percorso predefinito è:

```text
%LOCALAPPDATA%\WeeklyPlanner\Data\weeklyplanner.db
```

Il percorso resta configurabile, ma il file non deve essere collocato su share UNC, unità di rete o
cartelle sincronizzate usate contemporaneamente da più computer.

Il polling viene mantenuto per:

- rilevare modifiche effettuate da una seconda istanza locale;
- aggiornare la UI dopo modifiche esterne al processo;
- preservare un confine applicativo utile a un'eventuale futura sincronizzazione.

## Conseguenze

### Positive

- nessun servizio da installare o amministrare;
- deployment desktop semplice;
- locking SQLite gestito dal filesystem locale;
- migrazioni e backup basati su un singolo file;
- architettura compatibile con uso personale immediato.

### Negative

- nessuna sincronizzazione fra computer;
- nessuna collaborazione in tempo reale fra utenti su macchine differenti;
- il nome utente è metadato locale, non un'identità autenticata;
- per ripristinare il requisito collaborativo servirà una nuova decisione architetturale.

## Evoluzione futura

L'ADR-0001 resta come analisi delle alternative multiutente. Potrà essere riaperta scegliendo una
Minimal API con SQLite locale al server oppure un database client/server. I repository del Core
continuano a non dipendere dalla UI per ridurre il costo di tale evoluzione.
