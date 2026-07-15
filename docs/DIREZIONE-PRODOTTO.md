# WeeklyPlanner — Direzione di prodotto

## 1. Scopo

Questo documento traduce il confronto con altri prodotti kanban in decisioni concrete per
WeeklyPlanner. Non è una lista di funzioni da copiare: serve a proteggere il posizionamento del
prodotto e a evitare espansioni incoerenti.

Riferimenti valutati:

- [Vikunja](https://github.com/go-vikunja/vikunja)
- [Kanboard](https://github.com/kanboard/kanboard)
- [TaskBoard](https://github.com/kiswa/TaskBoard)
- [Kan](https://github.com/kanbn/kan)
- [Kanba](https://github.com/kanba-co/kanba)

## 2. Posizionamento di WeeklyPlanner

WeeklyPlanner è:

> un kanban desktop locale, rapido e affidabile, con una matrice tipologia × stato e una gestione
> completa della singola attività, senza server obbligatorio.

I vantaggi da preservare sono:

- dati locali e facilmente trasferibili;
- installazione e avvio semplici;
- nessuna amministrazione di utenti o servizi;
- interazione diretta nella board;
- storico, lock e rollback;
- comportamento prevedibile anche con più istanze locali;
- documentazione e test come parte del prodotto.

## 3. Lezioni dai concorrenti

| Prodotto | Lezione utile | Cosa non inseguire ora |
|---|---|---|
| Vikunja | profondità della task: etichette, filtri, checklist, ricorrenze, relazioni e allegati | server, condivisione, CalDAV, Gantt e gerarchie complesse |
| Kanboard | disciplina kanban: WIP, ricerca, subtasks, automazioni, metriche di ciclo | architettura web/plugin come requisito del client locale |
| TaskBoard | card ricca ma board semplice; dettagli avanzati separati dalla vista principale | stack tecnico e modello di manutenzione del progetto |
| Kan | interfaccia moderna, filtri leggibili, template e attività comprensibile | workspace, membri e permessi nel breve periodo |
| Kanba | pulizia visuale, dashboard e raccolta di riferimenti | dipendenza da autenticazione e servizi hosted |

## 4. Funzioni da adottare

### Priorità alta

- backup e ripristino dalla UI;
- ricerca e filtri;
- etichette multiple;
- viste salvate;
- checklist;
- collegamenti e allegati locali;
- duplicazione e template.

Queste funzioni aumentano il valore quotidiano senza cambiare il modello local-first.

### Priorità successiva

- centro scadenze;
- notifiche locali;
- ricorrenze;
- automazioni limitate;
- limiti WIP;
- statistiche kanban.

Queste funzioni richiedono decisioni di dominio più rigorose e devono arrivare dopo la gestione
completa dei contenuti della card.

## 5. Funzioni deliberatamente escluse dal breve periodo

- utenti, ruoli e permessi;
- workspace condivisi;
- sincronizzazione cloud;
- link pubblici;
- commenti conversazionali;
- OAuth, LDAP e SSO;
- API pubblica e plugin;
- Gantt;
- calendario completo;
- billing;
- database su share di rete.

L'esclusione non significa che siano funzioni inutili. Significa che introdurle ora richiederebbe un
prodotto diverso: server, sicurezza distribuita, sincronizzazione, gestione identità e un nuovo
modello operativo.

## 6. Regole di design

### Board leggibile

La board deve mostrare solo le informazioni necessarie per decidere e agire. I dettagli più profondi
restano nella modifica o nella finestra Informazioni.

### Card ricca, non sovraccarica

Nuovi attributi devono avere:

- rappresentazione compatta fuori modifica;
- editor esplicito;
- comportamento da tastiera;
- storico e rollback coerenti;
- nessun salvataggio indipendente che possa spezzare la bozza.

### Tipologie ed etichette non sono equivalenti

- la tipologia è esclusiva e determina la fascia;
- le etichette sono multiple e trasversali.

### Locale prima di tutto

Ogni funzione deve funzionare senza servizi esterni. Collegamenti e notifiche possono interagire con
il sistema operativo, ma i dati autorevoli restano locali.

### Automazioni governabili

Le automazioni future devono essere dichiarative, limitate e auditabili. Non verranno introdotti
script arbitrari.

### Nessuna complessità server anticipata

Il modello locale non deve essere deformato per facilitare un ipotetico server futuro. M7 avrà
contratti e decisioni proprie.

## 7. Segnali per rivalutare M6 o M7

Board multiple o collaborazione distribuita diventano prioritarie soltanto se l'uso reale mostra uno
o più dei seguenti segnali:

- le fasce non bastano più a separare progetti indipendenti;
- il volume rende necessaria una ricerca trasversale fra board;
- più persone devono lavorare da postazioni differenti sugli stessi dati;
- il trasferimento manuale del database non è più sostenibile;
- allegati e notifiche richiedono una fonte condivisa autorevole.

Fino ad allora, la roadmap resta concentrata sul miglior prodotto desktop locale possibile.
