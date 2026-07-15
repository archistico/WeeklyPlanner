# ADR-0009 — Cataloghi configurabili e storico funzionale delle card

## Stato

Accettata in M3.5.

## Contesto

Priorità, tipologie, scadenze e cronologia delle card devono diventare dati applicativi durevoli. Il
log tecnico JSONL introdotto in M3.3 non è adatto: ruota, ha retention limitata e non deve contenere
informazioni funzionali necessarie alla ricostruzione della vita di una card.

## Decisione

Lo schema v4 introduce tre cataloghi SQLite:

- `Priorities`;
- `CardTypes`;
- `PriorityTypeDeadlines` per le eccezioni alla durata predefinita.

Le priorità iniziali sono U, B, D e P. Le durate sono memorizzate in ore per rappresentare sia 72 ore
sia intervalli espressi in giorni. La regola D + Esame strumentale usa 1.440 ore, cioè 60 giorni.

Ogni card riceve:

- `StableId` immutabile;
- data di creazione e indicazione se stimata;
- priorità e tipologia opzionali;
- data di assegnazione della priorità;
- scadenza calcolata e salvata come snapshot.

Le modifiche alla configurazione non ricalcolano retroattivamente le card esistenti. La scadenza viene
ricalcolata quando cambia la priorità o la tipologia della singola card.

Lo storico funzionale è memorizzato in `CardEvents`. Ogni evento contiene identificativo stabile,
utente, sessione, computer, riepilogo e payload JSON versionato. L'inserimento dell'evento avviene
nella stessa transazione della mutazione della card.

Titolo e note non vengono duplicati nel payload dello storico: per questi campi viene registrato solo
quale parte è cambiata. Gli eventi restano consultabili dopo l'eliminazione grazie a `StableId` e a
`CardId` con `ON DELETE SET NULL`.

## Conseguenze

- una modifica non può essere confermata senza il relativo evento;
- un errore nello storico annulla anche la mutazione della card;
- lo storico sopravvive alla cancellazione;
- i cataloghi e le colonne incrementano `BoardState.Revision`;
- il CRUD visuale e la finestra cronologia possono essere costruiti sopra repository già testabili;
- il database passa dalla versione 3 alla versione 4 e beneficia del backup preventivo M3.4.
