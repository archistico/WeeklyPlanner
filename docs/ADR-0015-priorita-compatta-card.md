# ADR-0015 — Priorità compatta e bozza unificata della card

## Stato

Implementata nella M3.11; verifica build, test e runtime richiesta.

## Contesto

Lo schema v4 aveva già introdotto priorità configurabili, regole di scadenza per fascia, campi
`PriorityId`, `PriorityAssignedAtUtc`, `DueAtUtc` e l'evento `PriorityChanged`. Fino alla M3.10 tali
dati non erano però modificabili dalla card nella board.

Un editor separato della priorità avrebbe richiesto un secondo lock o un salvataggio indipendente,
creando il rischio di persistere titolo e note senza priorità, oppure di perdere una parte della bozza
in caso di conflitto. La rappresentazione permanente di una ComboBox, inoltre, avrebbe appesantito
visivamente ogni card.

## Decisione

La priorità è parte della stessa bozza protetta di titolo e note:

1. `CardViewModel` espone un catalogo di opzioni con una voce esplicita `Nessuna`;
2. durante la modifica la ComboBox aggiorna soltanto la bozza locale;
3. `IsDirty`, annullamento, validazione, lock e optimistic concurrency includono anche `PriorityId`;
4. `CreateEditedModel` passa la priorità scelta alla stessa chiamata `CardRepository.UpdateAsync`;
5. il repository imposta `PriorityAssignedAtUtc`, calcola `DueAtUtc` e registra `PriorityChanged`
   nella transazione già usata per la card;
6. la regola specifica `(PriorityId, CardTypeId)` prevale sulla scadenza standard della priorità;
7. fuori modifica la ComboBox è sostituita da un riepilogo compatto e trasparente con codice, nome e tempo residuo; il click sul riepilogo acquisisce il lock e apre immediatamente l’elenco;
8. la bozza resta attiva fino a Salva, Annulla o `Esc`: la perdita del focus non produce più un commit implicito;
9. durante una bozza il polling aggiorna modelli e lock senza ricreare la proiezione swimlane, preservando controlli, focus e popup;
10. le priorità inattive non vengono proposte, salvo quella già assegnata alla card, che resta leggibile;
11. un cambio remoto di priorità o scadenza durante l'editing segnala conflitto senza sovrascrivere la
   bozza locale.

Il tempo residuo della M3.11 è calcolato rispetto all'orologio condiviso quando la card viene caricata o
aggiornata. La M3.12 introdurrà un ticker condiviso per mantenerlo aggiornato senza creare un timer per
ogni card.

Le azioni di creazione vengono spostate nelle cinque intestazioni operative. Ogni azione crea una card
nella fascia di sistema Generica e nello stato dell'intestazione selezionata; l'eventuale priorità
predefinita attiva viene applicata subito.

## Conseguenze

- nessuna nuova migrazione: lo schema SQLite resta v5;
- priorità, titolo e note vengono salvati o annullati insieme;
- il calcolo della scadenza e lo storico restano responsabilità del repository;
- la board rimane compatta quando le card non sono in modifica;
- le priorità inattive non possono essere assegnate di nuovo ma non scompaiono dalle card esistenti;
- la creazione è disponibile in tutti gli stati, sempre nella fascia Generica;
- la M3.12 potrà aggiornare i testi relativi usando `UpdateDisplayNow` su un ticker unico.
