# ADR-0005 — Composition root e dependency injection esplicita

- Stato: accettata
- Data: 14 luglio 2026
- Milestone: M3.1

## Contesto

La crescita del progetto aveva portato `BoardViewModel` a costruire direttamente connection factory,
repository, retry policy, sessione e `DispatcherTimer`. Questo rendeva la logica applicativa legata ad
Avalonia e SQLite anche nei test che non avevano bisogno di tali infrastrutture.

## Decisione

L'applicazione usa constructor injection e un composition root esplicito, senza introdurre un Generic
Host o un container esterno.

`ApplicationCompositionRoot` è l'unico punto autorizzato a costruire:

- servizio delle impostazioni;
- sessione applicativa;
- orologio;
- connection factory e repository SQLite;
- change detector;
- scheduler Avalonia;
- ViewModel.

`BoardViewModel` riceve tutte le dipendenze necessarie dal costruttore. `DispatcherTimer` è confinato
nell'adapter `AvaloniaRecurringTaskScheduler`; i test possono usare uno scheduler manuale.

## Conseguenze

### Positive

- nessuna apertura del database nei costruttori dei ViewModel;
- sessione e clock determinabili nei test;
- polling e heartbeat sostituibili senza dispatcher UI;
- costruzione runtime rintracciabile in un unico file;
- preparazione alla scomposizione successiva del lifecycle e della sincronizzazione.

### Negative

- il costruttore di `BoardViewModel` espone numerose dipendenze;
- l'adapter Avalonia contiene ancora il necessario bridge `async void` dell'evento timer;
- la completa determinizzazione delle esecuzioni periodiche è rimandata alla M3.2.

## Alternative escluse

- Service locator globale: nasconde le dipendenze e peggiora i test.
- Costruzione diretta nei ViewModel: mantiene l'accoppiamento precedente.
- Generic Host completo: non necessario per l'attuale applicazione desktop locale e aggiungerebbe
  infrastruttura senza un beneficio proporzionato in questa fase.
