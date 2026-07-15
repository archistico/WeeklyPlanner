# WeeklyPlanner — Obiettivi e roadmap

Versione documento: **2.2.0**  
Ultimo aggiornamento: **15 luglio 2026**

## 1. Visione

WeeklyPlanner è un'applicazione desktop locale in **C# / .NET 10 / Avalonia** per gestire attività
tramite un kanban a swimlane.

Il nome WeeklyPlanner viene mantenuto, ma la struttura non è più legata ai giorni della settimana.
La board combina due dimensioni:

- **stato del lavoro**, rappresentato dalle colonne;
- **tipologia o area progettuale**, rappresentata dalle fasce orizzontali.

Esempi di fasce:

- Generica;
- WinCliente;
- WinCassa;
- Porting SIP;
- Personale.

## 2. Principi di sviluppo

- milestone piccole e verificabili;
- build senza warning;
- test automatici prima della chiusura di ogni milestone;
- migrazioni incrementali, protette e reversibili;
- SQLite locale senza server;
- nessuna perdita silenziosa di dati o bozze;
- storico funzionale atomico con la mutazione;
- UI e persistenza separate;
- documentazione aggiornata insieme al codice;
- refactoring prima di aggiungere complessità non governabile.

## 3. Modello kanban

### 3.1 Colonne operative

Le colonne sono cinque voci di sistema:

| Ordine | Chiave | Titolo |
|---:|---|---|
| 0 | `backlog` | BACKLOG |
| 1 | `todo` | TODO |
| 2 | `in_progress` | IN PROGRESS |
| 3 | `testing` | TESTING |
| 4 | `done` | DONE |

Non sono configurabili, rinominabili o eliminabili.

`TIPOLOGIA` è soltanto l'intestazione visuale della prima area descrittiva e non una colonna dati.

### 3.2 Fasce orizzontali

Le righe della matrice sono le tipologie. Ogni fascia contiene una cella per ciascuno stato.

```text
┌──────────────┬─────────┬──────┬─────────────┬─────────┬──────┐
│ TIPOLOGIA    │ BACKLOG │ TODO │ IN PROGRESS │ TESTING │ DONE │
├──────────────┼─────────┼──────┼─────────────┼─────────┼──────┤
│ Generica     │  Card   │      │             │         │      │
├──────────────┼─────────┼──────┼─────────────┼─────────┼──────┤
│ WinCliente   │         │ Card │    Card     │         │ Card │
├──────────────┼─────────┼──────┼─────────────┼─────────┼──────┤
│ WinCassa     │  Card   │      │             │  Card   │      │
└──────────────┴─────────┴──────┴─────────────┴─────────┴──────┘
```

La posizione persistita della card è:

```text
(CardTypeId, ColumnId, SortOrder)
```

### 3.3 Tipologia Generica

Generica è una voce di sistema:

- chiave `generic`;
- sempre prima;
- sempre attiva;
- non eliminabile, rinominabile, disattivabile o riordinabile;
- destinazione delle card senza tipologia;
- fascia iniziale delle nuove card.

### 3.4 Priorità

Le priorità restano un attributo della card, indipendente dalla posizione:

| Codice | Nome | Scadenza predefinita |
|---|---|---:|
| U | Urgente | 72 ore |
| B | Breve | 10 giorni |
| D | Differibile | 30 giorni |
| P | Programmabile | 120 giorni |

Le regole alternative per tipologia sono mantenute. Esempio:

```text
D + Esame strumentale = 60 giorni
```

La priorità verrà selezionata tramite un ComboBox compatto sotto le note della card.

## 4. Persistenza

### 4.1 Architettura

```text
WeeklyPlanner.App ──> SQLite locale
```

Percorso predefinito:

```text
%LOCALAPPDATA%\WeeklyPlanner\Data\weeklyplanner.db
```

Non sono supportati database distinti sincronizzati fra computer o accesso tramite share di rete.

### 4.2 Schema corrente v5

```text
SchemaVersion(Version)
BoardState(Id, Revision)
Columns(Id, Name, SortOrder, SystemKey, IsSystem)
Priorities(Id, Code, Name, Description, DefaultDueHours, SortOrder, IsActive, IsDefault, Version)
CardTypes(Id, Name, ColorHex, SortOrder, IsActive, IsDefault, Version, SystemKey, IsSystem)
PriorityTypeDeadlines(PriorityId, CardTypeId, DueHours, Version)
Cards(
    Id, ColumnId, StableId,
    CreatedAtUtc, CreatedAtIsEstimated,
    PriorityId, CardTypeId, PriorityAssignedAtUtc, DueAtUtc,
    Title, Notes, SortOrder,
    CreatedBy, UpdatedBy, UpdatedAtUtc, Version
)
CardEvents(
    Id, CardStableId, CardId, EventType, OccurredAtUtc,
    UserName, SessionId, MachineName, Summary, DataJson, FormatVersion
)
CardEditLocks(CardId, SessionId, UserName, MachineName, AcquiredAtUtc, LastHeartbeatUtc, ExpiresAtUtc)
```

### 4.3 Migrazione v4 → v5

- Backlog diventa BACKLOG;
- Lunedì-Domenica confluiscono in TODO;
- l'ordine è deterministico: vecchia colonna, `SortOrder`, ID;
- vengono create IN PROGRESS, TESTING e DONE;
- viene creata Generica oppure promossa un’eventuale tipologia omonima già esistente;
- le card senza tipologia ricevono Generica;
- vengono scritti eventi `WorkflowMigrated` e `TypeMigrated`;
- il backup preventivo M3.4 protegge l'upgrade;
- `integrity_check` e `foreign_key_check` validano il risultato.

### 4.4 Snapshot atomico

`BoardSnapshotRepository` legge in un'unica transazione:

- revisione;
- colonne;
- card;
- tipologie;
- priorità;
- regole di scadenza;
- lock attivi.

Il polling non deve mai costruire la UI combinando dati appartenenti a revisioni differenti.

## 5. Regole funzionali

### 5.1 Creazione

La destinazione definitiva delle nuove card sarà:

```text
Generica / BACKLOG
```

La UI transitoria M3.7 conserva ancora i pulsanti sulle colonne; il comportamento definitivo verrà
applicato con il layout a swimlane.

### 5.2 Drag&drop

Il trascinamento futuro potrà modificare in una sola operazione:

- tipologia;
- stato;
- ordine nella cella.

Il drop su TIPOLOGIA sarà rifiutato.

### 5.3 Timestamp

`UpdatedAtUtc` rappresenta l'ultima modifica funzionale della singola card.

Il riordino tecnico delle card vicine aggiorna `SortOrder`, ma non `UpdatedAtUtc` o `UpdatedBy`.
Soltanto la card trascinata viene marcata come modificata.

### 5.4 Editing

- lock applicativo per card;
- lease rinnovato da heartbeat;
- indicatore dell'utente che modifica;
- bozza protetta dal polling;
- `Cards.Version` per concorrenza ottimistica;
- salvataggio atomico di titolo, note, priorità e metadati;
- annullamento senza perdita dei dati persistiti.

### 5.5 Storico

Eventi funzionali:

- Imported;
- WorkflowMigrated;
- TypeMigrated;
- Created;
- Updated;
- PriorityChanged;
- TypeChanged;
- Moved;
- Reordered;
- Deleted.

Gli eventi vengono salvati nella stessa transazione della modifica.

## 6. Milestone completate

| Milestone | Risultato |
|---|---|
| M0.1.2 | baseline compilabile, testabile e senza vulnerabilità bloccanti |
| M1.1.1 | SQLite locale e revisione monotona |
| M1.2 | ordinamento atomico |
| M1.3.1 | editing protetto e lock applicativi |
| M1.4 | lifecycle e recovery |
| M2.1.1 | CRUD completo e validazione |
| M2.2.3 | drag&drop, tastiera e pan orizzontale |
| M2.3 | impostazioni e tema; la geometria finestra viene rimossa in M3.12.1 |
| M3.1 | composition root e dependency injection |
| M3.2.1 | scheduler deterministici e feedback di persistenza |
| M3.3.4 | logging, diagnostica e shutdown affidabile |
| M3.4 | backup e rollback delle migrazioni |
| M3.5 | schema v4, cataloghi e storico |
| M3.6.1 | CRUD priorità e tipologie |
| M3.7 | modello kanban, schema v5 e snapshot atomico |
| M3.8 | configurazione delle fasce e trasferimento atomico delle card |
| M3.9 | layout kanban a swimlane e pan bidimensionale |
| M3.10 | movimento bidimensionale atomico |
| M3.11 | priorità compatta, editing stabile e avvio massimizzato |
| M3.12 | ultimo salvataggio relativo e stati di persistenza |
| M3.12.1 | consolidamento tecnico, startup e regole condivise |
| M3.13 | informazioni card, lock e cronologia paginata |

## 7. Milestone corrente

### M3.13 — Informazioni e cronologia

#### Obiettivi

- aggiungere l'icona `i` accanto all'indicatore di salvataggio e al cestino;
- aprire una finestra modale senza acquisire il lock di editing;
- mostrare creazione, ultima modifica, fascia, stato, priorità e scadenza;
- mostrare il lock corrente con proprietario, computer e scadenza del lease;
- caricare la cronologia dal più recente in pagine da 20 eventi;
- consentire di raggiungere l’ultimo elemento senza tagli tramite spazio tecnico inferiore;
- descrivere chiaramente movimento fra celle e riordino nella stessa cella;
- mantenere lo schema SQLite alla versione 5.

#### Test

- risoluzione dei nomi correnti di fascia, stato e priorità;
- lock assente, lock della sessione corrente e lock esterno;
- prima pagina e caricamento tramite `beforeEventId`;
- etichette leggibili per eventi di movimento e riordino;
- assenza di nuove migrazioni SQLite.

#### Criteri di chiusura

1. `dotnet build` senza warning o errori;
2. `dotnet test` completamente verde;
3. icona informazioni presente su ogni card;
4. modale centrata sulla finestra principale;
5. dati correnti e lock leggibili anche senza cronologia;
6. paginazione limitata a 20 eventi per lettura;
7. descrizioni bidimensionali senza esposizione del JSON tecnico;
8. badge `M3.13` e schema SQLite v5.

## 8. Roadmap successiva

### M3.14 — Consolidamento kanban

- test UI headless;
- due istanze sullo stesso database;
- molte fasce e molte card;
- temi chiaro/scuro;
- accessibilità;
- prestazioni;
- screenshot e manuali;
- rimozione dei riferimenti residui al planner settimanale.

## 9. Post-MVP

- backup e restore manuale dalla UI;
- verifica integrità dalla UI;
- packaging Windows portable e self-contained;
- release candidate;
- filtri per fascia, stato e priorità;
- ricerca;
- notifiche sulle scadenze;
- eventuale API/server solo come progetto separato futuro.

## 10. Decisioni annullate o sostituite

| Decisione precedente | Decisione corrente |
|---|---|
| Backlog + sette giorni | cinque stati kanban |
| tipologia come semplice attributo visuale | tipologia come fascia orizzontale |
| scelta tipologia da ComboBox sulla card | scelta tramite posizione nella fascia |
| CRUD libero delle colonne | colonne di sistema fisse |
| settimane precedenti/successive | fuori dalla roadmap corrente |
| archiviazione settimanale | fuori dalla roadmap corrente |

ADR di riferimento:

- [`docs/ADR-0011-modello-kanban-swimlane.md`](docs/ADR-0011-modello-kanban-swimlane.md);
- [`docs/ADR-0014-movimento-bidimensionale.md`](docs/ADR-0014-movimento-bidimensionale.md);
- [`docs/ADR-0017-consolidamento-tecnico-startup.md`](docs/ADR-0017-consolidamento-tecnico-startup.md);
- [`docs/ADR-0018-informazioni-cronologia-card.md`](docs/ADR-0018-informazioni-cronologia-card.md).
