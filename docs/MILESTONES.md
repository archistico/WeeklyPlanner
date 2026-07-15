# WeeklyPlanner — Milestone operative

## M3.2 — Scheduler deterministici e lifecycle verificabile

- nuovo `AsyncRecurringTaskCoordinator` per serializzare le callback periodiche;
- nessuna sovrapposizione fra due tick dello stesso scheduler;
- tick ricevuti durante un'esecuzione attiva scartati senza creare backlog;
- rimozione dell'handler `async void` dal timer Avalonia;
- `IRecurringTaskScheduler` convertito a lifecycle asincrono con `StopAsync` e `IAsyncDisposable`;
- shutdown coordinato che annulla e attende polling e heartbeat prima del cleanup dei lock;
- scheduler manuale con avanzamento deterministico degli intervalli nei test;
- test di polling lento, cambio intervallo, errori consecutivi, recovery, errore strutturale,
  perdita del lease e dispose durante una callback attiva;
- 114 test dichiarati e validati su Windows;
- comportamento UI e schema SQLite v3 invariati.

## M3.1 — Composizione applicativa e dependency injection

- composition root unico in `ApplicationCompositionRoot`;
- costruzione centralizzata di settings, ViewModel, SQLite, repository e scheduler;
- `BoardViewModel` configurato esclusivamente tramite constructor injection;
- astrazioni per settings, inizializzazione database, sessione, orologio e scheduler;
- `DispatcherTimer` confinato nell'adapter `AvaloniaRecurringTaskScheduler`;
- session ID e nome macchina forniti da una singola `ApplicationSession`;
- nessuna apertura o creazione del database durante la costruzione dei ViewModel;
- test con initializer, repository, clock e scheduler controllabili;
- 103 test dichiarati e validati su Windows;
- comportamento UI e schema SQLite v3 invariati.

## M2.3 — Impostazioni e rifinitura della sessione

- finestra Impostazioni accessibile dall'header;
- modifica di nome utente, polling, tema e percorso database;
- tema Sistema/Chiaro/Scuro applicato immediatamente;
- intervallo di polling aggiornato senza riavvio;
- nome e database protetti durante editing e operazioni in corso;
- cambio database differito al successivo avvio;
- apertura delle cartelle database e dati applicativi;
- persistenza iniziale della geometria finestra, successivamente rimossa in M3.12.1;
- cursore `SizeAll` durante il pan con tasto centrale;
- versione e milestone centralizzate in `Directory.Build.props` e lette dall'assembly;
- test di settings, blocchi della sessione, tema, polling, cartelle e geometria;
- schema SQLite invariato alla versione 3.

## M2.2.3 — Pan orizzontale e allineamento autore

- testo `di <utente>` in corsivo;
- margine sinistro della firma allineato al contenuto di titolo e note;
- pan orizzontale della board tenendo premuto il tasto centrale del mouse;
- asse verticale invariato durante il pan;
- scrollbar orizzontale mantenuta come modalità alternativa;
- drag&drop delle card con il tasto sinistro e scroll verticali delle colonne invariati;
- test unitari del calcolo dell’offset;
- schema invariato alla versione 3.

## M2.2.1 — Rifinitura visuale della board

- rimozione del pulsante esteso `+ Aggiungi card` dal fondo delle colonne;
- azione compatta `+` con icona card accanto al nome del giorno;
- titolo delle card leggermente più grande;
- autore mantenuto a sinistra nel footer;
- cestino allineato all’estrema destra;
- floppy verde a sinistra del cestino dopo un salvataggio riuscito;
- feedback testuale mantenuto per salvataggio in corso ed errori;
- schema invariato alla versione 3.

## M2.2 — Drag&drop rifinito e accessibilità da tastiera

- indicatore di inserimento prima/dopo la card o in fondo alla colonna;
- rifiuto dei drop che non producono alcun cambiamento;
- `Alt+↑/↓` per il riordino nella colonna;
- `Alt+←/→` per lo spostamento nella colonna adiacente;
- mantenimento della posizione verticale quando possibile;
- ripristino del focus sulla card dopo il commit;
- bordo di focus e metadati accessibili per card e pulsanti a icona;
- test puri della pianificazione degli indici e degli spostamenti no-op;
- schema invariato alla versione 3.

## M2.1.1 — Rifinitura pulsante eliminazione

- piccolo pulsante con icona cestino nel footer della card;
- allineamento a sinistra accanto all'autore;
- conferma inline e protezioni della M2.1 invariate.

## M2.1 — CRUD completo e feedback dell'editor

- eliminazione visibile con conferma inline e non modale;
- conferma disponibile soltanto su card non modificate e non bloccate;
- titolo obbligatorio, limite di 160 caratteri e normalizzazione nel repository;
- indicatori per salvataggio in corso, successo ed errore;
- bozza conservata quando il salvataggio fallisce;
- scroll verticale indipendente per ogni colonna;
- azione di aggiunta sempre visibile;
- schema invariato alla versione 3.

Ultimo aggiornamento: 15 luglio 2026.

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
| M1.3.1 — Editing protetto | **Validata** | build/test e runtime verificati; bozza separata, polling non distruttivo, lease, indicatore utente e optimistic concurrency |
| M1.4 — Stato operativo e lifecycle | **Validata** | stati connessione, classificazione errori, retry letture, recupero non distruttivo e chiusura coordinata |
| M2.1.1 — Rifinitura eliminazione | **Validata** | build, test e prova manuale riusciti; icona cestino compatta nel footer della card |
| M2.1 — CRUD e feedback editor | **Validata tramite M2.1.1** | eliminazione inline, validazione titolo, feedback di salvataggio e scroll per colonna |
| M2.2 — Tastiera e drop feedback | **Validata** | build e test passati; linea di inserimento, no-op rifiutati, `Alt` + frecce, focus e metadati accessibili |
| M2.2.1 — Rifinitura visuale | **Validata tramite M2.2.3** | aggiunta nell’header colonna, titolo più grande, floppy verde e cestino a destra |
| M2.2.3 — Pan orizzontale | **Validata** | build, test e prova manuale riusciti; autore corsivo e pan con tasto centrale |
| M2.3 — Impostazioni | **Validata** | configurazione runtime e tema; la geometria persistita viene rimossa in M3.12.1 |
| M3.1 — Composizione e DI | **Validata** | composition root, constructor injection, sessione/orologio/scheduler astratti e test senza SQLite |
| M3.2 — Timer deterministici | **Validata** | callback seriali, stop asincrono, tempo simulato e test completi del lifecycle |
| M3.2.1 — Feedback persistenza | **Implementata, verifica richiesta** | floppy per tutte le modifiche della card e footer riallineato |
| M3.3 — Logging e diagnostica | **Validata tramite M3.3.4** | logging locale, correlazione errori e informazioni runtime |
| M3.4 — Migrazioni protette | **Validata** | backup coerente, controlli d'integrità e rollback |
| M3.5 — Cataloghi e storico | **Validata** | schema v4, priorità, tipologie, scadenze ed eventi card |
| M3.6.1 — Configurazione cataloghi | **Validata** | CRUD priorità e tipologie con regole di concorrenza |
| M3.7 — Modello kanban | **Validata** | schema v5, cinque stati, Generica e snapshot atomico |
| M3.8 — Configurazione fasce | **Validata** | Generica protetta, conteggi e trasferimento atomico in eliminazione |
| M3.9 — Layout swimlane | **Accettata nella prova manuale** | matrice TIPOLOGIA × stato, scroll globale, pan e movimento nella fascia |
| M3.10 — Drag&drop bidimensionale | **Validata** | compilazione e 225 test passati; cambio fascia/stato atomico e indice locale |
| M3.11 — Priorità compatta | **Validata** | build, test e verifica UX riusciti; bozza persistente, badge, scadenza e avvio attivo |
| M3.12 — Ultimo salvataggio relativo | **Implementata** | floppy verde, tempo relativo e ticker condiviso |
| M3.12.1 — Consolidamento tecnico e startup | **Implementata, verifica richiesta** | startup semplice, API 2D unica, scadenze centralizzate e conflitti espliciti |
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
- `MoveToCellAsync` riceve fascia, stato e indice locale e ricalcola atomicamente le sequenze interessate;
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


## M1.4 — Stato operativo e lifecycle affidabile

### Stato operativo

- la UI espone connessione, recupero, offline, errore e chiusura;
- l'ultimo polling riuscito è visibile nell'intestazione;
- le operazioni lunghe espongono un testo attività;
- **Riprova ora** forza un tentativo senza attendere il polling successivo;
- le scritture non partono quando il database non è online.

### Resilienza

- `DatabaseFailureClassifier` usa `SqliteErrorCode` e non il testo localizzato;
- `BUSY` e `LOCKED` hanno backoff dedicato per letture e scritture;
- percorso/I/O, permessi, spazio, corruzione e schema producono messaggi distinti;
- una lettura fallita non svuota le collection;
- il recupero riutilizza i ViewModel esistenti e conserva le bozze;
- dopo il primo caricamento riuscito, la scomparsa del file viene trattata come indisponibilità e non genera un database vuoto sostitutivo;
- il polling ritenta automaticamente solo gli errori recuperabili; gli errori strutturali attendono **Riprova ora**.

### Lifecycle

- `Window.Closing` annulla temporaneamente la prima richiesta di chiusura;
- polling e heartbeat vengono fermati prima del cleanup;
- le operazioni correnti ricevono cancellazione e il gate viene atteso;
- tutti i lock della sessione vengono rilasciati prima della chiusura effettiva;
- in caso di errore del cleanup, i lease restano comunque a scadenza.

### Test

- classificazione di contention, indisponibilità, corruzione, permessi, schema e fallback unknown;
- protezione contro la ricreazione silenziosa del database durante il recupero;
- rilascio di tutti e soli i lock appartenenti a una sessione;
- restano validi tutti i test di migrazione, repository, polling, riordino ed editing.

### Smoke test manuale richiesto

1. avviare l'app e verificare il badge `M1.4` e lo stato verde **Database online**;
2. aprire una card e lasciare una bozza non salvata;
3. rendere temporaneamente indisponibile il file o la cartella del database;
4. verificare che la board e la bozza restino visibili e che compaia lo stato offline/errore;
5. ripristinare il database e usare **Riprova ora**;
6. verificare il ritorno online senza perdita della bozza;
7. aprire due istanze, modificare una card nella prima e chiuderla normalmente;
8. verificare nella seconda che il lock venga rimosso subito, senza attendere 30 secondi.

## M2.1 — CRUD completo e feedback dell'editor

### Eliminazione

- comando di eliminazione visibile sulle card non in editing e non bloccate; nella M2.1.1 è rappresentato da un’icona cestino compatta;
- primo comando che apre una conferma inline nella stessa card;
- **Annulla** ripristina immediatamente la card senza scritture;
- conferma chiusa automaticamente quando la card viene bloccata da un'altra istanza;
- eliminazione effettiva delegata alla transazione atomica già testata in `CardRepository`;
- una sola conferma aperta alla volta nella board.

### Validazione e salvataggio

- titolo obbligatorio e limite di `Card.MaxTitleLength = 160`;
- contatore caratteri e messaggio di validazione durante l'editing;
- `CanSave` richiede titolo valido e nessun salvataggio già in corso;
- trim del titolo in `CreateEditedModel` e nel repository;
- stato per card: `Salvataggio…`, `Salvata`, errore;
- campi temporaneamente read-only durante il commit;
- errore di salvataggio senza perdita della bozza.

### Layout

- board orizzontale invariata;
- `Grid` a tre righe per ogni colonna;
- scroll verticale confinato all'elenco card;
- header e pulsante **+ Aggiungi card** esclusi dallo scroll.

### Test

- titolo vuoto non salvabile nel ViewModel;
- titolo normalizzato nel modello editato;
- transizioni saving/error/saved;
- conferma eliminazione disabilitata durante lock o editing;
- repository che rifiuta titoli vuoti o oltre il limite senza avanzare la revisione;
- repository che persiste il titolo normalizzato.

### Smoke test manuale richiesto

1. avviare l'app e verificare il badge `M2.1.1`;
2. creare abbastanza card da superare l'altezza di una colonna;
3. verificare che scorra soltanto la colonna e che **+ Aggiungi card** resti visibile;
4. cancellare una card, prima annullando e poi confermando;
5. svuotare il titolo e verificare messaggio, contatore e pulsante **Salva** disabilitato;
6. salvare un titolo con spazi iniziali/finali e verificare che venga normalizzato;
7. osservare `Salvataggio…` e `Salvata`;
8. aprire due istanze e verificare che una card bloccata non mostri il comando di eliminazione.


## Criteri di chiusura M2.2

1. eseguire `dotnet build` e `dotnet test`;
2. avviare l'app e verificare il badge `M2.2`;
3. trascinare una card sopra e sotto altre card verificando la linea di inserimento;
4. trascinare nello spazio libero di una colonna e verificare l'indicatore in coda;
5. provare un drop sulla posizione originale e verificare che non venga accettato;
6. selezionare una card con `Tab` e usare `Alt+↑/↓`;
7. usare `Alt+←/→` e verificare che il focus resti sulla card spostata;
8. verificare che le scorciatoie non spostino la card mentre si scrive in titolo o note.

## Criteri di chiusura M2.2.1

1. eseguire `dotnet build` e `dotnet test`;
2. avviare l'app e verificare il badge `M2.2.1`;
3. verificare il pulsante compatto `+` con icona card accanto a ogni nome di colonna;
4. verificare che il vecchio pulsante esteso non sia più presente in fondo alle colonne;
5. verificare che il titolo delle card sia leggermente più grande;
6. modificare e salvare una card, verificando la floppy verde alla sinistra del cestino;
7. verificare autore a sinistra e cestino allineato all'estrema destra;
8. verificare che `Salvataggio…` e gli errori continuino a essere mostrati come testo.


## Criteri di chiusura M2.2.3

1. eseguire `dotnet build` e `dotnet test`;
2. avviare l'app e verificare il badge `M2.2.3`;
3. verificare che `di <utente>` sia corsivo e allineato con titolo e note;
4. tenere premuto il tasto centrale e trascinare verso sinistra e destra;
5. verificare che il pan modifichi soltanto lo scorrimento orizzontale;
6. verificare che drag&drop con il tasto sinistro e scroll verticali continuino a funzionare;
7. verificare che la scrollbar orizzontale resti utilizzabile.

## Criteri di chiusura M2.3

1. eseguire `dotnet build` e `dotnet test` senza warning o errori;
2. verificare il badge `M2.3` e il titolo ricavati dall'assembly;
3. cambiare tema e polling e verificarne l'applicazione immediata;
4. aprire le cartelle database e dati applicativi;
5. modificare il nome utente a sessione libera e verificare il nuovo nome nell'header;
6. aprire una card in editing e verificare che nome e database siano bloccati;
7. cambiare database e verificare il messaggio di riavvio, senza scollegare la board corrente;
8. verificare che il riavvio apra sempre la board massimizzata senza ripristinare geometrie precedenti;
9. verificare il cursore di pan durante il trascinamento con tasto centrale.

## M3.2.1 — Feedback completo di persistenza

- floppy verde dopo inserimento, salvataggio, spostamento fra colonne e riordino;
- tooltip specifico: `Card inserita`, `Card salvata`, `Card spostata`, `Ordine aggiornato`;
- rimozione automatica del feedback precedente quando arriva una modifica esterna;
- padding esplicito e condiviso fra titolo, note e testo autore;
- 119 test dichiarati;
- nessuna modifica allo schema SQLite.


## M3.3 — Logging, gestione globale degli errori e diagnostica

### Logging

- coda asincrona bounded e writer singolo;
- formato JSON Lines;
- file giornalieri con sequenza e rotazione a 5 MB;
- retention best effort di 14 giorni;
- fallback non bloccante quando la cartella log non è scrivibile;
- nessun titolo o nota delle card nei record;
- proprietà con chiavi sensibili redatte automaticamente.

### Correlazione degli errori

- generazione di riferimenti brevi `WP-XXXXXX`;
- stesso riferimento nel messaggio UI e nel log tecnico;
- stack trace disponibile soltanto nel log;
- gestione globale delle eccezioni UI, AppDomain e Task non osservate.

### Diagnostica

- finestra raggiungibile dall'intestazione;
- versione applicazione, milestone, .NET, Avalonia, OS e architettura;
- utente, computer, sessione e stato della board;
- percorso, dimensione, schema e stato del database;
- percorso impostazioni e log, stato logger e ultimo file scritto;
- copia negli appunti e apertura delle cartelle;
- conteggi aggregati senza contenuto delle card.

### Test

- serializzazione JSON e redazione delle proprietà sensibili;
- rolling per dimensione;
- retention;
- errore filesystem non propagato;
- formato del riferimento errore;
- diagnostica database disponibile, assente e non valida;
- provider e ViewModel diagnostico;
- logging delle operazioni con soli identificativi;
- correlazione fra errore UI e log;
- composition root con logger sostituibile.

### Criteri di chiusura

1. eseguire `dotnet build` e `dotnet test`;
2. verificare il badge `M3.3.3`;
3. compiere operazioni sulla board e controllare il file JSONL;
4. verificare che titolo e note non compaiano nel log;
5. aprire Diagnostica, copiare il testo e aprire le cartelle;
6. simulare un errore database e verificare lo stesso riferimento in UI e log;
7. rendere non scrivibile la destinazione log e verificare che la board continui a funzionare.


## M3.3.1 — Correzione build logging

- Corretto l’overload non valido di `LastIndexOf` in `FileAppLogger.ParseSequence`.
- Nessuna variazione funzionale o di schema.

## M3.3.2 — Compatibilità analyzer xUnit

- Corrette due asserzioni secondo la regola `xUnit2031`, usando `Assert.Single(collection, predicate)`.
- Verificata l’assenza di altri `Assert.Single(...Where(...))` nella suite.
- Nessuna variazione funzionale o di schema.
- Criterio di accettazione: `dotnet build` e `dotnet test` completati senza warning o errori.


## M3.3.3 — Test versione non fragile

- il test della milestone legge il valore atteso da `AssemblyMetadataAttribute`;
- nessuna stringa di milestone precedente è più duplicata nella suite;
- `ApplicationVersionInfo.Milestone` e `WindowTitle` restano verificati rispetto ai metadati centralizzati;
- nessuna modifica funzionale o allo schema SQLite;
- criterio di accettazione: tutti i 143 test completati senza errori.


## M3.3.4 — Terminazione affidabile del processo

- shutdown esplicito del lifetime desktop dopo il cleanup asincrono della board;
- attesa globale limitata a 2 secondi;
- rimozione del Flush sincrono separato dall'evento Exit;
- worker del logger protetto da errori di scrittura inattesi;
- test del disposer bounded;
- schema SQLite invariato alla versione 3.

## M3.4 — Protezione preventiva delle migrazioni

### Sicurezza dell'upgrade

- tutte le migrazioni mancanti vengono selezionate e validate prima di modificare lo schema;
- i database esistenti vengono sottoposti a `PRAGMA integrity_check` e `PRAGMA foreign_key_check`;
- prima della prima migrazione viene creato un backup coerente tramite `SqliteConnection.BackupDatabase`;
- anche il backup viene verificato prima di procedere;
- dopo l'ultima migrazione viene ripetuto il controllo di integrità.

### Rollback

- se una migrazione intermedia o il controllo finale falliscono, la connessione viene chiusa;
- il backup sostituisce il database parzialmente migrato;
- il file ripristinato viene verificato nuovamente;
- la versione schema ripristinata deve coincidere con quella iniziale;
- il backup preventivo resta disponibile dopo il rollback;
- se anche il restore fallisce, l'errore conserva separatamente eccezione di migrazione e di recovery.

### Backup e retention

- cartella predefinita `%LOCALAPPDATA%\WeeklyPlanner\Backups\Migrations`;
- nomi file con versione origine, versione destinazione, timestamp UTC e identificativo univoco;
- retention degli ultimi cinque backup;
- pulizia best-effort, incapace di annullare una migrazione già riuscita;
- temporanei e sidecar SQLite rimossi durante il ripristino;
- database nuovi parziali eliminati dopo una creazione fallita.

### Test

- creazione di una copia coerente con dati e versione originali;
- retention con conservazione obbligatoria del backup corrente;
- restore di un database parzialmente modificato;
- upgrade protetto da schema v1 e v2;
- schema v3 corrente senza backup superfluo;
- migrazione v3 simulata fallita con rollback alla v1;
- integrity check fallito prima del backup;
- recovery fallito con conservazione delle due cause;
- rimozione del database nuovo parziale;
- classificazione operativa di corruzione e migrazione ripristinata.

### Criteri di chiusura

1. eseguire `dotnet build` e `dotnet test` senza warning o errori;
2. verificare il badge `M3.4`;
3. aggiornare un database v1 e verificare la presenza del backup `v1-to-v3`;
4. aggiornare un database v2 e verificare la presenza del backup `v2-to-v3`;
5. riaprire un database v3 e verificare che non venga creato un nuovo backup;
6. eseguire i test di migrazione fallita e verificare versione, dati e integrità del file ripristinato;
7. verificare che lo schema SQLite applicativo resti alla versione 3.

## M3.5 — Fondazione cataloghi e storico

**Obiettivo:** introdurre il modello dati necessario a priorità, tipologie, scadenze e cronologia
senza anticipare il relativo CRUD visuale.

### Implementazione

- schema SQLite v4;
- `Priorities`, `CardTypes` e `PriorityTypeDeadlines`;
- seed U/B/D/P e tipologie iniziali;
- regola D + Esame strumentale = 60 giorni;
- `StableId` immutabile e metadati di creazione sulle card;
- priorità, tipologia, data di assegnazione e scadenza snapshot;
- `CardEvents` persistente con payload JSON versionato;
- eventi atomici per create, update, cambio priorità/tipologia, move, reorder e delete;
- backfill delle card esistenti con evento `Imported`;
- trigger di revisione per cataloghi e colonne;
- repository di lettura per cataloghi e storico paginato;
- contesto audit della sessione iniettato nel repository;
- 176 casi di test stimati considerando `Fact` e `InlineData`.

### Vincoli

- titolo e note non vengono duplicati nello storico;
- lo storico sopravvive all'eliminazione della card;
- un errore di audit annulla la mutazione;
- nessuna modifica visuale alla board;
- backup preventivo M3.4 obbligatorio prima dell'upgrade.

### Verifica

```powershell
dotnet build
dotnet test
dotnet run --project WeeklyPlanner.App
```

Smoke test database:

1. avviare un database v3 e verificare il backup `v3-to-v4`;
2. controllare schema v4 in Diagnostica;
3. creare, modificare, spostare, riordinare ed eliminare una card;
4. verificare tramite SQLite che gli eventi siano presenti e privi di titolo/note nel `DataJson`;
5. verificare `PRAGMA integrity_check` e `PRAGMA foreign_key_check`.


## M3.6 — Gestione priorità e tipologie

### Funzioni

- finestra Configura board aperta dalle Impostazioni;
- CRUD completo di priorità e tipologie;
- riordino atomico con concorrenza ottimistica;
- default unico e stato attivo/disattivo;
- colori tipologia con anteprima;
- regole di scadenza per tipologia salvate come aggregato della priorità;
- eliminazione bloccata per valori usati dalle card.

### Test

- creazione, update e normalizzazione;
- duplicati e validazioni;
- sostituzione delle regole;
- conflitti di versione;
- riordino e compattazione;
- eliminazione di voci libere e rifiuto delle voci usate;
- ViewModel della finestra e composition root.

### Criteri di chiusura

1. `dotnet build` senza warning o errori;
2. `dotnet test` completamente verde;
3. badge `M3.6`;
4. CRUD manuale di una priorità e una tipologia;
5. verifica della regola D + Esame strumentale;
6. prova di cancellazione di una voce usata;
7. schema SQLite ancora alla versione 4.


## M3.6.1 — Correzione test regole di scadenza

### Correzione

Il caricamento della configurazione seleziona la prima priorità ordinata, cioè `U`.
Il test dell’override a 60 giorni appartiene invece alla priorità `D` e ora la seleziona esplicitamente prima dell’asserzione.

### Criteri di chiusura

1. `dotnet build` senza warning o errori;
2. `dotnet test` completamente verde;
3. badge `M3.6.1`;
4. nessuna modifica allo schema SQLite v4 o ai CRUD.

## M3.7 — Modello kanban e migrazione v5

**Obiettivo:** sostituire il modello settimanale con il fondamento dati del kanban a swimlane,
senza introdurre ancora il nuovo layout visuale.

### Implementazione

- schema SQLite v5;
- colonne di sistema BACKLOG, TODO, IN PROGRESS, TESTING e DONE;
- chiavi stabili `backlog`, `todo`, `in_progress`, `testing`, `done`;
- proprietà `SystemKey` e `IsSystem` su colonne e tipologie;
- tipologia di sistema Generica con chiave `generic`, promuovendo senza duplicati un’eventuale tipologia omonima già esistente;
- tipologia obbligatoria per ogni card;
- assegnazione automatica a Generica nel repository;
- migrazione Backlog→BACKLOG e giorni→TODO;
- ordine deterministico delle card migrate;
- eventi `WorkflowMigrated` e `TypeMigrated`;
- protezione delle colonne di sistema;
- `BoardSnapshotRepository` con lettura transazionale completa;
- cataloghi e revisione esposti dal `BoardViewModel`;
- riordino delle card vicine senza aggiornare `UpdatedAtUtc` e `UpdatedBy`;
- ADR-0011.

### Test

- schema nuovo v5;
- upgrade da v1, v2, v3 e v4;
- backup v4→v5;
- cinque colonne e chiavi di sistema;
- backfill Generica;
- eventi di migrazione;
- `CardTypeId` obbligatorio;
- colonne di sistema e tipologia Generica protette;
- snapshot completo e lock scaduti esclusi;
- tipo Generica assegnato automaticamente in creazione;
- timestamp delle card vicine preservato;
- integrity check e foreign key check.

### Criteri di chiusura

1. `dotnet build` senza warning o errori;
2. `dotnet test` completamente verde;
3. badge `M3.7`;
4. backup `v4-to-v5` presente durante un upgrade;
5. cinque colonne visibili nella UI transitoria;
6. card dei giorni migrate in TODO;
7. card senza tipologia assegnate a Generica;
8. database integro e senza violazioni di foreign key.

## M3.8 — Configurazione delle fasce

**Obiettivo:** adattare il catalogo delle tipologie alle regole operative delle future swimlane,
senza introdurre ancora il layout bidimensionale della board.

### Implementazione

- terminologia UI aggiornata da tipologie a fasce;
- conteggio delle card per fascia nello snapshot di configurazione;
- rimozione del default configurabile dal contratto e dall’editor delle fasce;
- Generica sempre prima, attiva e protetta, con solo il colore modificabile;
- riordino limitato alle fasce utente con ordini contigui da 1;
- eliminazione diretta delle fasce vuote;
- selezione di una destinazione attiva per le fasce usate;
- trasferimento transazionale delle card con stato e ordine invariati;
- ricalcolo della scadenza in base alla fascia di destinazione;
- evento `TypeChanged` per ogni card trasferita;
- contesto audit utente/sessione/macchina collegato alla configurazione;
- blocco dell’eliminazione in presenza di lock di modifica attivi;
- ADR-0012;
- schema SQLite invariato alla versione 5.

### Test

- salvataggio e normalizzazione delle fasce;
- protezioni di Generica;
- riordino senza Generica e compattazione dopo delete;
- conteggio delle card;
- trasferimento verso una fascia scelta;
- conservazione di `ColumnId`, `SortOrder` e `PriorityAssignedAtUtc`;
- ricalcolo `DueAtUtc`;
- evento `TypeChanged` e contesto audit;
- rollback senza destinazione e su errore di audit;
- rifiuto con lock attivo;
- ViewModel della finestra e composition root aggiornati.

### Criteri di chiusura

1. `dotnet build` senza warning o errori;
2. `dotnet test` completamente verde;
3. badge `M3.8`;
4. Generica sempre prima e non riordinabile;
5. eliminazione di una fascia vuota;
6. eliminazione di una fascia usata con trasferimento atomico;
7. stato e ordine delle card invariati;
8. evento `TypeChanged` presente per ogni card trasferita;
9. database SQLite v5 integro e senza violazioni di foreign key.

## M3.9 — Layout kanban a swimlane

**Obiettivo:** rendere visibile il modello bidimensionale introdotto nello schema v5 senza
anticipare il contratto di movimento bidimensionale della M3.10.

### Implementazione

- intestazioni TIPOLOGIA, BACKLOG, TODO, IN PROGRESS, TESTING e DONE;
- nuova proiezione `SwimlaneViewModel` / `SwimlaneCellViewModel`;
- cinque celle fisse per ogni fascia;
- card instradate tramite `CardTypeId` e `ColumnId`;
- riuso degli stessi `CardViewModel` posseduti dalle colonne tecniche;
- Generica sempre prima;
- fasce attive ordinate secondo catalogo;
- fasce inattive mostrate soltanto quando contengono card;
- indicatore e banda con il colore della fascia;
- altezza condivisa fra le celle della riga mediante una singola Grid;
- celle vuote senza testo segnaposto;
- unico ScrollViewer verticale e orizzontale;
- pan bidimensionale con tasto centrale;
- aggiunta transitoria nelle celle di Generica;
- drag&drop e `Alt` + frecce ripristinati per riordino e cambio stato nella fascia corrente;
- traduzione degli indici visuali della cella verso il contratto tecnico per colonna;
- cambio fascia riservato alla M3.10;
- ADR-0013;
- schema SQLite invariato alla versione 5.

### Test

- ordine e visibilità delle fasce;
- cinque celle per fascia e chiavi di workflow corrette;
- instradamento delle card nella cella corretta;
- fallback a Generica per dati legacy senza tipologia;
- fascia inattiva con card visibile e fascia inattiva vuota nascosta;
- `CardViewModel` riutilizzato dopo refresh e cambio fascia/stato;
- XAML con intestazioni, binding alle swimlane, larghezza elastica e scroll globale;
- indici di drop corretti in presenza di card di fasce diverse nella stessa colonna;
- pan su entrambi gli assi.

### Criteri di chiusura

1. `dotnet build` senza warning o errori;
2. `dotnet test` completamente verde;
3. badge `M3.9`;
4. matrice completa e allineata;
5. scroll verticale unico;
6. pan bidimensionale funzionante;
7. drag&drop e tastiera disponibili nella fascia corrente;
8. nessuna card nascosta per fascia inattiva;
9. nessuna duplicazione di stato UI;
10. database SQLite v5 integro.

## M3.10 — Drag&drop bidimensionale

**Obiettivo:** rendere la posizione `(CardTypeId, ColumnId, indice nella cella)` un contratto atomico
fra UI e repository, eliminando la traduzione M3.9 verso l'indice globale della colonna.

### Implementazione

- nuovo `ICardRepository.MoveToCellAsync` con indice locale della cella;
- cambio stato, cambio fascia e cambio simultaneo;
- riordino nella stessa cella con semantica prima/dopo/in fondo;
- ordinamento tecnico canonico per fascia e ricompattazione di `SortOrder`;
- ricalcolo di `DueAtUtc` al cambio fascia, senza modificare `PriorityAssignedAtUtc`;
- incremento della `Version` della card spostata;
- audit `Reordered` nella stessa cella e `Moved` negli altri casi;
- evento `Moved` con fascia, stato, indici e scadenze precedenti e successivi;
- rollback dell'intera transazione se fallisce l'audit;
- rifiuto delle nuove assegnazioni verso fasce inattive;
- drop sulla colonna TIPOLOGIA rifiutato;
- indicatori prima/dopo/in fondo conservati;
- `Alt` + Su/Giù per riordino;
- `Alt` + Sinistra/Destra per cambio stato;
- `Ctrl` + `Alt` + Su/Giù per cambio fascia attiva;
- creazione definitiva soltanto in Generica / BACKLOG;
- rimozione di `SwimlaneMoveIndexResolver`;
- ADR-0014;
- schema SQLite invariato alla versione 5.

### Test

- movimento bidimensionale e ricalcolo della scadenza;
- indice locale con altre fasce presenti nella stessa colonna;
- no-op senza revisione o audit;
- rollback completo su errore dell'evento;
- rifiuto della fascia inattiva;
- proiezione ViewModel dopo cambio simultaneo;
- creazione disponibile solo nell'incrocio definitivo;
- XAML con drop sulle celle e comando di creazione corretto.

### Criteri di chiusura

1. `dotnet build` senza warning o errori;
2. `dotnet test` completamente verde;
3. badge `M3.10`;
4. cambio fascia e stato simultaneo;
5. riordino locale indipendente dalle altre fasce;
6. audit leggibile e atomico;
7. rollback completo su errore;
8. nessuna nuova card in fasce inattive;
9. creazione solo in Generica / BACKLOG;
10. database SQLite v5 integro.



## M3.11 — Priorità compatta sulla card

**Obiettivo:** rendere priorità e scadenza visibili e modificabili senza appesantire la card né
separarle dalla bozza protetta di titolo e note.

### Implementazione

- `CardPriorityOptionViewModel` con opzione esplicita Nessuna;
- ComboBox mostrata soltanto durante l'editing;
- badge compatto con codice, nome, tooltip e tempo residuo fuori modifica;
- priorità inclusa in `IsDirty`, annullamento e `CreateEditedModel`;
- priorità inattiva corrente mantenuta nel catalogo della card;
- anteprima della scadenza basata sulla regola specifica della fascia;
- persistenza tramite `CardRepository.UpdateAsync`, con `PriorityChanged`;
- cinque pulsanti di creazione nelle intestazioni operative;
- nuova card sempre in Generica, nello stato selezionato;
- priorità predefinita attiva applicata in creazione;
- finestra principale aperta sempre massimizzata e attivata davanti alle altre applicazioni;
- attivazione iniziale semplificata in M3.12.1 senza `Topmost`;
- editing mantenuto fino a Salva, Annulla o `Esc`, senza commit su `LostFocus`;
- click sul riepilogo della priorità con apertura diretta della ComboBox;
- polling senza ricostruzione delle swimlane mentre esistono bozze attive;
- spazio inferiore della board aumentato a 160 px;
- ADR-0015;
- schema SQLite invariato alla versione 5.

### Test

- bozza, annullamento e modello editato della priorità;
- opzione Nessuna;
- regola di scadenza specifica della fascia;
- badge in sola lettura e priorità inattiva corrente;
- cinque azioni di creazione e stato iniziale corretto;
- priorità predefinita sulle nuove card;
- binding XAML di ComboBox, badge, pulsanti, massimizzazione e spazio inferiore;
- assenza del vecchio handler `LostFocus`;
- attivazione iniziale e apertura diretta del menu priorità.

### Criteri di chiusura

1. `dotnet build` senza warning o errori;
2. `dotnet test` completamente verde;
3. badge `M3.11`;
4. priorità modificata nella stessa bozza della card;
5. scadenza e audit coerenti;
6. creazione dalle cinque intestazioni;
7. editing stabile fino a Salva, Annulla o `Esc`;
8. ComboBox priorità aperta direttamente dal riepilogo;
9. finestra massimizzata e attiva all’avvio;
10. ultima card completamente visibile;
11. database SQLite v5 integro.


## M3.12 — Ultimo salvataggio relativo

**Obiettivo:** rendere visibile e stabile lo stato di persistenza di ogni card senza introdurre timer
per istanza o nuove colonne nel database.

### Implementazione

- timestamp letto da `UpdatedAtUtc`, con fallback a `CreatedAtUtc`;
- testo relativo `adesso`, minuti, ore e giorni;
- tooltip con data locale completa al secondo e autore dell'ultima modifica;
- floppy verde permanente nello stato salvato;
- indicatori separati per dirty, saving ed error;
- aggiornamento di tutte le card dal ticker condiviso del polling;
- aggiornamento automatico dopo merge di modifiche esterne;
- ADR-0016;
- schema SQLite invariato alla versione 5.

### Test

- bucket temporali relativi;
- tooltip esatto;
- transizioni fra saved, dirty, saving ed error;
- ticker condiviso attraverso `PollingScheduler`;
- refresh esterno del timestamp;
- binding XAML dei quattro stati.

### Criteri di chiusura

1. `dotnet build` senza warning o errori;
2. `dotnet test` completamente verde;
3. badge `M3.12`;
4. indicatore salvato sempre derivato dal timestamp persistito;
5. nessun timer creato dalle singole card;
6. modifiche esterne riflesse automaticamente;
7. database SQLite v5 integro.

## M3.12.1 — Consolidamento tecnico e startup

**Obiettivo:** eliminare le ambiguità residue prima della finestra Informazioni e cronologia.

### Implementazione

- `WindowState`, `ShowActivated` e `ShowInTaskbar` impostati una sola volta prima dello `Show`;
- nessun salvataggio o ripristino di larghezza, altezza, coordinate o stato massimizzato;
- rimozione di `WindowPlacementCalculator`, eventi di geometria e `Topmost`;
- attivazione post-onboarding richiesta una sola volta tramite dispatcher;
- rimozione completa di `ICardRepository.MoveAsync` e dei test legacy monodimensionali;
- `PriorityDeadlineCalculator` usato da repository card, trasferimento fascia e ViewModel;
- conflitto di versione rappresentato tramite `MarkConcurrencyConflict`, senza `Card` parziali;
- un solo messaggio di conflitto e bozza sempre conservata;
- flag `titleChanged` e `notesChanged` calcolati una sola volta;
- schema SQLite invariato alla versione 5.

### Test

- settings legacy con campi geometria ignorati;
- bootstrap privo di `Topmost` e persistenza posizione;
- assenza di `MoveAsync` dall’interfaccia e dai test double;
- risoluzione condivisa di durata predefinita e override di fascia;
- conflitto concorrente con bozza intatta e singolo stato di errore;
- test repository bidimensionali e rollback esistenti mantenuti.

### Criteri di chiusura

1. `dotnet build` senza warning o errori;
2. `dotnet test` completamente verde;
3. board massimizzata e attiva sia all’avvio diretto sia dopo onboarding;
4. nessuna geometria finestra scritta nei settings;
5. nessun uso di `Topmost`;
6. nessun riferimento a `MoveAsync`;
7. scadenze identiche in tutti i percorsi applicativi;
8. bozza concorrente conservata con un solo messaggio;
9. badge `M3.12.1`;
10. database SQLite v5 integro.

