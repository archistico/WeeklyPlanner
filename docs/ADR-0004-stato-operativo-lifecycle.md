# ADR-0004 — Stato operativo e chiusura coordinata

Data: 14 luglio 2026
Stato: accettata

## Contesto

WeeklyPlanner usa un file SQLite locale e due timer UI: polling della revisione e heartbeat dei lock.
La semplice presenza di retry non è sufficiente: l'utente deve sapere se il database è disponibile,
la board non deve essere svuotata da una lettura parzialmente fallita e la chiusura del processo non
deve interrompere il rilascio dei lock già avviato.

L'handler `Closed` viene eseguito quando la finestra è già chiusa e non garantisce che un cleanup
asincrono termini prima dell'uscita del processo.

## Decisione

1. La UI espone uno stato finito di connessione:
   `Connecting`, `Online`, `Recovering`, `Offline`, `Error`, `ShuttingDown`.
2. Gli errori vengono classificati tramite `SqliteErrorCode` e tipi di eccezione, senza analizzare
   messaggi localizzati.
3. Soltanto `SQLITE_BUSY` e `SQLITE_LOCKED` ricevono retry immediati brevi, sia in lettura sia in
   scrittura. Gli errori recuperabili restano soggetti al polling; quelli che richiedono intervento
   sospendono i tentativi automatici e attendono **Riprova ora**.
4. Le letture necessarie al primo caricamento vengono completate prima di sostituire le collection.
5. Durante un recupero vengono riutilizzati i ViewModel esistenti, preservando le bozze.
6. Dopo il primo caricamento riuscito, l'assenza del file viene trattata come indisponibilità: il
   recovery non può creare automaticamente un database vuoto sostitutivo.
7. Le scritture sono ammesse soltanto nello stato `Online`.
8. La prima richiesta `Window.Closing` viene annullata temporaneamente; il ViewModel ferma i timer,
   cancella le operazioni, attende il gate e rilascia i lock della sessione. Una seconda chiusura
   programmatica conclude la finestra dopo il cleanup.
9. Il rilascio finale resta best effort: se SQLite è indisponibile, il lease scade naturalmente.

## Conseguenze

- nessuna board vuota o parziale dopo un errore intermedio;
- nessuna perdita della bozza durante offline e recupero;
- nessuna sostituzione silenziosa della board con un nuovo database vuoto;
- stato operativo comprensibile e retry manuale disponibile;
- riduzione dei lock fantasma alla chiusura normale;
- i dettagli tecnici completi richiedono ancora il logging rolling previsto in M3;
- il modello non introduce sincronizzazione fra database locali differenti.
