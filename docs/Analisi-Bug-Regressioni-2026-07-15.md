# WeeklyPlanner — Analisi bug, regressioni ed errori di progettazione

Data analisi: 15 luglio 2026 — Milestone corrente: **M3.11**

Questa analisi copre `WeeklyPlanner.Core` (repository, migrazioni, resilienza) e
`WeeklyPlanner.App` (ViewModel, servizi, interazione), letti insieme a README, roadmap e ADR
per verificare la coerenza tra comportamento documentato e codice effettivo. Non è stata eseguita
una compilazione o un'esecuzione dei test: le osservazioni sono basate su lettura statica del
codice e confronto incrociato con la suite `WeeklyPlanner.Tests`.

## Sintesi

Il codice è nel complesso curato: transazioni esplicite attorno a ogni scrittura, controllo
ottimistico via `Version`, gestione dei lock applicativi, classificazione degli errori SQLite e
migrazioni protette da backup/rollback sono tutte implementate coerentemente con quanto descritto
nei documenti di progetto. I problemi trovati sono soprattutto di natura architetturale
(duplicazione di regole di business, codice non più raggiungibile dal path di produzione) più che
crash o corruzioni di dati; un punto (§1.3) è invece un bug comportamentale concreto, seppure a
impatto limitato.

## 1. Problemi con impatto concreto

### 1.1 `CardRepository.MoveAsync` non incrementa `Version`, `MoveToCellAsync` sì

File: `WeeklyPlanner.Core/Repositories/CardRepository.cs`

`MoveToCellAsync` (usato dal drag&drop 2D reale) incrementa sempre `Version` sulla card spostata:

```csharp
UPDATE Cards SET CardTypeId = @CardTypeId, DueAtUtc = @DueAtUtc,
    UpdatedBy = @UpdatedBy, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
WHERE Id = @CardId;
```

`MoveAsync` (spostamento in una sola colonna, precedente al modello swimlane) invece chiama
`TouchCardAsync`, che aggiorna solo `UpdatedBy`/`UpdatedAtUtc` e **non tocca `Version`**. Nessun
test in `CardRepositoryTests.cs` verifica `Version` dopo `MoveAsync` (verificano solo l'ordine
delle card), quindi l'asimmetria non è coperta.

Il rischio pratico è oggi contenuto perché `BoardViewModel` chiama solo `MoveToCellAsync` (verificato
via ricerca nel codice: `MoveAsync` è invocato esclusivamente dai test). Ma `MoveAsync` resta parte
dell'interfaccia pubblica `ICardRepository`, è documentato e testato come se fosse una funzionalità
attiva: qualunque nuovo chiamante (o un eventuale ripristino del vecchio path 1D) produrrebbe righe
la cui `Version` non riflette uno spostamento reale, indebolendo silenziosamente la garanzia di
concorrenza ottimistica descritta nel README ("controllo ottimistico tramite `Cards.Version`").

**Suggerimento**: allineare `TouchCardAsync`/`MoveAsync` a `MoveToCellAsync` (incrementare sempre
`Version`), oppure rimuovere `MoveAsync` se è definitivamente superato dal modello a fasce.

### 1.2 Tripla implementazione della regola di scadenza priorità/fascia

La formula "scadenza = data assegnazione + `PriorityTypeDeadlines.DueHours` (o, in mancanza,
`Priorities.DefaultDueHours`)" esiste in **tre punti indipendenti**, sincronizzati solo "a mano":

1. `WeeklyPlanner.Core/Repositories/PriorityDeadlineCalculator.cs` — classe C# dedicata, con una
   propria suite di test (`PriorityDeadlineCalculatorTests.cs`), ma **non referenziata da nessun
   altro punto del codice di produzione**: è codice morto.
2. `CardRepository.CalculateDueAtAsync` — stessa regola reimplementata come SQL inline
   (`COALESCE(rule.DueHours, priority.DefaultDueHours)`), usata da `CreateAsync`/`UpdateAsync`.
3. `CardCatalogRepository.CalculateReassignedDueAt` — terza reimplementazione in C#, usata solo
   durante l'eliminazione di una fascia con trasferimento delle card.

Se in futuro la regola cambia (arrotondamento, gestione DST, un nuovo fallback), il rischio è di
aggiornarne solo una delle tre varianti e ottenere scadenze diverse a seconda dell'operazione che
ha toccato la card (creazione/modifica priorità vs. eliminazione della fascia di provenienza).

**Suggerimento**: eliminare `PriorityDeadlineCalculator` se non verrà mai richiamato, oppure farlo
diventare l'unica fonte di verità e far sì che sia `CardRepository` sia `CardCatalogRepository` lo
richiamino invece di duplicare la query.

### 1.3 Rilevamento "modifiche esterne" basato su un oggetto `Card` incompleto

File: `WeeklyPlanner.App/ViewModels/BoardViewModel.cs`, gestione di `CardConcurrencyException` in
`CommitEditAsync` (righe ~536-561):

```csharp
catch (CardConcurrencyException ex)
{
    card.RefreshFromModel(new Card
    {
        Id = card.Model.Id,
        ColumnId = card.Model.ColumnId,
        Title = card.Model.Title,
        Notes = card.Model.Notes,
        SortOrder = card.Model.SortOrder,
        CreatedBy = card.Model.CreatedBy,
        UpdatedBy = card.Model.UpdatedBy,
        UpdatedAtUtc = card.Model.UpdatedAtUtc,
        Version = ex.ActualVersion,
    });
    ...
}
```

L'oggetto `Card` creato al volo lascia `CardTypeId`, `PriorityId`, `PriorityAssignedAtUtc` e
`DueAtUtc` ai valori di default (`null`). `CardViewModel.RefreshFromModel`, quando la card è in
editing, confronta questi campi con quelli del modello corrente per decidere se marcare
`HasExternalChanges = true`:

```csharp
updatedModel.PriorityId != Model.PriorityId ||
updatedModel.CardTypeId != Model.CardTypeId ||
...
```

Poiché l'oggetto fittizio ha sempre `PriorityId`/`CardTypeId` nulli, il confronto risulta quasi
sempre vero per qualunque card che abbia già una priorità o una fascia assegnata — cioè quasi
sempre — indipendentemente dal fatto che la scrittura concorrente abbia davvero cambiato priorità
o fascia. L'effetto visibile è che, dopo un conflitto di concorrenza, la card mostra **due
messaggi sovrapposti e solo in parte coerenti**: il testo di errore salvataggio ("Conflitto
rilevato: la bozza è conservata...") e, tramite `LockStatusText`, anche "⚠ La card è cambiata
altrove: annulla per ricaricare" — quest'ultimo derivato da un confronto che non riflette la realtà
dei dati.

**Suggerimento**: nel blocco `catch (CardConcurrencyException ...)`, rileggere la card persistita
reale (ad es. un `GetByIdAsync` esposto dal repository) invece di costruire un `Card` parziale, così
il confronto in `RefreshFromModel` opera su dati completi e realistici.

## 2. Duplicazione di responsabilità e codice non più raggiunto

### 2.1 Controllo "card non in editing" applicato due volte, con esiti diversi

`CardViewModel.CanDrag` disabilita silenziosamente il trascinamento se `IsEditing` è vero (la UI
semplicemente non permette il drag). `CardRepository.EnsureCardIsNotLockedAsync` — richiamato da
`DeleteAsync`, `MoveAsync` e `MoveToCellAsync` — applica la stessa regola lanciando
`CardEditLockException` se **una qualunque sessione** detiene il lock, anche se il chiamante non è
quella sessione. Le due regole coincidono oggi, ma vivono in due livelli diversi con due
meccanismi di segnalazione diversi (proprietà bindata vs. eccezione runtime): se la UI un giorno
rilassasse la regola (es. permettendo il drag mentre si edita un'*altra* card nella stessa cella),
il repository continuerebbe comunque a bloccare silenziosamente via eccezione generica, con un
messaggio pensato per un caso diverso ("La card X è in modifica da...").

### 2.2 API "morte" ereditate dal modello pre-swimlane

Sia `ICardRepository.MoveAsync` (§1.1) sia `PriorityDeadlineCalculator` (§1.2) sono resti
funzionanti e testati del modello ad una dimensione precedente a M3.7-M3.9, ma non più raggiunti
dal codice applicativo dopo l'introduzione delle fasce. Non sono errori in sé, ma aumentano la
superficie che un nuovo contributor può scambiare per "path di produzione attivo" — con il rischio
di investire tempo a mantenerli allineati (o peggio, di introdurre bug fix solo lì, credendo di
correggere il comportamento reale dell'app).

**Suggerimento**: se non è previsto un loro riutilizzo a breve, marcarli esplicitamente come
legacy/deprecati (commento + eventuale `[Obsolete]`) o rimuoverli insieme ai relativi test.

## 3. Osservazioni minori

- **`CardRepository.UpdateAsync` / `BuildContentSummary`**: `titleChanged` e `notesChanged` vengono
  calcolati due volte con la stessa logica (una volta per `contentChanged`, una seconda volta dentro
  `BuildContentSummary`). Innocuo, ma un refactor che tocchi solo una delle due copie potrebbe far
  divergere il messaggio di storico dal comportamento reale.
- **`WindowPlacementCalculator.ClampPosition`**: il minimo verticale è fissato esattamente a
  `workingArea.Y`, senza il margine "pixel visibili" applicato invece sull'asse orizzontale. È
  probabilmente intenzionale (mantenere la barra del titolo sempre raggiungibile), ma l'asimmetria
  non è commentata e potrebbe essere "corretta" per errore in futuro scambiandola per un bug.
- **Migrazione v5 (`0005_kanban_model.sql`)**: assume che le colonne storiche `Backlog`/`Lunedì..Domenica`
  abbiano sempre Id 0..7 (hardcoded `CASE WHEN card.ColumnId = 0 THEN 0 ELSE 1 END`). L'assunzione è
  vera oggi perché lo schema v1 seeda sempre quegli Id, ma non c'è un controllo esplicito nello
  script v5 che lo verifichi: un database v1 alterato manualmente romperebbe la migrazione senza un
  messaggio d'errore chiaro.

## 4. Lancio di MainWindow: non massimizza correttamente e resta in sottofondo

File: `WeeklyPlanner.App/Views/MainWindow.axaml.cs` (`OnWindowOpened`, `ActivateWindowAtStartup`),
`WeeklyPlanner.App/App.axaml.cs` (`CreateMainWindow`).

Il comportamento atteso (da README/ADR) è: "la finestra principale viene creata massimizzata, con
attivazione iniziale esplicita e un passaggio `Topmost` temporaneo rimosso subito dopo l'apertura."
Il codice tenta di implementarlo così:

1. `App.CreateMainWindow` e `ConfigureApplicationServices` impostano `WindowState = Maximized`,
   `ShowActivated = true`, `ShowInTaskbar = true` **prima** di mostrare la finestra: fin qui corretto.
2. Nell'handler `Opened` (`OnWindowOpened`), che scatta **dopo** che la finestra è già stata mostrata
   (quindi già maximized), il codice però riassegna comunque `Width`, `Height` e — se esiste una
   posizione salvata nelle impostazioni — anche `Position`, calcolati da
   `WindowPlacementCalculator.FitSizeToWorkingArea`/`ClampPosition`.
3. Solo dopo, `ActivateWindowAtStartup()` richiama `EnsureWindowIsMaximized()`