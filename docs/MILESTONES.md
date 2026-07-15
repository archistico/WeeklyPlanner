# WeeklyPlanner — Gestione delle milestone

Questo documento definisce come vengono pianificate, implementate e chiuse le milestone. La
specifica funzionale delle milestone future è in
[`../WeeklyPlanner-Obiettivi-e-Roadmap.md`](../WeeklyPlanner-Obiettivi-e-Roadmap.md).

## 1. Fonti di verità

| Documento | Responsabilità |
|---|---|
| `README.md` | stato e funzioni correnti del prodotto |
| `WeeklyPlanner-Obiettivi-e-Roadmap.md` | ordine e perimetro delle milestone future |
| `docs/MILESTONES.md` | processo, stati e criteri di gestione |
| `docs/DIREZIONE-PRODOTTO.md` | posizionamento e funzioni escluse |
| `docs/ADR-*.md` | decisioni architetturali e motivazioni |
| `MILESTONE.txt` | milestone rappresentata dal codice e dal packaging corrente |

Le stesse informazioni non devono essere replicate in più documenti con formulazioni differenti.

## 2. Stati ammessi

| Stato | Significato |
|---|---|
| **Pianificata** | obiettivo noto, ma analisi e criteri non ancora completi |
| **Pronta** | ambito, dipendenze, test e criteri di chiusura definiti |
| **In corso** | implementazione attiva |
| **Implementata — verifica richiesta** | codice e documentazione pronti, attesa verifica locale |
| **Validata** | build, test e verifiche richieste completati |
| **Rinviata** | utile ma non prioritaria nell'ordine corrente |
| **Annullata** | non più coerente con la direzione del prodotto |
| **Sostituita** | coperta da una decisione o milestone successiva |

Non usare descrizioni ambigue come “quasi completata”, “provvisoria” o “da vedere”.

## 3. Regole di apertura

Una milestone può passare a **Pronta** soltanto quando contiene:

1. problema o bisogno utente;
2. obiettivo verificabile;
3. ambito incluso;
4. esclusioni esplicite;
5. dipendenze;
6. impatto sul modello dati;
7. strategia di migrazione e rollback, quando necessaria;
8. test automatici previsti;
9. verifica manuale prevista;
10. documentazione da aggiornare.

Se una di queste informazioni manca, la milestone resta Pianificata.

## 4. Dimensione delle milestone

Una milestone deve essere sufficientemente piccola da:

- produrre un risultato utilizzabile;
- essere verificata in modo completo;
- non richiedere più migrazioni indipendenti;
- non mescolare una nuova funzione con refactoring non correlati;
- poter essere corretta con una patch limitata in caso di regressione.

Quando una funzione contiene fasi con rischi differenti, usare sottostep espliciti. Esempio:

```text
M5.5A — Collegamenti
M5.5B — Allegati locali
```

Le patch correttive usano un suffisso soltanto quando modificano realmente il perimetro, non per ogni
errore di compilazione. Le correzioni tecniche restano nella stessa milestone finché non è validata.

## 5. Flusso di lavoro

### 5.1 Analisi

- verificare il codice reale, non solo la documentazione;
- cercare API morte, duplicazioni e vincoli già esistenti;
- distinguere bug, debito tecnico e nuove funzioni;
- evitare nuove migrazioni quando il modello corrente è sufficiente;
- definire il comportamento in caso di errore e concorrenza.

### 5.2 Implementazione

- aggiornare prima i contratti di dominio e repository;
- mantenere le scritture atomiche;
- centralizzare le regole di business;
- non affidare la sicurezza soltanto alla UI;
- conservare bozza, lock e storico quando la funzione li coinvolge;
- mantenere build senza warning.

### 5.3 Verifica

Comandi minimi:

```powershell
.\scripts\verify.ps1
```

oppure:

```powershell
dotnet restore WeeklyPlanner.sln
dotnet build WeeklyPlanner.sln -c Release --no-restore
dotnet test WeeklyPlanner.sln -c Release --no-build
```

Per modifiche UI aggiungere una checklist manuale specifica. Per packaging usare anche:

```powershell
.\scripts\release.ps1
```

### 5.4 Chiusura

Una milestone è **Validata** quando:

- build senza errori o warning;
- tutti i test verdi;
- migrazioni e rollback verificati, se presenti;
- test manuali UX completati, se richiesti;
- nessuna regressione nota lasciata senza decisione;
- README aggiornato solo se cambia il prodotto corrente;
- roadmap aggiornata;
- ADR aggiunto o aggiornato quando esiste una scelta architetturale;
- manuale utente aggiornato quando cambia il flusso operativo;
- lo zip completo contiene soltanto sorgenti e documentazione, senza `bin`, `obj` o dati utente.

## 6. Regole per schema e migrazioni

- una milestone non deve modificare lo schema se la funzione può essere implementata correttamente
  con il modello corrente;
- ogni nuova versione schema deve avere una singola responsabilità leggibile;
- la migrazione deve essere idempotente rispetto al catalogo delle migrazioni;
- backup preventivo, `integrity_check` e `foreign_key_check` restano obbligatori;
- una migrazione storica già distribuita non viene riscritta retroattivamente;
- database modificati manualmente fuori dall'applicazione non sono supportati;
- il numero della futura versione schema viene deciso durante la milestone, non prenotato nella
  roadmap senza necessità.

## 7. Regole per test

Ogni nuova funzione deve coprire, quando applicabile:

- caso nominale;
- validazione input;
- concorrenza ottimistica;
- lock applicativo;
- rollback transazionale;
- audit;
- polling e merge della snapshot;
- stato vuoto;
- cataloghi inattivi o mancanti;
- accessibilità da tastiera;
- tema chiaro e scuro;
- scenario di scala;
- compatibilità con database già esistenti.

I test strutturali sul testo del sorgente sono ammessi soltanto per contratti documentali o di
packaging. Per il comportamento applicativo preferire test sul risultato reale.

## 8. Regole per documentazione

### README

Deve descrivere solo:

- cosa fa il prodotto oggi;
- come costruirlo, avviarlo e distribuirlo;
- dove trovare roadmap e manuali.

Non deve contenere la cronaca dettagliata delle milestone passate.

### Roadmap

Deve contenere:

- ordine delle milestone;
- obiettivi, ambito, esclusioni e dipendenze;
- impatto previsto sul modello dati;
- criteri di chiusura.

Non deve descrivere come già presenti funzioni non implementate.

### ADR

Un ADR conserva la motivazione storica. Quando una decisione viene superata:

- aggiungere una nota di stato o indicare l'ADR sostitutivo;
- non usare l'ADR storico come descrizione dello stato corrente;
- non cancellare le ragioni che hanno portato alla decisione originale.

### Manuale utente

Deve descrivere soltanto funzioni disponibili nella baseline corrente.

## 9. Coda corrente

| Ordine | Milestone | Stato | Dipendenza principale | Schema previsto |
|---:|---|---|---|---|
| 0 | Gate di rilascio M4 | Implementata — verifica richiesta | M4 | v5 invariato |
| 1 | M5.1 Backup, restore e integrità UI | Pianificata | M4 validata | v5 invariato |
| 2 | M5.2 Ricerca e filtri | Pianificata | M5.1 | v5 invariato |
| 3 | M5.3 Etichette e viste salvate | Pianificata | M5.2 | migrazione richiesta |
| 4 | M5.4 Checklist | Pianificata | M5.3 | migrazione richiesta |
| 5 | M5.5 Collegamenti e allegati | Pianificata | M5.1, M5.4 | migrazione + filesystem |
| 6 | M5.6 Duplicazione e template | Pianificata | M5.3–M5.5 | da definire |
| 7 | M5.7 Scadenze e notifiche | Pianificata | M5.2 | migrazione probabile |
| 8 | M5.8 Ricorrenze | Pianificata | M5.6, M5.7 | migrazione richiesta |
| 9 | M5.9 Automazioni | Pianificata | M5.8 | migrazione richiesta |
| 10 | M5.10 WIP e statistiche | Pianificata | storico consolidato | da definire |
| 11 | M6 Board multiple | Rinviata | validazione uso reale | architetturale |
| 12 | M7 Server e collaborazione | Rinviata | progetto separato | non applicabile al solo client |

Prima di portare M5.1 allo stato **Pronta**, deve essere chiuso il gate di rilascio M4.

## 10. Cronologia sintetica delle fasi consolidate

| Fase | Risultato |
|---|---|
| M0 | baseline compilabile e testabile |
| M1 | SQLite locale, revisione, ordinamento e lock |
| M2 | CRUD, drag&drop, tastiera, pan, impostazioni e tema |
| M3.1–M3.4 | composition root, scheduler, diagnostica, shutdown e migrazioni protette |
| M3.5–M3.8 | cataloghi, storico, schema v5, modello kanban e fasce |
| M3.9–M3.14 | layout swimlane, movimento 2D, priorità, cronologia e consolidamento UI |
| M4 | packaging Windows e processo di release implementati; verifica distributiva ancora richiesta |

Le correzioni intermedie e i dettagli delle scelte sono ricostruibili tramite Git e ADR; non vengono
più duplicati in questo documento.

## 11. Gestione dei cambi di priorità

La sequenza può cambiare soltanto quando:

- emerge un bug bloccante;
- una milestone dipende da una decisione non ancora presa;
- una funzione successiva riduce sensibilmente il rischio di quella corrente;
- l'uso reale dimostra che il bisogno iniziale era errato.

Ogni variazione deve aggiornare roadmap e tabella della coda, indicando il motivo. Non è sufficiente
spostare titoli senza aggiornare dipendenze e criteri.
