# WeeklyPlanner

Planner settimanale desktop in **C# / .NET 10 / Avalonia**, sviluppato per milestone piccole,
test automatici e documentazione aggiornata insieme al codice.

## Stato del progetto

La baseline **M0.1.2** è stata validata su Windows: `dotnet build` e tutti i test sono passati.

La milestone **M1.1 — Revisione monotona locale** introduce lo schema SQLite v2 e sostituisce il
polling basato sui timestamp con una revisione numerica incrementata automaticamente nella stessa
transazione di ogni inserimento, modifica, spostamento o cancellazione di una card.

La milestone **M1.1.1 — SQLite locale affidabile** è stata validata su Windows: build, test e
avvio runtime sono riusciti. La normalizzazione dei percorsi legacy espande variabili come
`%LOCALAPPDATA%` e converte una cartella nel file `weeklyplanner.db`.

La milestone **M1.2 — Riordino atomico** è stata validata su Windows: compilazione e test sono
passati.

La milestone corrente **M1.3 — Editing protetto** separa la bozza dai dati persistiti. Il polling
non sovrascrive più una card mentre viene modificata; un lease SQLite mostra chi sta lavorando sulla
card e `Cards.Version` impedisce aggiornamenti concorrenti silenziosi.

Per la fase corrente è stata adottata una persistenza **SQLite locale senza server**. Il database
deve risiedere su un disco locale della macchina; la sincronizzazione fra computer e l'apertura del
file tramite share di rete non fanno parte del perimetro attuale. La decisione è documentata in
[`docs/ADR-0002-sqlite-locale.md`](docs/ADR-0002-sqlite-locale.md).

## Struttura

- `WeeklyPlanner.Core`: modelli, repository, resilienza, configurazione e migrazioni SQLite;
- `WeeklyPlanner.App`: applicazione Avalonia con MVVM, onboarding, board e polling;
- `WeeklyPlanner.Tests`: test unitari e di integrazione su file SQLite temporanei;
- `docs`: decisioni architetturali e milestone operative;
- `scripts`: verifica e pubblicazione da PowerShell.

## Prerequisiti

- SDK .NET 10 compatibile con `global.json`;
- Windows per la verifica primaria dell'app desktop;
- accesso a NuGet durante il restore.

Le versioni dei pacchetti sono centralizzate in `Directory.Packages.props`. Tutti i warning,
compresi gli avvisi NuGet di sicurezza, bloccano la build.

## Verifica completa

Da PowerShell, nella radice del repository:

```powershell
.\scripts\verify.ps1
```

Comandi equivalenti:

```powershell
dotnet restore WeeklyPlanner.sln
dotnet build WeeklyPlanner.sln -c Release --no-restore
dotnet test WeeklyPlanner.sln -c Release --no-build
```

## Avvio

```powershell
dotnet run --project WeeklyPlanner.App
```

Al primo avvio vengono proposti:

- il percorso locale `%LOCALAPPDATA%\WeeklyPlanner\Data\weeklyplanner.db`;
- il nome dell'utente registrato sulle modifiche;
- l'intervallo di polling.

È possibile scegliere un altro percorso, purché il database rimanga su un filesystem locale.
Si può indicare direttamente un file `.db` oppure una cartella esistente o terminante con un
separatore: in quest'ultimo caso l'app aggiunge `weeklyplanner.db`. La cartella padre viene creata
automaticamente se non esiste.

## Migrazione dello schema

L'applicazione applica automaticamente le migrazioni embedded mancanti:

- `0001_init.sql`: colonne e card;
- `0002_board_revision.sql`: tabella `BoardState` e trigger di revisione;
- `0003_card_editing.sql`: `Cards.Version`, tabella `CardEditLocks` e trigger dei lock.

I database v1 e v2 vengono aggiornati automaticamente alla v3 senza perdere le card esistenti. Una
versione dell'app più vecchia rifiuta esplicitamente uno schema più recente.

## Risoluzione problemi del percorso database

La configurazione è salvata in `%APPDATA%\WeeklyPlanner\settings.json`.

Se l'app mostra un errore di apertura, il banner include il percorso risolto realmente utilizzato.
La M1.1.1 migra automaticamente i casi legacy più comuni, compresi:

- percorso impostato a una cartella esistente;
- percorso terminante con `\` o `/`;
- variabili d'ambiente Windows non ancora espanse;
- virgolette esterne copiate insieme al percorso.

I percorsi UNC e i file su share di rete restano intenzionalmente non supportati nella fase locale.

## Riordino atomico

`CardRepository` usa transazioni SQLite non differite per le operazioni composte. Il chiamante
fornisce soltanto card, colonna e indice di inserimento: il repository legge l'ordine corrente,
calcola la sequenza finale e salva tutte le righe modificate nello stesso commit.

La UI non cambia più le collection prima del salvataggio. Se una scrittura fallisce, non è necessario
un rollback visuale perché lo stato mostrato non è stato ancora modificato. Dopo il commit viene
eseguito un merge con i dati persistiti.


## Editing protetto

Il primo focus sul titolo o sulle note acquisisce un lease di 30 secondi, rinnovato ogni 10 secondi.
Durante l'editing:

- titolo, note e posizione non vengono sostituiti dal polling;
- compare `🔒 Stai modificando`;
- le altre istanze mostrano `🔒 <utente> sta modificando…` e rendono i campi in sola lettura;
- `Esc` o **Annulla** ripristinano i valori persistiti;
- **Salva** oppure l'uscita completa dalla card eseguono il salvataggio;
- una modifica o cancellazione esterna conserva la bozza e mostra un conflitto;
- spostamento ed eliminazione sono bloccati finché il lease è attivo.

Il lock è applicativo e a scadenza: non mantiene una transazione SQLite aperta. `Cards.Version`
aggiunge una seconda protezione ottimistica contro sovrascritture accidentali. La decisione è
formalizzata in [`docs/ADR-0003-editing-protetto.md`](docs/ADR-0003-editing-protetto.md).

## Pubblicazione

```powershell
.\scripts\publish.ps1 -RuntimeIdentifier win-x64
```

L'output predefinito viene scritto in `artifacts/publish/win-x64` come pubblicazione
framework-dependent.

## Funzionalità presenti

- colonne fisse `Backlog` e `Lunedì`–`Domenica`;
- creazione e modifica di card;
- spostamento e riordino nella stessa colonna o fra colonne tramite drag&drop;
- inserimento prima o dopo la card sotto il puntatore;
- persistenza atomica e ricompattazione dei `SortOrder`;
- configurazione locale resiliente;
- polling basato su revisione monotona;
- bozza protetta dal polling durante l'editing;
- lock a scadenza con indicatore dell'utente;
- optimistic concurrency tramite `Cards.Version`;
- rilevazione delle cancellazioni anche da una seconda istanza locale;
- migrazioni incrementali;
- foreign key SQLite abilitate;
- retry limitato sui lock SQLite temporanei.

## Limiti noti prima dell'MVP

- mancano eliminazione dalla UI, conferma e scroll verticale per colonna;
- non è ancora presente un indicatore visuale della posizione di inserimento durante il drag;
- logging non ancora implementato; i primi test dei ViewModel coprono l'editing protetto;
- non esiste sincronizzazione fra computer.

La sequenza aggiornata è in [`docs/MILESTONES.md`](docs/MILESTONES.md). La visione funzionale e la
roadmap di prodotto sono in
[`WeeklyPlanner-Obiettivi-e-Roadmap.md`](WeeklyPlanner-Obiettivi-e-Roadmap.md).
