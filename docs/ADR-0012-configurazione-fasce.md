# ADR-0012 — Configurazione delle fasce

## Stato

Accettata nella milestone M3.8.

## Contesto

Nel modello kanban, `CardTypes` non rappresenta più soltanto un attributo catalogato: ogni voce è
una fascia orizzontale della board. Il CRUD originario consentiva ancora
un default configurabile e impediva l’eliminazione delle tipologie usate, regole non più adatte al
modello a swimlane.

## Decisione

Le tipologie vengono presentate e gestite come **fasce**.

La fascia di sistema `Generica`:

- resta al `SortOrder = 0`;
- è sempre attiva e predefinita a livello dati;
- non partecipa al riordino;
- non può essere rinominata, disattivata o eliminata;
- può cambiare soltanto colore.

Le fasce utente occupano gli ordini contigui da 1 in avanti. Il contratto di salvataggio non espone
più `IsDefault`; una nuova fascia viene sempre persistita con `IsDefault = 0`.

Lo snapshot del catalogo include il numero di card assegnate a ogni fascia. Una fascia vuota può
essere eliminata direttamente. Per eliminare una fascia usata occorre indicare una fascia di
destinazione diversa, attiva e letta alla versione attesa.

Il trasferimento viene eseguito nella stessa transazione dell’eliminazione:

- `CardTypeId` viene sostituito;
- `ColumnId` e `SortOrder` non cambiano;
- `PriorityAssignedAtUtc` non cambia;
- `DueAtUtc` viene ricalcolato usando la priorità e le regole della fascia di destinazione;
- `UpdatedBy`, `UpdatedAtUtc` e `Version` vengono aggiornati;
- viene inserito un evento `TypeChanged` per ogni card, con utente, sessione, macchina e motivo
  `CardTypeDeleted`;
- le regole `PriorityTypeDeadlines` della fascia eliminata vengono rimosse dalla foreign key
  `ON DELETE CASCADE`.

Se almeno una card della fascia possiede un lock di modifica non scaduto, l’operazione viene
rifiutata. In questo modo una bozza aperta non cambia classificazione alle spalle dell’editor.

## Conseguenze

- schema SQLite invariato alla versione 5;
- la UI deve chiedere la destinazione soltanto quando la fascia contiene card;
- Generica è la destinazione proposta per prima;
- l’eliminazione è atomica: un errore di validazione, concorrenza, aggiornamento o audit annulla
  trasferimenti ed eliminazione;
- la posizione kanban e l’ordine relativo delle card restano stabili;
- la scadenza può cambiare legittimamente perché dipende dalla nuova fascia;
- il layout swimlane usa direttamente l'ordine delle fasce e i conteggi esposti dal catalogo.
