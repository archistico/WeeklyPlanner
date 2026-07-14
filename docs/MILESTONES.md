# WeeklyPlanner — Milestone operative

## M1.3.1 — Pacchetto e ingresso in editing verificabili

- archivio distribuito con i file del progetto direttamente alla radice;
- badge visibile `M1.3.1` nella finestra per escludere l'avvio di copie precedenti;
- editor in sola lettura finché il lease non è stato acquisito;
- test sullo stato read-only prima e dopo `BeginEdit`;
- resta valida la protezione della bozza dal polling introdotta in M1.3.


Ultimo aggiornamento: 14 luglio 2026.

## Regole di sviluppo

Ogni milestone deve:

1. avere uno scope ridotto e verificabile;
2. aggiungere o aggiornare i test pertinenti;
3. compilare senza warning (`TreatWarningsAsErrors=true`);
4. aggiornare README, roadmap e decisioni architetturali;
5. non dichiararsi completata prima di `restore`, `build` e `test` reali;
6. evitare refactor non necessari insieme a modifiche funzionali.

## Stato

| Milestone | Stato | Contenuto |
|---|---|---|
| M0.1.2 — Baseline riproducibile | **Validata** | build riuscita, test passati, toolchain centralizzata, runtime SQLite corretto, migrazioni embedded, lifecycle controllato e drag&drop Avalonia 11.3 |
| ADR-0002 — Persistenza locale | **Accettata** | SQLite su disco locale, nessun server e nessuna sincronizzazione fra PC |
| M1.1.1 — SQLite locale affidabile | **Validata** | schema v2, revisione monotona, polling delle cancellazioni, percorsi legacy normalizzati, build/test e avvio runtime riusciti |
| M1.2 — Riordino atomico | **Validata** | compilazione e test passati; create/delete/move transazionali, normalizzazione completa dei `SortOrder`, rollback e drop prima/dopo una card |
| M1.3 — Editing protetto | **Implementata, verifica richiesta** | bozza separata, polling non distruttivo, lease con heartbeat, indicatore utente, `Cards.Version`, conflitti e test multi-istanza |
| M2 — CRUD e UX minima | Pianificata | eliminazione visibile e confermata, scroll verticale, editor controllato e stato di salvataggio |
| M3 — Resilienza e osservabilità | Pianificata | logging locale, classificazione errori, retry coerente e test ViewModel |
| M4 — Packaging MVP locale | Pianificata | publish Windows, backup documentato, smoke test e pacchetto distribuibile |

## M1.1.1 — SQLite locale affidabile

### Decisione di persistenza

- database SQLite sul filesystem locale;
- percorso predefinito in `%LOCALAPPDATA%\WeeklyPlanner\Data\weeklyplanner.db`;
- creazione automatica della directory;
- percorsi UNC e relativi rifiutati;
- nessuna promessa di sincronizzazione fra computer;
- ADR multiutente mantenuta come possibile evoluzione futura.

### Schema e polling

- tabella singleton `BoardState(Id, Revision)`;
- trigger `AFTER INSERT`, `AFTER UPDATE` e `AFTER DELETE` su `Cards`;
- incremento della revisione nella stessa transazione della modifica;
- aggiornamento automatico da database v1 a v2;
- polling indipendente dagli orologi dei processi;
- cancellazioni osservabili da una seconda istanza locale.

### Percorsi legacy

- espansione delle variabili d'ambiente;
- rimozione delle virgolette esterne;
- conversione di una cartella in `<cartella>\weeklyplanner.db`;
- diagnostica con percorso realmente utilizzato;
- test di regressione per settings e connection factory.

## M1.2 — Riordino atomico

### Repository

- `CreateAsync` assegna il `SortOrder` nel repository e non accetta l'ordine proposto dalla UI;
- prima di una creazione vengono ricompattati eventuali buchi o duplicati legacy nella colonna;
- `MoveAsync` riceve un indice di inserimento e ricalcola l'intera sequenza interessata;
- spostamento e riordino nella stessa colonna vengono eseguiti in una sola transazione non differita;
- spostamento fra colonne ricompatta sorgente e destinazione nella stessa transazione;
- `DeleteAsync` elimina e ricompatta la colonna nella stessa transazione;
- tutti i record riordinati dalla stessa operazione ricevono lo stesso timestamp UTC;
- update/delete su card inesistente producono un errore esplicito;
- le operazioni senza modifiche reali non avanzano inutilmente la revisione.

### ViewModel e drag&drop

- la UI non applica più un riordino ottimistico prima del commit;
- dopo il commit viene riletto lo stato persistito;
- in caso di errore la collection visuale resta coerente con il database;
- il drop sulla metà superiore di una card inserisce prima;
- il drop sulla metà inferiore inserisce dopo;
- il drop sullo spazio libero inserisce in fondo;
- la stessa semantica vale dentro la medesima colonna e fra colonne differenti.

### Test

- append atomico e indipendente dal `SortOrder` del chiamante;
- riparazione di buchi e duplicati prodotti da versioni precedenti;
- riordino verso testa, centro e coda nella stessa colonna;
- spostamento fra colonne con sequenze contigue;
- indice oltre la coda ricondotto all'ultima posizione;
- no-op senza incremento della revisione;
- colonna di destinazione inesistente senza modifiche parziali;
- trigger di test che forza un errore a metà riordino e verifica il rollback completo;
- cancellazione con ricompattazione;
- timestamp uniforme per tutte le card coinvolte.

## Criteri di chiusura M1.2

Eseguire dalla radice:

```powershell
.\scripts\verify.ps1
```

Poi verificare manualmente:

1. creare almeno quattro card nella stessa colonna;
2. spostare l'ultima prima della seconda e riavviare l'app;
3. spostare una card in un'altra colonna, prima e dopo una card esistente;
4. trascinare una card in fondo a una colonna;
5. spostare una card verso l'alto e verso il basso nella stessa colonna;
6. aprire una seconda istanza locale e verificare che il nuovo ordine venga rilevato dal polling;
7. confermare che dopo il riavvio non esistano duplicati o buchi nell'ordine visuale.


## M1.3 — Editing protetto

### Stato della card

- `CardViewModel` conserva separatamente valori persistiti, bozza e versione attesa;
- `IsEditing` protegge titolo, note e posizione dal merge periodico;
- `IsDirty` abilita il salvataggio soltanto in presenza di modifiche;
- modifiche o cancellazioni esterne impostano uno stato di conflitto senza perdere la bozza;
- `Esc` e il pulsante **Annulla** ripristinano i dati persistiti;
- il passaggio fra titolo e note non conclude l'editing;
- l'uscita completa dalla card salva automaticamente.

### Lock e versioning

- migrazione schema v3 con `Cards.Version` e `CardEditLocks`;
- un solo lease attivo per card;
- durata 30 secondi e heartbeat ogni 10 secondi;
- acquisizione e rinnovo atomici tramite transazioni brevi;
- lock scaduti recuperabili automaticamente;
- rilascio alla chiusura della sessione in modalità best effort;
- update del contenuto condizionato a sessione proprietaria e versione attesa;
- move/delete rifiutati durante un editing attivo;
- trigger dei lock collegati a `BoardState.Revision`;
- rilettura periodica dei lock anche senza cambio revisione per osservare la scadenza naturale.

### Test

- migrazione v1/v2 → v3 e idempotenza;
- acquisizione, contesa, rinnovo, scadenza e rilascio dei lock;
- impossibilità di rinnovare un lease già scaduto;
- salvataggio senza lock rifiutato;
- incremento della versione dopo il salvataggio;
- versione obsoleta rifiutata senza sovrascrittura;
- move/delete rifiutati durante il lock;
- polling delle variazioni dei lock fra due istanze;
- bozza non sovrascritta dal refresh del ViewModel;
- indicatore e sola lettura per lock posseduto da un'altra sessione.

### Smoke test manuale richiesto

1. aprire due istanze sullo stesso database locale;
2. entrare nel titolo di una card nella prima istanza;
3. verificare nella seconda il testo `Emilie sta modificando…` e i campi in sola lettura;
4. scrivere lentamente nella prima istanza per oltre due intervalli di polling;
5. verificare che titolo e note non cambino durante la digitazione;
6. passare dal titolo alle note senza perdere il lock;
7. uscire dalla card e verificare il salvataggio nell'altra istanza;
8. ripetere usando `Esc` e verificare l'annullamento;
9. chiudere forzatamente l'istanza che detiene il lock e verificare lo sblocco entro circa 30 secondi.
