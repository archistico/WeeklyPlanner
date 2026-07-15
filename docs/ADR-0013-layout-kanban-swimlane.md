# ADR-0013 — Layout kanban a swimlane

## Stato

Accettata. Il contratto di movimento transitorio descritto qui è stato sostituito dal movimento
bidimensionale definitivo di ADR-0014.

## Contesto

Lo schema v5 descrive la posizione di una card mediante `CardTypeId`, `ColumnId` e `SortOrder`, ma
la finestra principale continuava a mostrare cinque colonne verticali indipendenti. Tale layout non
rendeva visibile la seconda dimensione del modello: la tipologia come fascia orizzontale.

Il `BoardViewModel` possiede già gli stessi oggetti `CardViewModel` usati da editing, lock,
concorrenza ottimistica e feedback. Creare copie distinte per il nuovo layout avrebbe prodotto due
stati UI concorrenti per la stessa card e avrebbe messo a rischio le bozze durante il polling.

Nella prima introduzione del layout, il repository conservava ancora un ordine tecnico per colonna.
Il movimento definitivo è stato successivamente riallineato alla semantica locale della cella da
ADR-0014.

## Decisione

La board viene proiettata in una matrice visuale composta da:

- una riga di intestazione con `TIPOLOGIA` e i cinque stati di sistema;
- una `SwimlaneViewModel` per ogni fascia visibile;
- cinque `SwimlaneCellViewModel` per fascia, una per ciascuna colonna di sistema;
- gli stessi riferimenti `CardViewModel` già posseduti dalle collection tecniche delle colonne.

Le fasce visibili sono:

- Generica, sempre;
- tutte le fasce attive;
- le fasce inattive che contengono ancora almeno una card.

Le fasce inattive e vuote non vengono mostrate. Una card con tipologia nulla proveniente da un test o
da una sorgente legacy viene proiettata in Generica; una tipologia non esistente è invece considerata
un'incoerenza dei dati.

Ogni fascia è una singola `Grid` con sei colonne. In questo modo tutte le celle della riga ricevono
automaticamente la stessa altezza, determinata dalla cella con il contenuto maggiore. Le card sono
impilate verticalmente e le celle vuote non introducono testo segnaposto.

La finestra usa un solo `ScrollViewer` per l'intera matrice:

- scroll verticale globale con spazio finale di sicurezza;
- scroll orizzontale globale;
- pan bidimensionale con il tasto centrale.

Le azioni di creazione sono collocate nelle cinque intestazioni operative e creano una card nella
fascia Generica nello stato selezionato. Il movimento corrente usa il contratto atomico definito da
ADR-0014 e può cambiare fascia, stato e ordine locale della cella.

## Conseguenze

- schema SQLite invariato alla versione 5;
- nessuna duplicazione dello stato di editing o dei lock;
- refresh e polling possono ricostruire la proiezione riutilizzando i CardViewModel esistenti;
- una fascia con molte card aumenta l'altezza dell'intera riga, mantenendo l'allineamento;
- le card di fasce inattive non vengono nascoste;
- la matrice usa tutta la larghezza disponibile con una soglia minima di leggibilità;
- riordino, cambio stato e cambio fascia usano la posizione `(CardTypeId, ColumnId, SortOrder)`;
- lo spostamento bidimensionale viene registrato atomicamente nello storico come definito da
  ADR-0014.
