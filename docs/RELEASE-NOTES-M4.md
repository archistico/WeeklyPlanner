# WeeklyPlanner M4 — Note di rilascio

## Stato

M4 chiude il packaging dell'MVP locale di WeeklyPlanner per Windows x64.

## Pacchetti

La release produce due distribuzioni a cartella:

- **portable framework-dependent**: richiede .NET 10 x64 installato;
- **self-contained**: include il runtime .NET necessario.

Entrambe includono applicazione, dipendenze, metadati di pacchetto, manuale, procedura di backup e
smoke test. Database, impostazioni e log non vengono inclusi.

## Funzionalità MVP

- board kanban a swimlane con cinque stati;
- fasce configurabili e Generica protetta;
- drag&drop bidimensionale e movimento da tastiera;
- modifica protetta da lock e controllo ottimistico;
- priorità con scadenze e override per fascia;
- indicatore dell'ultimo salvataggio;
- informazioni card e cronologia paginata;
- temi chiaro e scuro;
- diagnostica e logging locale;
- uso concorrente di più istanze sullo stesso database locale.

## Dati

Lo schema SQLite resta alla versione 5. Il database non viene spostato nella cartella di
installazione e continua a essere selezionato dall'utente.

Prima di aggiornare una postazione con dati importanti è raccomandato un backup manuale seguendo
`BACKUP-RIPRISTINO.md`.

## Limitazioni note

- Windows x64 è il target di distribuzione verificato;
- i percorsi database UNC di rete non sono supportati;
- il pacchetto portable richiede il runtime .NET 10 x64;
- non è ancora presente un installer MSI/MSIX;
- backup, ripristino e verifica integrità non sono ancora esposti dalla UI.
