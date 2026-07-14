# ADR-0003 — Editing protetto con lease e optimistic concurrency

- Stato: **Accettata**
- Data: 14 luglio 2026

## Contesto

Il polling locale può aggiornare una card mentre l'utente sta scrivendo. Prima della M1.3 titolo e
note erano collegati direttamente al modello persistito e il merge periodico poteva sostituire una
bozza non ancora salvata.

Due istanze dell'app possono inoltre aprire lo stesso database SQLite locale. Una transazione tenuta
aperta per tutta la durata dell'editing non è accettabile: bloccherebbe le altre scritture e
lascerebbe uno stato fragile in caso di crash.

## Decisione

WeeklyPlanner usa due protezioni complementari.

### Lease applicativo

`CardEditLocks` contiene al massimo un lock per card:

- `SessionId` identifica una specifica esecuzione dell'app;
- `UserName` e `MachineName` alimentano l'indicatore visuale;
- il lease dura 30 secondi;
- la sessione proprietaria lo rinnova ogni 10 secondi;
- salvataggio, annullamento e chiusura rilasciano il lock;
- un lock non rinnovato scade e può essere acquisito da un'altra sessione.

Il lease non usa una transazione SQLite lunga. Acquisizione, rinnovo e rilascio sono operazioni brevi
e atomiche.

### Concorrenza ottimistica

`Cards.Version` parte da 1 e viene incrementata a ogni salvataggio del contenuto. L'`UPDATE` riesce
soltanto se:

- la sessione possiede un lease ancora attivo;
- la versione coincide con quella letta all'inizio dell'editing.

Una differenza di versione produce un conflitto esplicito e la bozza locale non viene scartata.

### Merge della UI

Durante `IsEditing` il polling non modifica titolo, note, versione o posizione della card. Le altre
card continuano ad aggiornarsi. Una modifica o cancellazione esterna viene segnalata senza
sovrascrivere la bozza.

## Conseguenze

- l'editing simultaneo della stessa card è impedito fra istanze che aprono lo stesso file SQLite;
- l'interfaccia mostra `Stai modificando` oppure `<utente> sta modificando…`;
- crash e chiusure anomale non producono lock permanenti;
- spostamento ed eliminazione sono rifiutati mentre la card è in editing;
- il polling legge i lock attivi anche quando `BoardState.Revision` non cambia, perché la semplice
  scadenza temporale di un lease non genera una scrittura;
- database locali distinti su computer diversi non condividono né dati né lock. Una futura modalità
  realmente collaborativa richiederà un server o una sincronizzazione esplicita.
