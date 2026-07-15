# ADR-0019 — Consolidamento kanban prima del packaging

- **Stato:** accettata
- **Data:** 15 luglio 2026
- **Milestone:** M3.14

## Contesto

Il modello kanban a swimlane è funzionalmente completo: cinque stati di sistema, fasce configurabili,
movimento bidimensionale, priorità, lock, storico e finestra informazioni. Prima del packaging MVP
serviva una fase dedicata a verificare il prodotto oltre i test unitari dei singoli componenti.

Le aree considerate critiche sono:

- caricamento del XAML reale senza desktop interattivo;
- comportamento di due istanze sullo stesso database SQLite;
- molte fasce e molte card;
- temi chiaro e scuro;
- accessibilità da tastiera e nomi per le tecnologie assistive;
- documentazione e acquisizione ripetibile degli screenshot;
- rimozione delle API o etichette residue non più coerenti con il kanban.

## Decisione

### Test UI headless

Il progetto di test usa `Avalonia.Headless.XUnit` nella stessa versione di Avalonia Desktop. I test
caricano `App.axaml` e le finestre reali, eseguono layout e binding sul dispatcher Avalonia e
verificano almeno:

- apertura della finestra principale;
- presenza delle cinque azioni di creazione;
- disponibilità di `BoardScrollViewer` e matrice swimlane;
- caricamento sotto tema chiaro e tema scuro.

Il backend Skia è abilitato per permettere una cattura documentale reale del frame, ma gli screenshot
vengono scritti soltanto quando è impostata la variabile `WEEKLYPLANNER_CAPTURE_UI=1`.

### Concorrenza fra istanze

Un test di integrazione crea due repository e due gestori di lock indipendenti sul medesimo file
SQLite. Il test verifica che:

- il lock acquisito dalla prima istanza sia visibile alla seconda;
- la seconda istanza non possa sostituire un lease attivo;
- dopo il rilascio possa acquisire il lock;
- una copia obsoleta venga rifiutata tramite `CardConcurrencyException`;
- snapshot, revisione e lock riflettano lo stato condiviso.

### Scala e prestazioni

La suite include uno smoke test con 30 fasce, cinque stati e 1.500 card. Non è un benchmark assoluto:
il limite di dieci secondi serve a intercettare regressioni macroscopiche nella costruzione della
proiezione swimlane, non a certificare prestazioni hardware indipendenti.

### Accessibilità

Le azioni prive di testo devono avere tooltip e `AutomationProperties.Name`. Titolo, note, priorità,
maniglia di trascinamento e riepilogo della priorità espongono nomi o istruzioni accessibili. Il
riepilogo priorità può essere attivato anche con `Invio` o `Spazio`.

I target compatti principali vengono portati almeno a 30×30 px; le azioni `+` nelle intestazioni
restano compatte ma non inferiori a 32×28 px. Il tema chiaro usa un accento più scuro per mantenere il
contrasto del testo bianco. La suite verifica un rapporto minimo 4,5:1 per le coppie testuali
principali di entrambi i temi.

### Documentazione e screenshot

Il manuale utente descrive flusso operativo, tastiera, lock, priorità, cronologia e collaborazione fra
istanze. `scripts/capture-ui.ps1` genera gli screenshot chiaro/scuro dal XAML reale con dati fittizi in
memoria, senza toccare il database dell'utente.

### Pulizia

L'overload di `PriorityDeadlineCalculator.CalculateDueAt` che accettava interi cataloghi viene
rimosso perché non aveva chiamanti produttivi. Rimangono le due primitive realmente usate:

- `ResolveDueHours`;
- `CalculateDueAt` con ore predefinite e override opzionale.

I riferimenti ai giorni della settimana restano soltanto nelle migrazioni e nella documentazione
storica necessaria a spiegare l'upgrade. Non devono comparire nell'interfaccia attiva.

## Conseguenze

- La suite richiede i pacchetti `Avalonia.Headless.XUnit` e `Avalonia.Skia` 11.3.18.
- I test UI possono essere eseguiti in CI senza desktop visibile.
- La cattura screenshot è ripetibile e non dipende da dati reali.
- I test vengono eseguiti senza parallelizzazione per evitare condivisione non deterministica dello
  stato Avalonia globale.
- Lo schema SQLite resta alla versione 5.
