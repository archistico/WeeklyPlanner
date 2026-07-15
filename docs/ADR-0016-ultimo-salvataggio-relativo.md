# ADR-0016 — Ultimo salvataggio relativo della card

- **Stato:** accettata
- **Milestone:** M3.12
- **Data:** 2026-07-15

## Contesto

La card esponeva un floppy verde soltanto dopo un'operazione eseguita nella sessione corrente. Il
feedback era transitorio, non spiegava quando fosse avvenuta l'ultima persistenza e veniva perso dopo
un aggiornamento esterno. Aggiungere un timer a ogni card avrebbe inoltre moltiplicato callback e
risorse in una board con molte fasce.

## Decisione

1. La data di riferimento è il metadato persistito `Cards.UpdatedAtUtc`; per righe legacy prive di
   tale valore viene usato `CreatedAtUtc` come fallback.
2. Il footer mostra un floppy verde e un testo relativo quando la card è in stato persistito:
   `adesso`, minuti, ore o giorni.
3. Il tooltip mostra data e ora locali complete al secondo e, quando disponibile, l'utente che ha
   effettuato l'ultima modifica.
4. Il ticker è condiviso: il polling della board aggiorna `_displayNow` di tutti i `CardViewModel`.
   Nessuna card crea un proprio timer.
5. Il footer distingue in modo mutuamente esclusivo:
   - bozza dirty;
   - salvataggio in corso;
   - errore di persistenza;
   - stato salvato con timestamp relativo.
6. Un merge dovuto a modifiche esterne sostituisce `UpdatedAtUtc`; il tempo relativo e il tooltip si
   riallineano automaticamente al nuovo valore.
7. Non viene introdotta alcuna nuova colonna o migrazione: lo schema SQLite resta alla versione 5.

## Conseguenze

- Il feedback è stabile anche dopo riavvio o aggiornamenti provenienti da un'altra istanza.
- Il costo dell'aggiornamento temporale è lineare e centralizzato in un solo tick della board.
- La precisione del testo relativo è limitata all'intervallo di polling configurato, compreso fra 3 e
  60 secondi, sufficiente per etichette espresse al minuto.
- Gli errori continuano a conservare il messaggio tecnico completo nel tooltip, mentre il footer resta
  compatto.
