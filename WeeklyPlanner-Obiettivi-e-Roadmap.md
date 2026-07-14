# WeeklyPlanner — Obiettivi e roadmap

Versione documento: **0.5.0**  
Ultimo aggiornamento: **14 luglio 2026**

## 1. Visione

WeeklyPlanner è un planner settimanale desktop leggero, scritto in **C# / .NET 10 / Avalonia**.
Riprende l'immediatezza visiva di *ThisWeekInMyLife*: una board essenziale, simile a una parete di
post-it, con `Backlog` e una colonna per ciascun giorno della settimana.

La fase corrente è **local-first**: una singola installazione desktop usa un database SQLite sul
disco locale, senza server. L'obiettivo non è costruire un sistema di project management generalista,
ma fornire uno strumento rapido e leggibile, mantenendo una base tecnica che possa tornare
collaborativa in futuro senza riscritture continue.

La configurazione del database accetta sia un percorso completo al file sia una cartella locale. I
percorsi legacy vengono normalizzati centralmente: le variabili d'ambiente vengono espanse e una
cartella viene convertita nel file `weeklyplanner.db`, evitando configurazioni formalmente complete
ma non apribili da SQLite.

## 2. Principi di sviluppo

- milestone piccole e verificabili;
- test automatici per la logica dati e applicativa;
- build senza warning;
- migrazioni incrementali e versionate;
- documentazione aggiornata insieme al codice;
- separazione netta fra UI, logica applicativa e persistenza;
- nessuna milestone dichiarata conclusa senza build e test reali;
- affidabilità e consistenza prima delle nuove funzionalità.

## 3. Obiettivi dell'MVP

| Codice | Requisito |
|---|---|
| F1 | Visualizzare `Backlog` e le colonne da lunedì a domenica |
| F2 | Creare, modificare ed eliminare card con titolo e note |
| F3 | Spostare e riordinare card tramite drag&drop |
| F4 | Tracciare autore e ultimo utente che ha modificato la card |
| F5 | Rilevare automaticamente modifiche effettuate da una seconda istanza locale |
| F6 | Configurare identità utente e percorso del database locale al primo avvio |
| F7 | Mostrare chiaramente gli stati di caricamento, offline, errore e salvataggio |
| F8 | Conservare l'ordinamento senza duplicati o buchi dopo ogni operazione |
| F9 | Proteggere le bozze dal polling e indicare chi sta modificando una card |

## 4. Non-obiettivi dell'MVP

- board multiple;
- ruoli e permessi;
- autenticazione aziendale;
- notifiche push;
- localizzazione multilingua;
- colonne personalizzabili;
- sincronizzazione offline con merge differito;
- conflict resolution sofisticata oltre a una prima forma di optimistic concurrency;
- applicazioni mobili o web.

## 5. Architettura

### 5.1 Layer

```text
WeeklyPlanner.App
    Avalonia, View, ViewModel, composizione applicativa

WeeklyPlanner.Core
    modelli, contratti, persistenza corrente, migrazioni, resilienza

WeeklyPlanner.Tests
    test unitari e di integrazione
```

L'UI non deve contenere SQL né conoscere dettagli delle migrazioni. I repository non devono
dipendere da Avalonia. Questa separazione permette di sostituire l'accesso diretto a SQLite con un
client HTTP senza riscrivere la board.

### 5.2 Persistenza locale

La decisione corrente è descritta in
[`ADR-0002`](docs/ADR-0002-sqlite-locale.md): WeeklyPlanner apre un file SQLite sul filesystem
locale della macchina, senza server e senza condivisione fra computer.

```text
WeeklyPlanner.App ──> SQLite locale
```

Il percorso predefinito è `%LOCALAPPDATA%\WeeklyPlanner\Data\weeklyplanner.db`. L'ADR-0001 resta
come analisi per un eventuale ritorno futuro al requisito multiutente.

### 5.3 Schema corrente v3

```sql
SchemaVersion(Version)
Columns(Id, Name, SortOrder)
Cards(
    Id,
    ColumnId,
    Title,
    Notes,
    SortOrder,
    CreatedBy,
    UpdatedBy,
    UpdatedAtUtc,
    Version
)
BoardState(Id, Revision)
CardEditLocks(
    CardId,
    SessionId,
    UserName,
    MachineName,
    AcquiredAtUtc,
    LastHeartbeatUtc,
    ExpiresAtUtc
)
```

La revisione viene incrementata da trigger SQLite nella stessa transazione di ogni inserimento,
modifica, spostamento o cancellazione di una card e di ogni variazione dei lease di editing. Il
polling non dipende dagli orologi dei processi. `Cards.Version` implementa il controllo di
concorrenza ottimistico sul contenuto.

### 5.4 Migrazioni

- ogni modifica allo schema ha un file `NNNN_descrizione.sql` embedded;
- le migrazioni vengono applicate in ordine, una transazione per versione;
- `SchemaVersion` viene aggiornata dal migration runner, non dallo script;
- un'app meno recente deve rifiutare un database con versione superiore;
- non sono ammessi cambi manuali non rappresentati da una migrazione.

## 6. Requisiti non funzionali

### Affidabilità

- un database locale non apribile non deve chiudere l'app;
- ogni operazione deve produrre uno stato comprensibile;
- le scritture composte devono essere atomiche;
- la UI deve ripristinare lo stato precedente se una scrittura fallisce;
- le cancellazioni devono essere osservabili da una seconda istanza locale.

### Concorrenza locale

- la board ha una revisione monotona indipendente dall'orologio;
- il riordino deve aggiornare tutte le card coinvolte nella stessa transazione;
- i `SortOrder` devono essere normalizzati e univoci nella colonna;
- le operazioni devono restare corrette anche con due istanze locali;
- una card in editing deve avere un lease a scadenza visibile alle altre istanze;
- il polling non deve sovrascrivere né spostare una bozza attiva;
- ogni salvataggio deve verificare la versione letta all'inizio dell'editing;
- le letture devono ottenere uno snapshot coerente.

### Manutenibilità

- nullable reference types attivi;
- warning trattati come errori;
- versioni NuGet centralizzate e pinning delle dipendenze transitive di sicurezza;
- CI su push e pull request;
- test su database SQLite temporaneo reale;
- nessun accesso a database o rete nei costruttori dei ViewModel.

### UX

- interazioni principali raggiungibili senza finestre complesse;
- drag avviato soltanto dopo una soglia di movimento;
- colonne con scroll verticale indipendente;
- stato di salvataggio visibile senza interrompere il lavoro;
- conferma per operazioni distruttive;
- tema chiaro/scuro coerente con il sistema.

## 7. Identità visiva

L'interfaccia richiama GTK4/Libadwaita senza tentare di copiarne i widget:

- superfici piatte;
- ombre minime;
- angoli arrotondati;
- palette neutra;
- un solo colore d'accento;
- header essenziale;
- spaziatura su una scala coerente;
- font Inter incluso tramite `Avalonia.Fonts.Inter`;
- colori definiti in risorse `DynamicResource`, non nei singoli controlli salvo stati eccezionali.

Palette iniziale:

| Ruolo | Light | Dark |
|---|---|---|
| Finestra | `#FAFAFA` | `#242424` |
| Superficie | `#FFFFFF` | `#303030` |
| Bordo | `#DEDDDA` | `#3D3846` |
| Testo primario | `#241F31` | `#FFFFFF` |
| Testo secondario | `#5E5C64` | `#9A9996` |
| Accento | `#3584E4` | `#78AAF2` |

## 8. Polling locale

Il polling dell'MVP resta periodico e interroga soltanto il database locale. Non costituisce una
sincronizzazione fra computer.

Lo schema v3 usa `BoardState.Revision`, incrementata automaticamente dai trigger su card e lock. Il
client memorizza l'ultima revisione vista e ricarica la board quando il valore cambia. I lock attivi
vengono comunque riletti a ogni ciclo: la scadenza naturale di un lease non produce infatti una
scrittura e non può incrementare la revisione.

Una successiva ottimizzazione potrà leggere board e revisione nello stesso snapshot oppure introdurre
un change log incrementale.

## 8.1 Ordinamento atomico

La M1.2 considera l'indice di drop un indice di inserimento nella collection di destinazione. Il
repository acquisisce una transazione SQLite non differita, legge l'ordine corrente e salva tutte le
card interessate con `SortOrder` contigui. Per gli spostamenti fra colonne, sorgente e destinazione
vengono aggiornate nello stesso commit e condividono lo stesso timestamp UTC.

Il ViewModel non applica più modifiche ottimistiche alla collection. La board viene aggiornata dopo
il commit; un errore produce quindi un messaggio ma non richiede di ricostruire manualmente l'ordine
precedente nella UI.


## 8.2 Editing protetto

Il focus su titolo o note apre un'unica sessione di editing per l'intera card. Il ViewModel conserva
la bozza separatamente dal modello persistito e congela titolo, note e posizione fino alla chiusura
dell'editing. Il polling continua ad aggiornare il resto della board.

`CardEditLocks` implementa un lease di 30 secondi, rinnovato ogni 10 secondi. Una seconda istanza
mostra il nome del proprietario e rende la card in sola lettura. Il lock viene rilasciato al
salvataggio, all'annullamento e alla chiusura; dopo un crash scade automaticamente.

Il salvataggio usa inoltre `Cards.Version`: l'`UPDATE` richiede sia il lease della sessione sia la
versione letta all'inizio. In caso di conflitto, scadenza o cancellazione la bozza viene conservata e
l'utente riceve un messaggio esplicito.

## 9. Test

### Automatici

- configurazione locale: assenza, corruzione, normalizzazione e salvataggio;
- migrazioni: creazione, idempotenza, versioni mancanti e schema più recente;
- repository: CRUD, vincoli referenziali e transazioni;
- riordino: stessa colonna, colonne differenti, testa, coda e rollback;
- polling: nessuna modifica, creazione, modifica, spostamento e cancellazione;
- ViewModel: merge non distruttivo, bozza, lock, conflitti, lifecycle e comandi concorrenti.

### Manuali

- onboarding;
- tema chiaro/scuro;
- drag senza partenze accidentali;
- resize e scroll;
- interruzione e ripristino della persistenza;
- due istanze locali contemporanee;
- upgrade da ogni versione di schema supportata.

## 10. Roadmap

Lo stato operativo dettagliato è mantenuto in [`docs/MILESTONES.md`](docs/MILESTONES.md).

### M0 — Baseline

- **M0.1:** toolchain riproducibile, migrazioni reali, settings resilienti, lifecycle controllato,
  aggiornamento drag&drop, test di base e CI;
- **M0.1.1:** aggiornamento del runtime SQLite vulnerabile senza disattivare l'audit NuGet;
- **M0.1.2:** adeguamento del drag&drop alle API tipizzate di Avalonia 11.3.18 e prima baseline
  validata con build e test reali su Windows.

### M1 — Coerenza dati locale

- **M1.1 / M1.1.1:** ADR SQLite locale, schema v2, revisione monotona, rilevazione cancellazioni,
  normalizzazione dei percorsi e avvio runtime validato;
- **M1.2:** creazione, eliminazione e riordino atomici; rinumerazione completa delle colonne;
  drop prima/dopo una card; rollback transazionale e verifica esplicita delle righe modificate;
- **M1.3:** schema v3, bozza protetta dal polling, lease con heartbeat e indicatore utente,
  optimistic concurrency e gestione esplicita dei conflitti.

### M2 — CRUD e UX MVP

- comando e pulsante di eliminazione;
- conferma non invasiva;
- scroll verticale indipendente;
- rifinitura dell'editor card protetto;
- indicatore saving/saved/error;
- validazione titolo;
- accessibilità da tastiera;
- indicatore visuale della posizione di drop.

### M3 — Resilienza

- dependency injection e composizione centralizzata;
- logging locale rolling;
- retry coerente e classificazione errori;
- stato online/offline;
- recupero automatico;
- test ViewModel e lifecycle.

### M4 — Packaging

- publish framework-dependent Windows;
- istruzioni di installazione/configurazione;
- test su installazione pulita e seconda istanza locale;
- procedura backup/restore;
- versione applicazione e compatibilità schema visibili nella UI.

## 11. Iterazione successiva all'MVP

Ordine proposto:

1. activity log append-only;
2. commenti;
3. etichette e filtri;
4. assegnatario;
5. scadenza e priorità;
6. checklist;
7. descrizione Markdown;
8. vista tabella;
9. ricorrenze;
10. Quick Add con parser testuale.

Ogni funzione deve avere una migrazione indipendente e non deve allargare la milestone che corregge
la consistenza di base.

## 12. Definition of Done dell'MVP

L'MVP è concluso soltanto quando:

- restore, build e test passano in CI e su Windows senza warning;
- due istanze locali possono aprire lo stesso database della macchina senza perdita di dati;
- creazione, modifica, eliminazione, spostamento e riordino sono atomici;
- dopo ogni operazione i `SortOrder` delle colonne coinvolte sono contigui da zero;
- ogni modifica, incluse le cancellazioni, viene rilevata entro il ciclo di polling;
- un errore temporaneo non provoca crash né perdita silenziosa delle modifiche;
- il riavvio conserva esattamente contenuto e ordinamento;
- il passaggio fra versioni di schema è testato;
- la persistenza adottata è coerente con l'ADR definitiva;
- README e documentazione descrivono il comportamento effettivo, non quello previsto.
