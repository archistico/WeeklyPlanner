# WeeklyPlanner

> **M3.11 — Priorità compatta sulla card**

Applicazione desktop **C# / .NET 10 / Avalonia** per organizzare attività in un kanban locale,
sviluppata per milestone piccole, test automatici, migrazioni protette e documentazione aggiornata
insieme al codice.

Il nome WeeklyPlanner resta invariato, ma il prodotto non è più concepito come semplice planner
settimanale. La direzione corrente è una board kanban a **swimlane**:

- le colonne rappresentano lo stato del lavoro;
- le fasce orizzontali rappresentano la tipologia o area progettuale;
- priorità, storico e scadenze restano attributi della card.

## Stato del progetto

La baseline fino alla **M3.8** è stata validata su Windows con compilazione e test riusciti.
Il layout M3.9, comprese le correzioni a larghezza, scroll, pan e movimento nella fascia, è stato
accettato dall'utente nella prova manuale.

La M3.10 è stata validata con compilazione e test completamente riusciti. Anche la baseline M3.11
ha superato compilazione e test; questo aggiornamento corregge il comportamento runtime di editing,
ComboBox e attivazione iniziale della finestra. La priorità resta un campo compatto della card senza
nuove tabelle: selezione, scadenza, lock, conflitti e storico riusano lo schema SQLite v5 e la
transazione di modifica esistente.

La suite dichiara **oltre 225 casi** considerando i test parametrizzati.

## Workflow kanban

Le colonne operative sono cinque e sono voci di sistema:

| Chiave stabile | Titolo |
|---|---|
| `backlog` | BACKLOG |
| `todo` | TODO |
| `in_progress` | IN PROGRESS |
| `testing` | TESTING |
| `done` | DONE |

Le colonne non possono essere rinominate o eliminate. `TIPOLOGIA` non è una colonna persistita:
è l'intestazione visuale della prima area descrittiva nel layout a swimlane.


## Layout kanban a swimlane

La finestra principale mostra una matrice con intestazioni fisse:

```text
TIPOLOGIA | BACKLOG | TODO | IN PROGRESS | TESTING | DONE
```

Ogni tipologia è una fascia orizzontale. La prima cella contiene nome, pallino e banda nel colore
configurato; le altre cinque celle contengono le card del relativo stato. Le celle della stessa fascia
condividono automaticamente l'altezza; le intersezioni vuote restano visivamente pulite.

Generica è sempre la prima fascia. Le fasce attive seguono l'ordine configurato; una fascia inattiva
rimane visibile soltanto se contiene ancora card, così nessun elemento viene nascosto dalla board.

La matrice si estende alla larghezza disponibile e conserva una larghezza minima per non comprimere
le card. Lo scroll verticale è unico per tutta la board e include margine sufficiente per visualizzare
interamente l'ultima card. Il pan con il tasto centrale agisce su entrambi gli assi.

La proiezione riutilizza gli stessi `CardViewModel` delle collection tecniche delle colonne: lock,
bozze, errori e feedback di salvataggio restano associati a un'unica istanza della card.

In M3.10 il drag&drop accetta qualsiasi cella operativa: consente di cambiare stato, fascia o entrambe
le dimensioni insieme, oltre al riordino prima/dopo/in fondo nella stessa cella. La zona TIPOLOGIA non
è una destinazione valida. Una fascia inattiva continua a mostrare e a far muovere le proprie card fra
gli stati, ma non può ricevere card provenienti da altre fasce.

Le scorciatoie da tastiera sono:

- `Alt` + `Su/Giù`: riordino nella cella;
- `Alt` + `Sinistra/Destra`: cambio stato nella stessa fascia;
- `Ctrl` + `Alt` + `Su/Giù`: cambio verso la fascia attiva precedente o successiva.

Ogni intestazione operativa espone un pulsante `+`: la nuova card viene creata nella fascia
**Generica** direttamente in BACKLOG, TODO, IN PROGRESS, TESTING o DONE. Se il catalogo definisce una
priorità predefinita attiva, questa viene applicata subito e il repository ne calcola la scadenza.

## Priorità compatta sulla card

Fuori modifica ogni card mostra codice, nome e tempo residuo direttamente sullo stesso sfondo della
card, senza un pannello separato. L’intera zona è cliccabile: acquisisce il lock, apre la bozza e porta
il focus alla ComboBox. Il tooltip combina la descrizione della priorità con la scadenza esatta; una
priorità inattiva resta leggibile sulle card esistenti ma non viene proposta per nuove assegnazioni.

Entrando in modifica compare una ComboBox sotto le note. Il click sul riepilogo della priorità apre
direttamente l’elenco; l’opzione `Nessuna` rimuove priorità e scadenza. La bozza non viene più chiusa
quando il focus passa a un popup, a un altro controllo o quando il polling aggiorna i lock: resta attiva
fino a **Salva**, **Annulla** o `Esc`. Titolo, note e priorità usano il medesimo lock, partecipano a
`IsDirty` e vengono persistiti con una sola chiamata `UpdateAsync`. Il repository imposta
`PriorityAssignedAtUtc`, calcola `DueAtUtc` con l’eventuale regola specifica della fascia e registra
`PriorityChanged`; un conflitto conserva l’intera bozza.

La finestra principale viene creata massimizzata, con attivazione iniziale esplicita e un passaggio
`Topmost` temporaneo rimosso subito dopo l’apertura. In questo modo viene mostrata davanti alle altre
applicazioni senza restare sempre in primo piano. Un margine inferiore più ampio consente di scorrere
oltre l’ultima fascia e visualizzare sempre integralmente la card più bassa.

## Tipologie come fasce

Le tipologie introdotte nello schema v4 sono le fasce orizzontali della board.

La migrazione v5 crea la tipologia di sistema:

```text
Generica
```

Generica:

- è la prima tipologia;
- è attiva e predefinita;
- riceve automaticamente le card che non avevano una tipologia;
- viene identificata dalla chiave stabile `generic`;
- non può essere eliminata, rinominata, disattivata o riordinata.

Le altre tipologie esistenti vengono conservate e ordinate sotto Generica. Dalla M3.8 la finestra
**Configura board** le tratta esplicitamente come fasce: mostra il numero di card assegnate, non
propone più un default configurabile e non include Generica nel riordino.

### Eliminazione di una fascia

Una fascia vuota può essere eliminata direttamente. Se contiene card, l’utente deve scegliere una
fascia di destinazione attiva; Generica viene proposta per prima. L’operazione è atomica:

- cambia soltanto `CardTypeId`;
- conserva `ColumnId` e `SortOrder`;
- conserva `PriorityAssignedAtUtc`;
- ricalcola `DueAtUtc` con le regole della fascia di destinazione;
- incrementa la versione della card e aggiorna l’autore dell’operazione;
- registra un evento `TypeChanged` per ogni card trasferita.

Se una card della fascia è aperta in modifica, l’eliminazione viene rifiutata per non invalidare la
bozza attiva.

## Migrazione dal planner settimanale

L'upgrade dallo schema v4 applica questa trasformazione:

- `Backlog` → `BACKLOG`;
- `Lunedì`–`Domenica` → `TODO`;
- `IN PROGRESS`, `TESTING` e `DONE` vengono create come nuove colonne;
- le card dei giorni vengono ordinate per vecchia colonna, posizione e ID;
- le card senza tipologia vengono assegnate a Generica.

Ogni trasformazione viene registrata nello storico funzionale:

- `WorkflowMigrated` per il cambio di stato;
- `TypeMigrated` per l'assegnazione a Generica.

Prima della migrazione viene creato un backup SQLite coerente. Se upgrade o controllo d'integrità
falliscono, WeeklyPlanner ripristina il database precedente.

## Snapshot atomico della board

`BoardSnapshotRepository` legge in una singola transazione SQLite:

```text
KanbanBoardSnapshot
├── Revision
├── Columns
├── CardTypes
├── Priorities
├── DeadlineRules
├── Cards
└── ActiveLocks
```

Il `BoardViewModel` non combina più colonne, card, cataloghi e lock letti in momenti differenti.
Questo rende affidabile il polling del layout bidimensionale e impedisce stati intermedi incoerenti.

## Ultima modifica reale della card

Il riordino modifica necessariamente il `SortOrder` delle card vicine. Dalla M3.7 questi aggiornamenti
tecnici non modificano più `UpdatedAtUtc` o `UpdatedBy` delle card vicine.

Soltanto la card realmente trascinata riceve un nuovo timestamp funzionale. Questa regola è la base
del futuro indicatore:

```text
💾 1 min fa
💾 3 giorni fa
```

## Persistenza

WeeklyPlanner usa un file SQLite locale, senza server e senza share di rete.

Percorso predefinito:

```text
%LOCALAPPDATA%\WeeklyPlanner\Data\weeklyplanner.db
```

I percorsi UNC e relativi vengono rifiutati. Le directory mancanti vengono create automaticamente.

## Schema SQLite v5

Tabelle principali:

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

Novità v5:

```text
Columns.SystemKey
Columns.IsSystem
CardTypes.SystemKey
CardTypes.IsSystem
```

Ogni card deve avere un `CardTypeId`. Il repository assegna automaticamente Generica quando il
chiamante non specifica una tipologia; trigger SQLite impediscono inserimenti o aggiornamenti con
`CardTypeId` nullo.

## Storico funzionale

`CardEvents` conserva gli eventi anche dopo l'eliminazione della card tramite `CardStableId`.

Eventi disponibili:

- `Imported`;
- `WorkflowMigrated`;
- `TypeMigrated`;
- `Created`;
- `Updated`;
- `PriorityChanged`;
- `TypeChanged`;
- `Moved`;
- `Reordered`;
- `Deleted`.

Titolo e note non vengono duplicati nei payload dello storico.

## Editing e concorrenza locale

- lock applicativo a scadenza per card;
- indicatore dell'utente che sta modificando;
- heartbeat periodico;
- controllo ottimistico tramite `Cards.Version`;
- bozza protetta dal polling;
- salvataggio e annullamento espliciti;
- recovery senza perdita della bozza;
- shutdown coordinato con rilascio dei lock.

La collaborazione vale fra istanze che aprono lo stesso file SQLite locale sulla stessa macchina.
Database distinti non vengono sincronizzati.

## Configurazione

Dalle Impostazioni è possibile gestire:

- nome utente;
- percorso del database;
- intervallo di polling;
- tema Sistema/Chiaro/Scuro;
- geometria della finestra;
- priorità;
- fasce, colori e ordine sotto Generica;
- trasferimento atomico delle card quando si elimina una fascia usata;
- regole di scadenza per fascia.

## Logging e diagnostica

Log tecnici:

```text
%LOCALAPPDATA%\WeeklyPlanner\Logs
```

Caratteristiche:

- JSON Lines;
- coda asincrona;
- rotazione giornaliera e per dimensione;
- retention;
- contenuto delle card redatto;
- riferimento errore `WP-XXXXXX` correlabile con la UI.

La finestra Diagnostica mostra versione, runtime, stato della board, schema SQLite, percorsi e stato
del logger senza esporre titolo o note delle card.

## Struttura

- `WeeklyPlanner.Core`: modelli, repository, migrazioni, audit, cataloghi, resilienza e SQLite;
- `WeeklyPlanner.App`: Avalonia, MVVM, composition root, scheduler, logging e diagnostica;
- `WeeklyPlanner.Tests`: test unitari e integrazione su database SQLite temporanei;
- `docs`: ADR e milestone;
- `scripts`: verifica e publish PowerShell.

Astrazioni rilevanti:

- `IBoardSnapshotRepository`;
- `ICardRepository`;
- `ICardCatalogRepository`;
- `ICardEventRepository`;
- `ICardEditLockRepository`;
- `IBoardChangeDetector`;
- `IDatabaseInitializer`;
- `IDatabaseMigrationBackupService`;
- `IRecurringTaskScheduler`;
- `IAppLogger`.

## Prerequisiti

- SDK .NET 10 compatibile con `global.json`;
- Windows per la verifica primaria dell'app desktop;
- accesso a NuGet durante il restore.

Le versioni NuGet sono centralizzate. Warning del compilatore, analyzer e audit NuGet sono trattati
come errori.

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

## Avvio

```powershell
dotnet run --project .\WeeklyPlanner.App\WeeklyPlanner.App.csproj
```

Nella M3.11 la finestra deve mostrare il badge `M3.11`, aprirsi massimizzata e presentare il pulsante
di creazione in ciascuna intestazione operativa. La priorità deve apparire come badge in lettura e come
ComboBox soltanto nella bozza di modifica.

## Roadmap immediata

1. **M3.12 — Ultimo salvataggio relativo**  
   Floppy verde, tempo relativo e tooltip esatto.
2. **M3.13 — Informazioni e cronologia**  
   Modale con metadati e storico paginato.
3. **M3.14 — Consolidamento kanban**  
   Test UI, prestazioni, accessibilità e documentazione finale.

Le decisioni del modello corrente sono descritte in:

- [`docs/ADR-0011-modello-kanban-swimlane.md`](docs/ADR-0011-modello-kanban-swimlane.md);
- [`docs/ADR-0012-configurazione-fasce.md`](docs/ADR-0012-configurazione-fasce.md);
- [`docs/ADR-0013-layout-kanban-swimlane.md`](docs/ADR-0013-layout-kanban-swimlane.md);
- [`docs/ADR-0014-movimento-bidimensionale.md`](docs/ADR-0014-movimento-bidimensionale.md);
- [`docs/ADR-0015-priorita-compatta-card.md`](docs/ADR-0015-priorita-compatta-card.md).
