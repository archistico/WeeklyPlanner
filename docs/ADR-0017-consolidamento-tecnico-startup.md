# ADR-0017 — Consolidamento tecnico e startup

- **Stato:** accettata
- **Data:** 15 luglio 2026
- **Milestone:** M3.12.1

## Contesto

La board veniva configurata da XAML, `App`, `MainWindow`, ripristino geometria, dispatcher e un
passaggio `Topmost`. La stessa base conservava inoltre un’API di movimento monodimensionale ormai
estranea al modello swimlane, più implementazioni indipendenti della regola di scadenza e un percorso
di conflitto che costruiva una `Card` incompleta.

## Decisione

1. La finestra principale viene configurata una sola volta prima della visualizzazione con
   `ShowActivated`, `ShowInTaskbar` e `WindowState.Maximized`.
2. Posizione, dimensione e stato della finestra non fanno parte di `AppSettings`; i campi legacy nel
   JSON sono ignorati.
3. Dopo l’onboarding viene effettuata una sola attivazione tramite dispatcher, senza `Topmost`.
4. Il solo contratto di movimento è `MoveToCellAsync`, basato su fascia, stato e indice locale.
5. `PriorityDeadlineCalculator` risolve durata e scadenza per tutti i percorsi produttivi.
6. I conflitti di versione sono rappresentati da uno stato esplicito del `CardViewModel`; la bozza non
   viene sovrascritta e l’interfaccia mostra un solo messaggio.
7. I confronti di titolo e note vengono calcolati una volta e riusati per persistenza e audit.

## Conseguenze

- avvio più prevedibile e conforme a una normale applicazione Windows;
- nessuna dipendenza dalla geometria salvata o dalla configurazione dei monitor precedente;
- superficie repository coerente con il kanban bidimensionale;
- scadenze uniformi durante creazione, modifica, movimento, trasferimento fascia e anteprima;
- conflitti più leggibili e meno fragili;
- nessuna nuova migrazione: lo schema resta v5.
