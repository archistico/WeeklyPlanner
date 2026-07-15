# WeeklyPlanner — Obiettivi e roadmap

Versione documento: **3.0.0**  
Ultimo aggiornamento: **15 luglio 2026**

## 1. Visione

WeeklyPlanner è un'applicazione desktop locale in **C# / .NET 10 / Avalonia** per gestire attività
tramite una board kanban a swimlane.

Il prodotto deve restare:

- rapido da avviare e usare;
- affidabile su un singolo database SQLite locale;
- comprensibile senza amministrazione di server;
- ricco nella gestione della singola attività;
- prudente nell'aggiungere funzioni che aumentano la complessità operativa.

La struttura corrente combina:

- **stato del lavoro** nelle colonne;
- **tipologia o area progettuale** nelle fasce;
- **priorità, scadenza, note e storico** nella card.

## 2. Posizionamento

WeeklyPlanner non deve diventare una copia ridotta di una piattaforma collaborativa web. Il suo
vantaggio è offrire un kanban desktop locale, veloce e verificabile, con:

- nessun server obbligatorio;
- dati sotto il controllo dell'utente;
- matrice bidimensionale tipologia × stato;
- modifica diretta delle card;
- storico, lock e concorrenza locale;
- packaging Windows semplice e ispezionabile.

Il confronto con Vikunja, Kanboard, TaskBoard, Kan e Kanba è sintetizzato in
[`docs/DIREZIONE-PRODOTTO.md`](docs/DIREZIONE-PRODOTTO.md).

## 3. Principi di sviluppo

- milestone piccole, verificabili e reversibili;
- build senza warning;
- test automatici prima della chiusura;
- verifica manuale per le modifiche UX;
- migrazioni incrementali protette da backup e rollback;
- nessuna perdita silenziosa di dati o bozze;
- storico funzionale atomico con la mutazione;
- business logic fuori dalla UI;
- documentazione aggiornata insieme al codice;
- niente compatibilità retroattiva non necessaria prima di un rilascio stabile;
- nessuna funzione server introdotta indirettamente nel modello locale.

Le regole operative per aprire, implementare e validare le milestone sono definite in
[`docs/MILESTONES.md`](docs/MILESTONES.md).

## 4. Baseline corrente

### 4.1 Kanban

Le colonne sono cinque voci di sistema:

| Ordine | Chiave | Titolo |
|---:|---|---|
| 0 | `backlog` | BACKLOG |
| 1 | `todo` | TODO |
| 2 | `in_progress` | IN PROGRESS |
| 3 | `testing` | TESTING |
| 4 | `done` | DONE |

Le fasce sono configurabili. **Generica** è una voce di sistema sempre presente, attiva e al primo
posto.

La posizione persistita della card è:

```text
(CardTypeId, ColumnId, SortOrder)
```

### 4.2 Persistenza

```text
WeeklyPlanner.App ──> SQLite locale
```

Percorso predefinito:

```text
%LOCALAPPDATA%\WeeklyPlanner\Data\weeklyplanner.db
```

Lo schema corrente è la versione **5**. Database distinti non vengono sincronizzati e i percorsi di
rete non sono supportati.

### 4.3 Funzioni consolidate

- board a swimlane e movimento bidimensionale;
- configurazione di fasce, priorità e regole di scadenza;
- editing protetto, heartbeat e controllo ottimistico;
- feedback di salvataggio e ultimo aggiornamento relativo;
- informazioni e cronologia paginata;
- logging e diagnostica;
- backup e rollback delle migrazioni;
- test UI headless, accessibilità, doppia istanza e scenario di scala;
- processo di packaging Windows `portable` e `self-contained`.

### 4.4 Gate di rilascio M4

M4 è implementata nel codice corrente. Prima di iniziare M5.1 devono essere completati:

1. `dotnet test` in configurazione Release;
2. `scripts\release.ps1` con produzione dei due archivi;
3. verifica automatica di entrambi i pacchetti;
4. smoke test del portable su Windows x64 con .NET 10;
5. smoke test del self-contained su Windows x64 senza runtime separato;
6. prova documentata di backup e ripristino su dati di test.

Il superamento di questo gate porta M4 allo stato **Validata**. Non richiede una nuova milestone né
modifiche allo schema.

## 5. Roadmap post-MVP

L'ordine seguente è vincolante salvo una decisione esplicita documentata. Ogni milestone deve essere
validata prima di iniziare quella successiva, salvo attività puramente documentali o correzioni
bloccanti.

---

## M5.1 — Backup, ripristino e integrità dalla UI

### Obiettivo

Portare nell'applicazione le operazioni di sicurezza oggi disponibili solo tramite procedure
manuali.

### Ambito

- comando **Crea backup ora**;
- elenco dei backup disponibili;
- data, dimensione, versione schema e risultato del controllo integrità;
- apertura della cartella backup;
- ripristino guidato;
- backup automatico immediatamente prima del restore;
- chiusura coordinata delle connessioni;
- riavvio controllato dopo il ripristino;
- messaggi chiari in caso di file incompatibile o corrotto.

### Dati e architettura

- nessuna modifica necessaria allo schema funzionale v5;
- nuovo servizio applicativo per backup e restore manuale;
- riuso di `IDatabaseIntegrityChecker` e delle primitive SQLite già testate;
- nessuna copia del database mentre esistono connessioni attive non coordinate.

### Test minimi

- backup di database valido;
- rifiuto di database corrotto;
- restore con backup preventivo;
- rollback se il restore non può essere completato;
- file sidecar gestiti correttamente;
- UI disabilitata durante l'operazione;
- due istanze aperte: operazione rifiutata o coordinata esplicitamente.

### Criteri di chiusura

1. build e test verdi;
2. backup e restore verificati su database di prova;
3. integrità mostrata dalla UI;
4. nessuna perdita del database precedente in caso di errore;
5. manuale utente e documentazione operativa aggiornati.

### Esclusioni

- pianificazione automatica dei backup;
- sincronizzazione cloud;
- cifratura del database;
- gestione remota dei backup.

---

## M5.2 — Ricerca e filtri temporanei

### Obiettivo

Rendere utilizzabile una board con molte card senza alterarne posizione o contenuto.

### Ambito

Ricerca in:

- titolo;
- note;
- ID stabile o identificativo card;
- autore di creazione o ultima modifica.

Filtri combinabili:

- fascia;
- stato;
- priorità;
- senza priorità;
- scaduta;
- in scadenza;
- senza scadenza;
- modificata recentemente;
- bloccata in modifica.

UX:

- barra compatta sopra la matrice;
- `Ctrl+F` per attivare la ricerca;
- chip dei filtri applicati;
- conteggio `visibili / totali`;
- comando unico **Azzera filtri**;
- indicazione chiara quando una fascia non contiene risultati visibili.

### Dati e architettura

- nessuna migrazione prevista;
- ricerca normalizzata in memoria sulla snapshot già caricata;
- nessuna modifica a `SortOrder` o alle collection persistite;
- il filtro opera sulla proiezione, non sui repository di scrittura.

### Test minimi

- combinazioni di filtri;
- normalizzazione maiuscole/minuscole e spazi;
- ricerca con caratteri accentati;
- bozza attiva durante filtro e polling;
- movimento o modifica di una card filtrata;
- scenario da almeno 1.500 card.

### Criteri di chiusura

1. ricerca reattiva senza bloccare la UI;
2. filtri completamente azzerabili;
3. nessuna mutazione dei dati per effetto del filtro;
4. navigazione da tastiera verificata;
5. manuale e screenshot aggiornati.

### Esclusioni

- SQLite FTS;
- viste salvate;
- sintassi di query avanzata;
- ricerca fra database differenti.

---

## M5.3 — Etichette multiple e viste salvate

### Obiettivo

Aggiungere una classificazione trasversale senza sovraccaricare il significato delle fasce.

### Ambito

- CRUD etichette con nome, colore, ordine e stato attivo;
- più etichette sulla stessa card;
- badge compatti;
- assegnazione dalla bozza della card;
- filtro per una o più etichette;
- viste salvate contenenti ricerca e filtri;
- viste preferite disponibili dalla board;
- conservazione delle etichette inattive già assegnate.

### Modello dati indicativo

```text
Labels
CardLabels
SavedViews
```

La versione esatta della migrazione sarà assegnata durante l'implementazione. La tipologia resta
esclusiva; le etichette sono multiple.

### Test minimi

- unicità e normalizzazione dei nomi;
- assegnazione atomica con la card;
- etichette inattive leggibili ma non riassegnabili;
- filtri AND/OR formalizzati e testati;
- viste salvate compatibili con filtri rimossi o cataloghi inattivi;
- migrazione, backup e rollback.

### Esclusioni

- etichette gerarchiche;
- permessi sulle etichette;
- condivisione fra database;
- automazioni basate su etichette, previste più avanti.

---

## M5.4 — Checklist sulla card

### Obiettivo

Gestire passi operativi interni a una card senza introdurre un secondo sistema kanban annidato.

### Ambito

- aggiunta rapida degli elementi;
- modifica, completamento, riordino ed eliminazione;
- indicatore `completati/totali` sulla card;
- barra di avanzamento opzionale;
- visualizzazione completa nella finestra Informazioni;
- storico delle modifiche significative.

### Regole

Un elemento checklist non possiede:

- fascia;
- stato kanban;
- priorità;
- allegati;
- ricorrenza autonoma.

### Test minimi

- ordinamento atomico;
- concorrenza e lock condivisi con la card;
- annullamento della bozza;
- eliminazione della card con cleanup coerente;
- storico e conteggi;
- migrazione protetta.

---

## M5.5 — Collegamenti e allegati locali

### Obiettivo

Associare riferimenti esterni e file alle card mantenendo backup e integrità sotto controllo.

### Fase A — Collegamenti

- URL o percorso locale;
- descrizione facoltativa;
- apertura con applicazione predefinita;
- copia del collegamento;
- indicatore numerico sulla card;
- validazione senza richiedere che la destinazione esista sempre.

### Fase B — Allegati

Gli allegati vengono copiati nell'area dati dell'applicazione:

```text
Data
├── weeklyplanner.db
└── Attachments
    └── <CardStableId>
```

Metadati minimi:

- ID stabile;
- nome originale;
- nome fisico sicuro;
- dimensione;
- hash;
- data e autore;
- stato del file.

### Vincoli

- nessun semplice riferimento al file originale come unica copia;
- backup e restore devono includere gli allegati;
- rilevamento di file mancanti e orfani;
- nomi fisici non derivati direttamente da input non affidabile;
- eliminazione coerente fra database e filesystem.

### Esclusioni

- anteprime complesse;
- modifica dei file dentro WeeklyPlanner;
- storage cloud;
- sincronizzazione tra computer.

---

## M5.6 — Duplicazione e template

### Obiettivo

Velocizzare la creazione di attività ripetitive senza introdurre ancora uno scheduler.

### Ambito

- duplicazione della card;
- scelta della cella di destinazione;
- scelta degli elementi da copiare;
- copia opzionale di etichette, checklist e collegamenti;
- copia degli allegati soltanto su richiesta esplicita;
- salvataggio come template;
- creazione da template;
- CRUD e ordinamento dei template.

### Regole

- la nuova card riceve un nuovo `StableId`;
- storico e timestamp ripartono dalla creazione;
- checklist copiata con stato formalizzato: mantenuto o azzerato;
- i template non contengono lock, versioni o dati di audit.

---

## M5.7 — Centro scadenze e notifiche locali

### Obiettivo

Trasformare le scadenze già presenti in uno strumento operativo quotidiano.

### Ambito

- viste Scadute, Oggi e Prossimi giorni;
- badge riepilogativo;
- notifiche desktop locali;
- anticipo configurabile;
- rinvio di una notifica;
- deduplicazione persistente;
- apertura diretta della card dalla notifica.

### Decisione di dominio preliminare

Prima dell'implementazione deve essere formalizzata la distinzione fra:

- scadenza calcolata dalla priorità;
- eventuale scadenza manuale;
- promemoria;
- stato della notifica.

### Esclusioni

- email;
- notifiche push remote;
- calendario completo;
- sincronizzazione con servizi esterni.

---

## M5.8 — Attività ricorrenti

### Obiettivo

Generare nuove card in modo prevedibile e idempotente.

### Ambito

Trigger iniziali:

- intervallo temporale;
- ingresso in DONE;
- numero di giorni dal completamento.

Configurazioni:

- giornaliera;
- settimanale;
- mensile;
- intervallo personalizzato;
- data finale;
- numero massimo di occorrenze.

La nuova card può ereditare:

- titolo e note;
- tipologia;
- priorità;
- etichette;
- checklist azzerata;
- collegamenti;
- stato iniziale, normalmente BACKLOG.

### Vincoli

- idempotenza;
- storico della generazione;
- nessuna riapertura silenziosa della card completata;
- recupero sicuro dopo arresto dell'applicazione.

---

## M5.9 — Automazioni locali

### Obiettivo

Consentire semplici regole evento-condizione-azione senza introdurre script arbitrari.

```text
Evento → condizioni → azione
```

Esempi:

- entrando in TESTING, aggiungi un'etichetta;
- entrando in DONE, rimuovi la priorità;
- assegnando una priorità urgente, sposta in TODO;
- completando una card ricorrente, genera la successiva.

### Vincoli

- prevenzione dei cicli;
- limite massimo di azioni concatenate;
- anteprima della regola;
- audit distinto fra azione manuale e automatica;
- rollback atomico;
- nessun codice o script personalizzato.

---

## M5.10 — Limiti WIP e statistiche Kanban

### Obiettivo

Aggiungere disciplina kanban e indicatori utili senza costruire una piattaforma di business
intelligence.

### Limiti WIP

- limite globale per stato;
- limite opzionale per cella tipologia × stato;
- inizialmente solo avviso;
- evidenziazione delle intestazioni e delle celle superate.

### Statistiche

- card completate per periodo;
- tempo medio nei principali stati;
- cycle time;
- aging delle card aperte;
- attività scadute per fascia;
- cumulative flow semplificato.

### Prerequisito

Prima dell'implementazione deve essere verificato che `CardEvents` contenga tutte le transizioni e i
timestamp necessari. Le statistiche non devono essere ricostruite da `UpdatedAtUtc`.

---

## 6. Evoluzioni architetturali successive

## M6 — Board multiple e progetti

M6 è una modifica di dominio, non un semplice elemento di navigazione. Prima di implementarla devono
essere decisi:

- cataloghi globali o per board;
- spostamento delle card fra board;
- storico e ID stabile;
- ricerca globale;
- template globali o locali;
- impostazioni e backup.

M6 verrà avviata solo se l'uso reale dimostrerà che le fasce non sono sufficienti.

## M7 — Server e collaborazione distribuita

M7 è un progetto separato e comprende eventualmente:

- API;
- autenticazione;
- ruoli e permessi;
- sincronizzazione;
- conflitti distribuiti;
- allegati condivisi;
- notifiche server;
- deployment e aggiornamenti.

Le esigenze di M7 non devono complicare prematuramente il modello locale.

## 7. Funzioni fuori dalla roadmap corrente

- Gantt;
- calendario completo;
- CalDAV;
- workspace condivisi;
- link pubblici;
- billing;
- plugin e scripting arbitrario;
- OAuth, LDAP o SSO;
- database su share di rete;
- sincronizzazione automatica fra file SQLite distinti.

Una funzione esclusa può rientrare soltanto con una nuova decisione di prodotto e una valutazione di
costi, sicurezza, migrazioni e manutenzione.

## 8. Sequenza raccomandata

```text
M5.1 Backup e integrità
  ↓
M5.2 Ricerca e filtri
  ↓
M5.3 Etichette e viste
  ↓
M5.4 Checklist
  ↓
M5.5 Collegamenti e allegati
  ↓
M5.6 Duplicazione e template
  ↓
M5.7 Scadenze e notifiche
  ↓
M5.8 Ricorrenze
  ↓
M5.9 Automazioni
  ↓
M5.10 WIP e statistiche
```

Le prime quattro milestone hanno priorità maggiore perché migliorano direttamente l'uso quotidiano
senza cambiare la natura local-first del prodotto.

## 9. Cronologia sintetica

| Fase | Risultato consolidato |
|---|---|
| M0–M2 | baseline, SQLite locale, CRUD, ordinamento, editing protetto, tema e interazione |
| M3.1–M3.4 | composition root, scheduler, diagnostica, shutdown e migrazioni protette |
| M3.5–M3.8 | cataloghi, storico, schema v5, modello kanban e configurazione fasce |
| M3.9–M3.14 | swimlane, movimento 2D, priorità, cronologia, accessibilità e consolidamento |
| M4 | packaging Windows portable/self-contained implementato; gate di rilascio da completare |

I dettagli storici delle scelte tecniche sono conservati negli ADR. La roadmap non ripete più la
cronaca di ogni correzione intermedia.
