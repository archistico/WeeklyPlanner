# WeeklyPlanner — Analisi bug, regressioni ed errori di progettazione

Prima analisi: 15 luglio 2026 (milestone M3.11) — **Aggiornamento**: 15 luglio 2026 dopo le modifiche
della milestone **M3.12** (ultimo salvataggio relativo) e **M3.12.1** (consolidamento tecnico e
startup, ADR-0016/ADR-0017).

Questa analisi copre `WeeklyPlanner.Core` (repository, migrazioni, resilienza) e
`WeeklyPlanner.App` (ViewModel, servizi, interazione), letti insieme a README, roadmap e ADR per
verificare la coerenza tra comportamento documentato e codice effettivo. Non è stata eseguita una
compilazione o un'esecuzione reale dei test in questo passaggio (l'ambiente di analisi non ha
accesso a un SDK .NET né a un desktop Windows interattivo): le osservazioni restano basate su
lettura statica del codice e confronto incrociato con la suite `WeeklyPlanner.Tests`.

## 0. Esito della riverifica: cosa è stato corretto

Le modifiche descritte in ADR-0016 e ADR-0017 intervengono esattamente sui punti segnalati nella
prima analisi. Verificati nel codice attuale:

| Problema segnalato in precedenza | Stato | Verifica |
|---|---|---|
| §1.1 `MoveAsync` non incrementava `Version` (asimmetria con `MoveToCellAsync`) | **Risolto** | `ICardRepository.MoveAsync` è stato rimosso: resta solo `MoveToCellAsync`, che incrementa sempre `Version`. Nessun riferimento residuo a `.MoveAsync(` in tutto il repository (verificato con ricerca globale). |
| §1.2 Tripla implementazione della regola di scadenza | **Risolto** | `CardRepository.CalculateDueAtAsync` e `CardCatalogRepository.CalculateReassignedDueAt` delegano ora entrambi il calcolo a `PriorityDeadlineCalculator.CalculateDueAt(assignedAt, defaultDueHours, overrideDueHours)`; recuperano dal DB solo i valori grezzi, non ripetono più la logica. `CardViewModel` usa lo stesso `PriorityDeadlineCalculator.ResolveDueHours`/`CalculateDueAt` per l'anteprima. Un solo punto di verità, come dichiarato in ADR-0017 punto 5. |
| §1.3 Oggetto `Card` fittizio nel conflitto di concorrenza | **Risolto** | `BoardViewModel.CommitEditAsync` ora chiama `card.MarkConcurrencyConflict()` invece di ricostruire una `Card` incompleta. Il nuovo metodo su `CardViewModel` imposta `HasExternalChanges = true` e un messaggio esplicito (`ConcurrencyConflictMessage`) senza inventare dati. In più, `HasLockStatus` è stato corretto per non mostrare contemporaneamente il banner "la card è cambiata altrove" quando è già visibile l'errore di conflitto (`(IsEditing && !(HasExternalChanges && HasSaveError)) || ... || (HasExternalChanges && !HasSaveError) || ...`): il doppio messaggio segnalato nella versione precedente non si presenta più. |
| §2.1 Regola "non spostare/eliminare se in editing" duplicata fra `CardViewModel.CanDrag` e `CardRepository.EnsureCardIsNotLockedAsync` | **Invariato** | Non toccato da questo giro di modifiche; resta un'osservazione di design minore (vedi §3). |
| §2.2 API morte ereditate dal modello pre-swimlane | **Risolto per `MoveAsync`** | Rimosso insieme ai test e al test double. `PriorityDeadlineCalculator` non è più dead code: è diventato l'unica fonte di verità (vedi sopra). |
| §3 minor — geometria finestra / `WindowPlacementCalculator` | **Superato, nuovo residuo** | `AppSettings` non ha più i campi `WindowWidth/Height/X/Y/Maximized` (rimossi, con un test dedicato — `Load_ignores_legacy_window_geometry_fields` — che verifica che i vecchi JSON legacy vengano letti senza errori e senza far riapparire quei campi). Questo rende però `WindowPlacementCalculator` codice completamente morto: vedi nuovo punto in §3. |
| §3 minor — assunzione ID hardcoded nella migrazione v5 | **Invariato** | Non in ambito di questo refactor (riguarda le migrazioni, non lo startup). Resta valido come osservazione. |
| §4 MainWindow non massimizza / resta in sottofondo | **Risolto (verifica statica)** | Vedi §4 aggiornato: l'intera logica di ripristino geometria + danza `Topmost` è stata rimossa. |

## 1. Problemi residui (invariati rispetto alla prima analisi)

### 1.1 Controllo "card non in editing" applicato due volte, con esiti diversi

`CardViewModel.CanDrag` disabilita silenziosamente il trascinamento se `IsEditing` è vero (la UI
semplicemente non permette il drag). `CardRepository.EnsureCardIsNotLockedAsync` — richiamato da
`DeleteAsync` e `MoveToCellAsync` — applica la stessa regola lanciando `CardEditLockException` se
**una qualunque sessione** detiene il lock, anche se il chiamante non è quella sessione. Le due
regole coincidono oggi, ma vivono in due livelli diversi con due meccanismi di segnalazione diversi
(proprietà bindata vs. eccezione runtime): se la UI un giorno rilassasse la regola, il repository
continuerebbe comunque a bloccare silenziosamente via eccezione generica, con un messaggio pensato
per un caso diverso. Non è un bug, ma un punto da tenere a mente se le due regole dovessero mai
divergere.

### 1.2 Migrazione v5 — assunzione implicita sugli ID storici delle colonne

`0005_kanban_model.sql` assume che le colonne storiche `Backlog`/`Lunedì..Domenica` abbiano sempre
Id 0..7 (`CASE WHEN card.ColumnId = 0 THEN 0 ELSE 1 END`). Vero oggi perché lo schema v1 seeda
sempre quegli Id, ma lo script v5 non lo verifica esplicitamente: un database v1 alterato
manualmente romperebbe la migrazione senza un messaggio d'errore chiaro. Osservazione invariata
rispetto alla prima analisi.

## 2. Nuove osservazioni minori emerse dal refactor M3.12.1

### 2.1 `WindowPlacementCalculator` è ora completamente codice morto

Dopo la rimozione della logica di ripristino/adattamento geometria da `MainWindow.axaml.cs`, la
classe `WeeklyPlanner.App/Interaction/WindowPlacementCalculator.cs` (`FitSizeToWorkingArea`,
`ClampPosition`) non è più richiamata da nessun punto di produzione: resta solo il proprio test
(`WindowPlacementCalculatorTests.cs`). Non causa bug, ma è un residuo del vecchio meccanismo che
vale la pena rimuovere insieme ai test, per evitare che un futuro contributor la scambi per codice
ancora attivo (lo stesso tipo di rischio già segnalato per `MoveAsync`/`PriorityDeadlineCalculator`
nella prima analisi).

### 2.2 Overload a 5 argomenti di `PriorityDeadlineCalculator.CalculateDueAt` non più usato in produzione

La classe ora è correttamente l'unica fonte di verità per la scadenza (vedi §0), ma l'overload
`CalculateDueAt(assignedAtUtc, priorityId, cardTypeId, priorities, deadlineRules)` (che risolve le
ore internamente tramite `ResolveDueHours`) è oggi richiamato solo dal proprio test; il codice di
produzione (`CardRepository`, `CardCatalogRepository`, `CardViewModel`) usa sempre la coppia
`ResolveDueHours` + `CalculateDueAt(assignedAt, hours, override)`. Non è un problema — anzi, è
naturale che l'API esponga entrambe le forme — ma se l'overload a 5 argomenti non serve più nemmeno
come comodità, si può rimuovere per ridurre la superficie da mantenere.

### 2.3 Nuovo ticker del "salvataggio relativo" (M3.12): nessun problema riscontrato

`BoardViewModel.PollBoardAsync` chiama `UpdateCardTimeIndicators()` a ogni tick, **prima** di
verificare lo stato di connessione e indipendentemente dal fatto che i dati siano cambiati; questo
aggiorna `_displayNow` (e quindi `LastSavedRelativeText`, `DueStatusText`, ecc.) su tutte le
`CardViewModel` anche quando il polling verso il database è sospeso per errore — esattamente il
comportamento dichiarato in ADR-0016 punto 4 ("il ticker è condiviso... anche quando il polling del
database è temporaneamente sospeso"). `GetLastSavedAt()` usa `UpdatedAtUtc` con fallback su
`CreatedAtUtc`, entrambi sempre valorizzati da `CardRepository` alla creazione, quindi il fallback è
solo difensivo. Non sono state trovate incoerenze in questa parte.

## 3. Cosa risulta solido (confermato in questo passaggio)

- Nessun riferimento residuo a `MoveAsync` in produzione, test o test double: la rimozione è stata
  pulita (`ICardRepository`, `CardRepository`, `BoardViewModelTestDoubles`, `CardRepositoryTests`
  tutti coerenti).
- Il nuovo test `AppSettingsServiceTests.Load_ignores_legacy_window_geometry_fields` verifica
  esplicitamente la retrocompatibilità con i vecchi file `settings.json` contenenti i campi di
  geometria ormai rimossi da `AppSettings`.
- Le colonne SQL restituite da `CardCatalogRepository` per il trasferimento delle card
  (`DefaultDueHours`, `OverrideDueHours`) corrispondono correttamente ai nomi di proprietà attesi da
  `CardTypeReassignmentRow` e dal nuovo `PriorityDeadlineCalculator.CalculateDueAt`.
- Il percorso di avvio della finestra principale (`App.CreateMainWindow`) ora configura
  `ShowActivated`, `ShowInTaskbar` e `WindowState.Maximized` una sola volta, prima della
  visualizzazione, senza più mutare `Width`/`Height`/`Position` né usare `Topmost` nel percorso di
  avvio normale (solo il flusso di onboarding conserva un singolo `Activate()` differito a bassa
  priorità, ma senza più dipendere da `Topmost` per funzionare).

## 4. Sezione storica: analisi originale (M3.11) mantenuta per riferimento

Le sottosezioni seguenti riportano il testo originale dei problemi ora risolti, utile come
riferimento su cosa è stato cambiato e perché.

### 4.1 (storico, risolto) `CardRepository.MoveAsync` non incrementava `Version`

`MoveToCellAsync` (usato dal drag&drop 2D reale) incrementava sempre `Version` sulla card spostata;
`MoveAsync` (spostamento in una sola colonna, precedente al modello swimlane) chiamava invece
`TouchCardAsync`, che aggiornava solo `UpdatedBy`/`UpdatedAtUtc` senza toccare `Version`. Il metodo
è stato rimosso interamente in M3.12.1 (vedi §0).

### 4.2 (storico, risolto) Tripla implementazione della regola di scadenza priorità/fascia

La formula "scadenza = data assegnazione + `PriorityTypeDeadlines.DueHours` (o, in mancanza,
`Priorities.DefaultDueHours`)" esisteva in tre punti indipendenti (una classe C# mai richiamata, una
query SQL inline in `CardRepository`, una terza reimplementazione in `CardCatalogRepository`). Da
M3.12.1 la classe C# è l'unica fonte di verità (vedi §0).

### 4.3 (storico, risolto) Rilevamento "modifiche esterne" basato su un oggetto `Card` incompleto

In `BoardViewModel.CommitEditAsync`, la gestione di `CardConcurrencyException` costruiva una `Card`
con `CardTypeId`/