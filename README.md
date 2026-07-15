# WeeklyPlanner

Applicazione desktop **C# / .NET 10 / Avalonia** per organizzare attività in un kanban locale a
swimlane. WeeklyPlanner usa SQLite, non richiede un server e mantiene software e dati utente
separati.

La board combina due dimensioni:

- le **colonne** rappresentano lo stato del lavoro;
- le **fasce orizzontali** rappresentano la tipologia o area progettuale;
- priorità, scadenza, note e storico restano attributi della card.

```text
TIPOLOGIA | BACKLOG | TODO | IN PROGRESS | TESTING | DONE
```

## Stato del progetto

La baseline funzionale del kanban è stata consolidata e validata fino alla M3.14. La M4 implementa il
processo di packaging Windows x64 con distribuzioni `portable` e `self-contained`, archivi ZIP,
checksum e verifica automatica dei contenuti. Prima di aprire M5.1 resta da completare il gate di
rilascio M4 con generazione e smoke test dei due pacchetti.

La fase successiva è il post-MVP locale. La roadmap parte da:

1. backup, ripristino e controllo integrità dalla UI;
2. ricerca e filtri;
3. etichette multiple e viste salvate;
4. checklist;
5. collegamenti e allegati locali.

La pianificazione completa è in
[`WeeklyPlanner-Obiettivi-e-Roadmap.md`](WeeklyPlanner-Obiettivi-e-Roadmap.md).

## Funzioni disponibili

### Board a swimlane

- cinque stati di sistema: BACKLOG, TODO, IN PROGRESS, TESTING e DONE;
- fasce configurabili, con **Generica** sempre presente e al primo posto;
- drag&drop bidimensionale fra fascia, stato e posizione;
- riordino e movimento tramite tastiera;
- pan con tasto centrale e scroll globale;
- creazione rapida da ciascuna intestazione di stato;
- fasce inattive ancora visibili quando contengono card.

### Card

- titolo e note modificabili direttamente nella board;
- priorità configurabile con scadenza standard o specifica per fascia;
- indicatore compatto della priorità fuori modifica;
- feedback di persistenza e ultimo salvataggio relativo;
- informazioni complete e cronologia paginata;
- storico funzionale persistente anche dopo l'eliminazione;
- controllo ottimistico tramite `Cards.Version`.

### Concorrenza locale

- lock applicativo a scadenza per card;
- heartbeat periodico;
- bozza protetta dal polling;
- conflitti espliciti senza perdita della bozza;
- più istanze sullo stesso database locale;
- shutdown coordinato con rilascio dei lock.

WeeklyPlanner non sincronizza database distinti e non supporta database collocati su share di rete.

### Configurazione

Dalle impostazioni è possibile gestire:

- nome utente;
- percorso del database;
- intervallo di polling;
- tema Sistema, Chiaro o Scuro;
- priorità e relative durate;
- fasce, colori e ordine;
- regole di scadenza specifiche per fascia;
- trasferimento atomico delle card quando viene eliminata una fascia usata.

### Diagnostica e affidabilità

- log tecnici JSON Lines con rotazione e retention;
- riferimenti errore `WP-XXXXXX` correlabili con i log;
- finestra diagnostica senza esposizione di titolo o note;
- snapshot atomico di board, cataloghi, card e lock;
- backup preventivo e rollback delle migrazioni;
- test Avalonia headless, doppia istanza, temi, accessibilità e scenario di scala.

## Persistenza

WeeklyPlanner usa un file SQLite locale. Il percorso predefinito è:

```text
%LOCALAPPDATA%\WeeklyPlanner\Data\weeklyplanner.db
```

Lo schema corrente è la versione **5** e include:

```text
SchemaVersion
BoardState
Columns
Cards
CardEditLocks
Priorities
CardTypes
PriorityTypeDeadlines
CardEvents
```

Database, impostazioni e log non vengono inclusi nei pacchetti di distribuzione.

## Struttura del repository

- `WeeklyPlanner.Core`: modelli, repository, migrazioni, audit, cataloghi, resilienza e SQLite;
- `WeeklyPlanner.App`: Avalonia, MVVM, composition root, scheduler, logging e diagnostica;
- `WeeklyPlanner.Tests`: test unitari, integrazione SQLite e test UI headless;
- `docs`: decisioni architetturali, manuali, roadmap operativa e documentazione di rilascio;
- `scripts`: verifica, screenshot, publish e packaging PowerShell.

## Prerequisiti

- SDK .NET 10 compatibile con `global.json`;
- Windows per la verifica primaria dell'app desktop;
- accesso a NuGet durante il restore.

Warning del compilatore, analyzer e audit NuGet sono trattati come errori.

## Build e test

```powershell
.\scripts\verify.ps1
```

Comandi equivalenti:

```powershell
dotnet restore WeeklyPlanner.sln
dotnet build WeeklyPlanner.sln -c Release --no-restore
dotnet test WeeklyPlanner.sln -c Release --no-build
```

## Avvio da sorgente

```powershell
dotnet run --project .\WeeklyPlanner.App\WeeklyPlanner.App.csproj
```

La finestra principale viene aperta come normale applicazione Windows massimizzata. Posizione e
dimensioni non vengono salvate o ripristinate.

## Packaging Windows

```powershell
.\scripts\release.ps1
```

Il comando:

- verifica build e test in configurazione Release;
- produce un pacchetto `portable` framework-dependent per `win-x64`;
- produce un pacchetto `self-contained` per `win-x64`;
- crea gli archivi ZIP;
- genera `SHA256SUMS.txt`;
- verifica automaticamente i contenuti dei pacchetti.

Single-file e trimming restano disattivati per preservare risorse Avalonia e dipendenze native
SQLite.

## Documentazione

Indice completo: [`docs/README.md`](docs/README.md).

### Uso e distribuzione

- [`docs/MANUALE-UTENTE.md`](docs/MANUALE-UTENTE.md)
- [`docs/BACKUP-RIPRISTINO.md`](docs/BACKUP-RIPRISTINO.md)
- [`docs/SMOKE-TEST-M4.md`](docs/SMOKE-TEST-M4.md)
- [`docs/RELEASE-CHECKLIST-M4.md`](docs/RELEASE-CHECKLIST-M4.md)
- [`docs/RELEASE-NOTES-M4.md`](docs/RELEASE-NOTES-M4.md)

### Pianificazione

- [`WeeklyPlanner-Obiettivi-e-Roadmap.md`](WeeklyPlanner-Obiettivi-e-Roadmap.md)
- [`docs/MILESTONES.md`](docs/MILESTONES.md)
- [`docs/DIREZIONE-PRODOTTO.md`](docs/DIREZIONE-PRODOTTO.md)
- [`docs/REGRESSION-CHECKLIST-KANBAN.md`](docs/REGRESSION-CHECKLIST-KANBAN.md)

### Architettura

Le decisioni tecniche sono conservate come ADR in `docs/ADR-*.md`. Gli ADR descrivono il motivo di
una scelta e possono contenere riferimenti storici; lo stato operativo corrente è definito dal
codice, dalla roadmap e dai documenti sopra elencati.

## Confini del prodotto

Nel post-MVP WeeklyPlanner resta un'applicazione **local-first**. Non sono previste a breve:

- autenticazione, ruoli e workspace condivisi;
- sincronizzazione cloud;
- link pubblici;
- API pubblica e plugin;
- Gantt o calendario completo;
- server obbligatorio.

Board multiple e collaborazione distribuita sono possibili evoluzioni separate, da valutare solo
dopo il consolidamento delle funzioni locali.
