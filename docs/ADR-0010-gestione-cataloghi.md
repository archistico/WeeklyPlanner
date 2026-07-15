# ADR-0010 — Gestione dei cataloghi configurabili

## Stato

Accettata nella milestone M3.6. La parte relativa alle tipologie è parzialmente superata da ADR-0012 nella M3.8.

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
- la successiva M3.7 può consumare cataloghi attivi e mantenere visibili quelli inattivi già assegnati.
