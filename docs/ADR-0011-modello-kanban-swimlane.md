# ADR-0011 — Modello kanban a swimlane

## Stato

Accettato. Layout e movimento definitivi sono completati da ADR-0013 e ADR-0014.

## Contesto

WeeklyPlanner era nato come planner settimanale con Backlog e sette colonne giornaliere. L'uso reale
richiede invece un kanban nel quale lo stato del lavoro e l'area progettuale siano due dimensioni
indipendenti.

## Decisione

La board usa cinque stati di sistema, identificati da chiavi stabili:

- `backlog` — BACKLOG;
- `todo` — TODO;
- `in_progress` — IN PROGRESS;
- `testing` — TESTING;
- `done` — DONE.

Le tipologie sono le fasce orizzontali della board. La prima fascia è la tipologia di
sistema `generic`, visualizzata come Generica. Ogni card deve appartenere a una tipologia.
Generica resta sempre attiva, al primo posto e predefinita per le nuove card; le altre tipologie non
possono sostituirla come valore predefinito.

La posizione persistita della card è:

```text
(CardTypeId, ColumnId, SortOrder)
```

`TIPOLOGIA` è soltanto l'intestazione visuale della prima colonna descrittiva e non una riga della
tabella `Columns`.

## Migrazione

La migrazione v5:

1. conserva Backlog come BACKLOG;
2. unifica Lunedì-Domenica in TODO;
3. ordina le card migrate per vecchia colonna, `SortOrder` e ID;
4. crea IN PROGRESS, TESTING e DONE;
5. crea Generica come tipologia di sistema;
6. assegna Generica alle card senza tipologia;
7. registra `WorkflowMigrated` e `TypeMigrated` in `CardEvents`;
8. protegge le colonne di sistema da rinomina ed eliminazione.

L'upgrade è protetto dal backup e dal rollback introdotti in M3.4.

## Snapshot

La board viene letta tramite `BoardSnapshotRepository` in una singola transazione SQLite. Lo snapshot
contiene revisione, colonne, tipologie, priorità, regole, card e lock attivi. Il ViewModel non combina
più letture appartenenti a revisioni differenti.

## Timestamp

Il riordino di una card può cambiare il `SortOrder` delle card vicine, ma non deve modificarne
`UpdatedAtUtc` o `UpdatedBy`. Soltanto la card trascinata riceve un nuovo timestamp funzionale.
Questo rende affidabile l’indicatore di ultimo salvataggio definito da ADR-0016.

## Conseguenze

- la UI mostra la matrice a swimlane definita da ADR-0013;
- il drag&drop usa il contratto bidimensionale definito da ADR-0014;
- il CRUD delle colonne libere è eliminato dalla roadmap;
- Generica e le regole delle fasce sono governate da ADR-0012.
