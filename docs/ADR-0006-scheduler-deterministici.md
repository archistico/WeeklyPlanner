# ADR-0006 — Scheduler deterministici e callback seriali

- Stato: Accettata
- Data: 14 luglio 2026
- Milestone: M3.2

## Contesto

Polling della board e heartbeat dei lock erano avviati tramite due `DispatcherTimer`. L'adapter
impediva informalmente le sovrapposizioni con un flag, ma usava un handler `async void`, non esponeva
la callback attiva e non permetteva allo shutdown di attenderne esplicitamente la conclusione.

I test potevano richiamare manualmente le callback, ma non descrivevano in modo deterministico il
trascorrere degli intervalli, i tick concorrenti, la cancellazione durante una callback o la sequenza
completa di arresto.

## Decisione

La generazione del tick e l'esecuzione asincrona vengono separate.

`AvaloniaRecurringTaskScheduler` resta l'adapter della sorgente temporale UI, mentre
`AsyncRecurringTaskCoordinator` gestisce il lifecycle della callback:

- una sola esecuzione può essere attiva;
- un tick ricevuto durante una callback viene scartato;
- non viene accumulato un backlog da recuperare;
- la callback corrente è tracciata;
- gli errori inattesi sono osservati e conservati;
- `StopAsync` annulla e attende l'esecuzione corrente;
- il dispose è asincrono e idempotente.

Nei test, `ManualRecurringTaskScheduler` usa lo stesso coordinator e permette di avanzare un tempo
simulato. Le verifiche non dipendono dal dispatcher Avalonia né da attese temporali reali.

## Shutdown

`BoardViewModel.DisposeAsync` segue questa sequenza:

1. impedisce nuove operazioni;
2. annulla il token di lifetime;
3. arresta e attende polling e heartbeat;
4. attende l'operazione applicativa eventualmente in corso;
5. rilascia i lock della sessione;
6. dispone scheduler, gate e token source.

In questo modo nessuna callback periodica può accedere alla board o ai repository dopo il rilascio
della sessione.

## Conseguenze

### Positive

- lifecycle riproducibile nei test;
- nessuna sovrapposizione silenziosa;
- nessun task faulted non osservato nello scheduler;
- shutdown verificabile e più prevedibile;
- integrazione con logging e diagnostica completata da ADR-0007.

### Negative

- i tick arrivati durante una callback lenta vengono persi intenzionalmente;
- lo scheduler non tenta di recuperare cicli arretrati;
- lo shutdown attende una callback che ignori impropriamente la cancellazione.

Queste conseguenze sono accettate: polling e heartbeat rappresentano controlli periodici dello stato
corrente, non job che devono essere eseguiti un numero esatto di volte.
