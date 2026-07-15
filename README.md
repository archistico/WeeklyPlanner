# WeeklyPlanner

> **M3.14 — Consolidamento kanban**

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

La M3.10 e la M3.11 sono state validate con compilazione, test e verifica manuale riusciti. La M3.12
rende permanente e leggibile il feedback di persistenza, la M3.12.1 consolida startup, movimento
bidimensionale, scadenze e conflitti e la M3.13 aggiunge metadati, lock e cronologia paginata.

La M3.14 chiude il consolidamento del kanban con test Avalonia headless, doppia istanza sul medesimo
SQLite, scenario di scala, verifica dei temi e dell'accessibilità, manuale utente e cattura ripetibile
degli screenshot. Lo schema SQLite resta alla versione 5.

La suite dichiara **oltre 240 casi** considerando i test parametrizzati.

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

La finestra principale viene configurata una sola volta, prima della visualizzazione, come normale
finestra Windows massimizzata e attiva. WeeklyPlanner non salva né ripristina posizione, dimensione o
stato della finestra e non usa `Topmost`. Dopo l’onboarding la board viene mostrata, l’onboarding viene
chiuso e l’attivazione viene richiesta una sola volta sul dispatcher. Un margine inferiore più ampio
consente di visualizzare integralmente la card più bassa.

## Ultimo salvataggio relativo

Il footer della card mostra un solo stato di persistenza alla volta:

- **floppy verde + tempo relativo** quando la card è salvata;
- **Non salvata** quando la bozza contiene modifiche;
- **Salvataggio…** durante la scrittura;
- **Errore** quando la persistenza fallisce, con il dettaglio completo nel tooltip.

Il testo relativo usa `adesso`, minuti, ore e giorni. Passando il mouse sul floppy viene mostrata la
data locale completa al secondo e, quando disponibile, il nome dell'ultimo utente che ha modificato la
card. Il valore deriva da `UpdatedAtUtc`, con fallback a `CreatedAtUtc` per eventuali righe legacy.

L'aggiornamento temporale è centralizzato nel ticker di polling della board: tutte le card ricevono lo
stesso istante e nessun `CardViewModel` crea un timer autonomo. Quando un'altra istanza modifica la
card, il merge sostituisce il timestamp e aggiorna immediatamente testo relativo e tooltip.

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

Nella M3.14 la finestra mostra il badge `M3.14`, si apre massimizzata senza ripristino della
geometria e resta attiva anche dopo l’onboarding. Ogni card espone l'icona informazioni per aprire
metadati, lock e cronologia paginata.

## Consolidamento M3.12.1

- `AppSettings` conserva soltanto preferenze applicative e percorso del database; eventuali campi
  legacy della geometria presenti in `settings.json` vengono ignorati;
- `MainWindow` non ascolta più eventi di posizione o ridimensionamento e non riscrive i settings alla
  chiusura;
- `ICardRepository` espone soltanto `MoveToCellAsync`, coerente con la posizione
  `(CardTypeId, ColumnId, indice locale)`;
- `PriorityDeadlineCalculator` è l’unica fonte di verità per durata predefinita, override di fascia e
  calcolo della scadenza;
- un conflitto di versione conserva la bozza, blocca il nuovo salvataggio e mostra un solo messaggio;
- titolo e note vengono confrontati una sola volta prima di costruire audit e riepilogo.

## Informazioni e cronologia M3.13

Nel footer di ogni card, accanto all'indicatore di salvataggio e al cestino, è disponibile l'icona
`i`. L'azione apre una finestra modale centrata sulla board senza entrare in modifica e senza acquisire
un nuovo lock.

La finestra mostra:

- data e autore di creazione, evidenziando le date stimate provenienti dalle migrazioni;
- data e autore dell'ultima modifica;
- fascia, stato operativo, priorità e scadenza correnti;
- eventuale lock attivo, proprietario, computer, acquisizione e scadenza del lease;
- cronologia dal più recente, caricata in pagine da 20 eventi.
- spazio inferiore di scorrimento per visualizzare integralmente l’ultimo evento o comando.

Gli eventi di movimento distinguono cambio di cella e riordino locale. Le descrizioni persistite
indicano fascia e stato di origine e destinazione, così il percorso bidimensionale resta leggibile
senza esporre il JSON tecnico dell'audit. Il caricamento di pagine precedenti usa l'identificativo
dell'ultimo evento visualizzato e non richiede nuove tabelle o migrazioni.


## Consolidamento M3.14

La suite carica il XAML reale con `Avalonia.Headless.XUnit` e verifica la finestra principale sotto i
temi Chiaro e Scuro. Le azioni prive di testo espongono tooltip e nomi per le tecnologie assistive;
titolo, note, priorità e maniglia di trascinamento sono identificabili e il riepilogo priorità si
attiva anche con `Invio` o `Spazio`.

Un test di integrazione apre due insiemi indipendenti di repository sullo stesso file SQLite e verifica
lock condivisi, revisione e controllo ottimistico. Uno smoke test costruisce 30 fasce e 1.500 card con
un budget ampio di dieci secondi per intercettare regressioni macroscopiche, senza presentarsi come
benchmark hardware.

Il tema chiaro usa un accento con contrasto maggiore; entrambi i temi vengono verificati sulle coppie
testuali principali con rapporto minimo 4,5:1. I target compatti principali sono stati ampliati e il
focus della priorità è visibile senza cambiare lo sfondo normale della card.

Documentazione:

- [`docs/MANUALE-UTENTE.md`](docs/MANUALE-UTENTE.md);
- [`docs/VERIFICA-M3.14.md`](docs/VERIFICA-M3.14.md);
- [`docs/ADR-0019-consolidamento-kanban.md`](docs/ADR-0019-consolidamento-kanban.md).

Gli screenshot chiaro e scuro vengono generati dal XAML reale con dati fittizi:

```powershell
.\scripts\capture-ui.ps1
```

La cattura non apre né modifica il database configurato dall'utente.

## Roadmap immediata

1. **M4 — Packaging MVP locale**  
   Publish Windows portable e self-contained, backup documentato e smoke test distribuibile.
2. **Post-MVP**  
   Filtri, ricerca, notifiche e ulteriori strumenti di amministrazione.

Le decisioni del modello corrente sono descritte in:

- [`docs/ADR-0011-modello-kanban-swimlane.md`](docs/ADR-0011-modello-kanban-swimlane.md);
- [`docs/ADR-0012-configurazione-fasce.md`](docs/ADR-0012-configurazione-fasce.md);
- [`docs/ADR-0013-layout-kanban-swimlane.md`](docs/ADR-0013-layout-kanban-swimlane.md);
- [`docs/ADR-0014-movimento-bidimensionale.md`](docs/ADR-0014-movimento-bidimensionale.md);
- [`docs/ADR-0015-priorita-compatta-card.md`](docs/ADR-0015-priorita-compatta-card.md);
- [`docs/ADR-0016-ultimo-salvataggio-relativo.md`](docs/ADR-0016-ultimo-salvataggio-relativo.md);
- [`docs/ADR-0017-consolidamento-tecnico-startup.md`](docs/ADR-0017-consolidamento-tecnico-startup.md);
- [`docs/ADR-0018-informazioni-cronologia-card.md`](docs/ADR-0018-informazioni-cronologia-card.md);
- [`docs/ADR-0019-consolidamento-kanban.md`](docs/ADR-0019-consolidamento-kanban.md).
