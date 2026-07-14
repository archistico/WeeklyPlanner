# ADR-0001 — Persistenza multiutente

- **Stato:** differita; sostituita per la fase corrente da ADR-0002
- **Data:** 14 luglio 2026
- **Decision owner:** progetto WeeklyPlanner

## Contesto

Il prototipo iniziale apre lo stesso file SQLite direttamente da più client tramite una cartella di
rete. La soluzione è semplice da distribuire, ma delega al filesystem remoto la correttezza dei lock
e dell'ordinamento delle scritture.

`busy_timeout`, retry e transazioni brevi aiutano a gestire `SQLITE_BUSY` e `SQLITE_LOCKED`, ma non
possono correggere un'implementazione incompleta o incoerente del locking da parte della share.
SQLite documenta espressamente questo rischio e indica come soluzione preferibile mantenere il file
sullo stesso host del processo SQLite, esponendo l'accesso ai client tramite un proxy o un database
client/server.

WAL non risolve il problema: richiede memoria condivisa tra i processi e non è supportato su un
filesystem di rete.

## Opzioni considerate

### A. SQLite aperto direttamente dalla share

**Vantaggi**

- nessun processo centrale;
- distribuzione iniziale molto semplice;
- modifica minima del prototipo.

**Svantaggi**

- rischio di corruzione non eliminabile dall'applicazione;
- affidabilità dipendente dalla specifica share e dal relativo protocollo;
- test multiutente e recovery più difficili;
- timestamp generati da client differenti;
- evoluzione futura più fragile.

Questa opzione può essere mantenuta soltanto come modalità sperimentale o come rischio accettato in
modo esplicito. Non deve essere descritta come persistenza multiutente sicura.

### B. `WeeklyPlanner.Server` + SQLite locale

Una piccola ASP.NET Core Minimal API viene eseguita su una macchina sempre disponibile. Il file
SQLite resta sul disco locale della stessa macchina e i client usano HTTP.

**Vantaggi**

- un solo processo accede al file;
- revisioni, transazioni e ordinamento vengono centralizzati;
- deployment ancora leggero;
- nessun database server esterno;
- base naturale per activity log, commenti e assegnazioni.

**Svantaggi**

- introduce un servizio da avviare e aggiornare;
- richiede configurazione di porta, firewall e URL;
- la disponibilità dipende dalla macchina host.

### C. Database client/server esistente

SQL Server o PostgreSQL eliminano l'accesso al file condiviso e forniscono concorrenza, backup e
strumenti maturi. È la scelta preferibile se l'infrastruttura è già disponibile, ma aumenta il peso
del progetto se va introdotta appositamente.

## Direzione raccomandata per un futuro ritorno al multiutente

Adottare **B — `WeeklyPlanner.Server` + SQLite locale**. Conserva SQLite e la semplicità operativa,
ma rimuove il presupposto più fragile del prototipo.

Per la fase local-first definita da ADR-0002:

- l'accesso multiutente fra computer è differito;
- il database deve restare sul filesystem locale;
- le milestone che cambiano lo schema dati devono restare piccole e reversibili;
- il contratto dei repository deve evitare dipendenze dalla UI, per consentire un futuro passaggio a HTTP.

## Fonti tecniche

- SQLite, *Over a network, caveat emptor*: https://sqlite.org/useovernet.html
- SQLite, *Write-Ahead Logging — Disadvantages*: https://sqlite.org/wal.html
