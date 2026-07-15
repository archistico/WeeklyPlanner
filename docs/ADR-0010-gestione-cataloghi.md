# ADR-0010 — Gestione dei cataloghi configurabili

## Stato

Accettata. La parte relativa alle tipologie è stata specializzata da ADR-0012; le priorità restano
governate da questa decisione.

## Decisione

Priorità e tipologie sono aggregate configurabili persistite in SQLite. Il repository applica
validazione, concorrenza ottimistica e transazioni; la UI non esegue SQL. Le regole di scadenza
specifiche per tipologia appartengono all'aggregato priorità e vengono sostituite nello stesso commit
dell'aggiornamento della priorità.

Le priorità assegnate alle card non vengono eliminate fisicamente: possono essere disattivate.
Per le tipologie/fasce, ADR-0012 introduce invece l’eliminazione con trasferimento atomico delle card.
Il valore predefinito resta configurabile soltanto per le priorità.

## Conseguenze

- nessuna migrazione aggiuntiva rispetto allo schema v4;
- ogni update/delete/reorder richiede la versione attesa;
- le modifiche incrementano la revisione della board tramite i trigger v4;
- il modello kanban consuma i cataloghi attivi e mantiene leggibili quelli inattivi già assegnati.
