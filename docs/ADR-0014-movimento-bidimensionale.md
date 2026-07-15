# ADR-0014 — Movimento bidimensionale atomico

## Stato

Validata nella M3.10 con compilazione e test completamente riusciti.

## Contesto

La M3.9 ha introdotto la matrice `CardTypeId × ColumnId`, ma il repository continuava a ricevere un
indice riferito all'intera colonna tecnica. La UI doveva quindi tradurre l'indice visuale della cella
nell'ordine globale della colonna e non poteva cambiare fascia in modo affidabile.

Uno spostamento di fascia può inoltre cambiare `DueAtUtc`, perché la scadenza dipende dalla coppia
priorità/fascia. Stato, fascia, ordine, scadenza e audit non possono essere salvati in passaggi separati:
un errore intermedio produrrebbe una card in una posizione parziale o uno storico incompleto.

## Decisione

`ICardRepository.MoveToCellAsync` riceve:

- identificativo della card;
- `targetColumnId`;
- `targetCardTypeId`;
- `targetCellIndex`, relativo alle sole card della cella prima della rimozione della sorgente;
- utente che esegue l'operazione.

Il repository apre una transazione SQLite immediata e:

1. verifica esistenza, lock, stato e fascia;
2. rifiuta una fascia inattiva se diversa da quella corrente;
3. calcola l'indice finale locale con la stessa semantica prima/dopo del drag&drop;
4. rimuove la card dalla sorgente e la inserisce nella cella di destinazione;
5. ricostruisce l'ordine tecnico delle colonne raggruppando le card secondo l'ordine delle fasce;
6. ricompatta `SortOrder` senza buchi;
7. ricalcola `DueAtUtc` solo quando cambia fascia, conservando `PriorityAssignedAtUtc`;
8. aggiorna autore, timestamp e `Version` della card spostata;
9. registra `Reordered` per la stessa cella oppure un singolo evento `Moved` con entrambe le dimensioni.

L'evento contiene nomi e identificativi di fascia e stato, indici locali e tecnici e scadenza precedente
e successiva. Titolo e note non vengono inclusi nell'audit.

La UI passa direttamente la `SwimlaneCellViewModel` di destinazione. Il drop sulla colonna TIPOLOGIA
non produce una cella e viene quindi rifiutato. Gli indicatori esistenti rappresentano inserimento prima,
dopo o in fondo.

La M3.10 ha consolidato inizialmente la creazione in Generica / BACKLOG. La M3.11 mantiene la fascia Generica ma sposta l’azione nelle cinque intestazioni operative, consentendo di scegliere lo stato iniziale.

## Conseguenze

- cambio stato, cambio fascia e cambio simultaneo sono una sola operazione atomica;
- la UI non dipende più da `SwimlaneMoveIndexResolver`, che viene rimosso;
- un errore nell'audit annulla anche ordine, fascia, stato, scadenza e versione;
- le fasce inattive non ricevono nuove card, ma le card esistenti possono ancora cambiare stato;
- lo schema SQLite resta v5;
- `SortOrder` rimane tecnico per colonna, ma viene mantenuto in forma canonica coerente con le swimlane.
