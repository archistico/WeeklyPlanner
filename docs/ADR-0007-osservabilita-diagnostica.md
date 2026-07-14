# ADR-0007 — Osservabilità locale e diagnostica

- **Stato:** Accettata
- **Data:** 14 luglio 2026
- **Milestone:** M3.3

## Contesto

WeeklyPlanner classifica già gli errori SQLite e mostra uno stato operativo nella UI, ma prima della
M3.3 non conservava una traccia tecnica persistente. Un errore osservato su una macchina dell'utente
non poteva quindi essere correlato con polling, lock, migrazioni o operazioni immediatamente
precedenti.

Il planner contiene testo libero inserito nelle card. Un sistema di logging generico rischierebbe di
registrare accidentalmente titolo o note, rendendo il log inadatto alla condivisione durante
l'assistenza.

## Decisione

WeeklyPlanner adotta un logger locale interno con le seguenti proprietà:

- formato JSON Lines, una registrazione per riga;
- scrittura asincrona tramite coda bounded e singolo consumer;
- file distinti per giorno e sequenza;
- rotazione quando un file supera 5 MB;
- retention predefinita di 14 giorni;
- directory `%LOCALAPPDATA%\WeeklyPlanner\Logs`;
- funzionamento best effort: l'indisponibilità dei log non interrompe la board;
- nessuna dipendenza NuGet aggiuntiva.

Gli eventi contengono soltanto identificativi, stato e metadati tecnici. Titolo e note delle card non
vengono passati al logger. Come difesa ulteriore, le proprietà con chiavi sensibili (`Title`, `Notes`,
`Content`, `Text` e simili) vengono sostituite con `[REDACTED]`.

Ogni errore operativo rilevante riceve un riferimento breve nel formato `WP-XXXXXX`. Il riferimento
è mostrato nella UI e scritto nello stesso record che contiene eccezione e stack trace.

Un monitor globale osserva:

- eccezioni non gestite del dispatcher Avalonia;
- eccezioni non gestite dell'AppDomain;
- eccezioni di Task non osservate.

Le eccezioni UI e di processo non vengono dichiarate gestite: il log viene forzato su disco, ma
l'applicazione non presume di poter continuare in uno stato sconosciuto. Le eccezioni di Task non
osservate vengono invece registrate e marcate come osservate.

La finestra Diagnostica raccoglie soltanto informazioni tecniche:

- versioni applicazione, .NET e Avalonia;
- sistema operativo e architettura;
- utente, computer e sessione abbreviata;
- stato board, ultimo aggiornamento e conteggi aggregati;
- percorso, dimensione e schema del database;
- percorsi impostazioni e log, stato del logger e ultimo file scritto.

La lettura diagnostica del database usa una connessione SQLite read-only e non crea il file quando è
assente.

## Conseguenze

### Positive

- gli errori segnalati dall'utente sono correlabili con un record tecnico preciso;
- polling, lock e operazioni sulle card possono essere ricostruiti senza accedere al contenuto;
- la diagnostica può essere copiata e condivisa rapidamente;
- un guasto del logging non blocca l'applicazione;
- logger e provider diagnostico sono sostituibili nei test.

### Negative

- la coda bounded può scartare eventi non critici in condizioni estreme se il writer non riesce a
tenere il passo;
- i log occupano spazio locale fino alla retention prevista;
- la finestra diagnostica espone percorsi locali e deve essere condivisa consapevolmente dall'utente;
- il logger interno richiede manutenzione diretta invece di delegarla a una libreria consolidata.

## Alternative considerate

### Serilog

Offre sink e rolling maturi, ma introdurrebbe nuove dipendenze e una superficie configurabile più
ampia del necessario per l'MVP. Potrà essere rivalutato se aumenteranno destinazioni, volumi o
requisiti strutturati.

### Solo messaggi UI

È insufficiente perché non conserva ordine temporale, stack trace o contesto delle operazioni.

### Registrare il contenuto completo delle card

Rifiutato: non è necessario alla diagnosi tecnica e aumenterebbe sensibilmente il rischio di esporre
dati personali o aziendali.
