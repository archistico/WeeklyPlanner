# ADR-0018 — Informazioni e cronologia della card

- **Stato:** Accettata
- **Data:** 15 luglio 2026
- **Milestone:** M3.13

## Contesto

Lo schema v4 ha introdotto `CardEvents` con identificativo stabile della card, metadati di sessione e
paginazione. Dopo il passaggio al kanban bidimensionale, queste informazioni devono essere consultabili
senza entrare in modifica, acquisire un lock o esporre il payload JSON tecnico.

## Decisione

Ogni card espone un'icona informazioni nel footer. L'icona apre una finestra modale centrata sulla
board e costruita da uno snapshot del modello corrente.

La finestra risolve fascia, stato e priorità dai cataloghi già caricati dalla board. Il lock viene
letto da `ICardEditLockRepository`; la cronologia viene letta da `ICardEventRepository` in pagine da
20 eventi, ordinate per identificativo decrescente e paginate tramite `beforeEventId`.

I riepiloghi persistiti restano la fonte della descrizione funzionale. Gli eventi `Moved` e
`Reordered` sono presentati rispettivamente come movimento fra celle e riordino locale. Il campo
`DataJson` non viene mostrato nella UI.

## Conseguenze

- nessuna nuova migrazione SQLite;
- la consultazione non altera lock, versione o revisione della board;
- la finestra resta utilizzabile anche se non esistono eventi storici;
- un errore nella lettura della cronologia non nasconde i metadati correnti della card;
- il numero di eventi letti per richiesta resta limitato e prevedibile.
