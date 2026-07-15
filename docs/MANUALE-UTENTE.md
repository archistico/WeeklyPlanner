# WeeklyPlanner — Manuale utente

## 1. Avvio

WeeklyPlanner si apre come normale applicazione desktop massimizzata. Al primo avvio richiede:

- percorso locale del database SQLite;
- nome da registrare nelle modifiche;
- intervallo di aggiornamento;
- tema Sistema, Chiaro o Scuro.

Il database deve essere locale. Percorsi di rete UNC e percorsi relativi non sono supportati.

## 2. Struttura della board

La board è una matrice:

```text
TIPOLOGIA | BACKLOG | TODO | IN PROGRESS | TESTING | DONE
```

Le righe sono le fasce o tipologie. Le colonne sono gli stati del lavoro. La fascia **Generica** è
sempre presente e rimane la prima.

Il pulsante `+` a destra di ogni intestazione crea una card in Generica direttamente nello stato
selezionato.

## 3. Navigazione

- Rotella del mouse: scroll verticale.
- Scrollbar orizzontale: raggiunge le colonne quando la finestra è stretta.
- Tasto centrale premuto e trascinamento: pan orizzontale e verticale.
- `Tab`: sposta il focus fra controlli e card.

## 4. Creazione e modifica

Cliccare sul titolo, sulle note o sul riepilogo della priorità per entrare in modifica. WeeklyPlanner
acquisisce un lock applicativo prima di aprire la bozza.

La modifica resta aperta fino a una delle seguenti azioni:

- **Salva**;
- **Annulla**;
- `Esc`.

Il click o l'attivazione da tastiera sul riepilogo priorità apre direttamente la ComboBox. Sono
supportati `Invio` e `Spazio` quando il riepilogo ha il focus.

## 5. Priorità e scadenze

Fuori modifica la priorità è mostrata come riepilogo compatto sullo stesso sfondo della card. Durante
la modifica compare una ComboBox. L'opzione **Nessuna** rimuove priorità e scadenza.

La scadenza deriva dalla durata della priorità e dall'eventuale regola specifica della fascia.

## 6. Movimento delle card

Con il mouse, trascinare la maniglia superiore della card nella cella desiderata. È possibile
cambiare fascia, stato e posizione in una sola operazione.

Scorciatoie:

- `Alt` + `Su/Giù`: riordina nella stessa cella;
- `Alt` + `Sinistra/Destra`: cambia stato nella stessa fascia;
- `Ctrl` + `Alt` + `Su/Giù`: cambia fascia.

Una fascia inattiva mostra ancora le card esistenti, ma non può riceverne di nuove da altre fasce.

## 7. Salvataggio e stato

Il footer della card mostra un solo stato:

- floppy verde e tempo relativo: salvata;
- **Non salvata**: bozza modificata;
- **Salvataggio…**: scrittura in corso;
- **Errore**: salvataggio non riuscito.

Il tooltip del floppy mostra data, ora e autore dell'ultima modifica.

## 8. Informazioni e cronologia

L'icona `i` apre una finestra in sola lettura con:

- creazione e ultima modifica;
- fascia, stato, priorità e scadenza;
- eventuale lock attivo;
- cronologia degli eventi.

La cronologia viene caricata in pagine da 20 elementi. **Carica eventi precedenti** recupera la pagina
successiva senza bloccare l'editing di altre card.

## 9. Uso con due istanze

Più istanze possono aprire lo stesso database locale sulla stessa macchina. Quando una card è in
modifica:

- le altre istanze vedono il proprietario del lock;
- non possono modificarla, spostarla o eliminarla;
- il polling aggiorna la board senza sovrascrivere bozze locali.

Se una copia diventa obsoleta, WeeklyPlanner conserva la bozza e segnala il conflitto. Annullare la
modifica per ricaricare i dati correnti.

## 10. Configurazione

Dalle Impostazioni è possibile cambiare:

- nome utente;
- database usato al prossimo avvio;
- intervallo di polling;
- tema;
- priorità, fasce, colori, ordine e regole di scadenza.

L'eliminazione di una fascia usata richiede una fascia di destinazione. Trasferimento, scadenze e
storico vengono aggiornati in una singola transazione.

## 11. Diagnostica

La finestra Diagnostica mostra versione, runtime, stato database, schema, percorsi e logger. Non
espone titoli o note delle card.

In caso di errore, conservare il riferimento `WP-XXXXXX` mostrato nell'interfaccia insieme ai log in:

```text
%LOCALAPPDATA%\WeeklyPlanner\Logs
```


## 12. Pacchetti Windows M4

Il pacchetto **portable** richiede .NET 10 x64 installato. Il pacchetto **self-contained** include il
runtime necessario. In entrambi i casi estrarre completamente lo ZIP e avviare
`WeeklyPlanner.App.exe`.

I pacchetti non includono database, impostazioni o log. Prima di trasferire o sostituire una
postazione seguire [`BACKUP-RIPRISTINO.md`](BACKUP-RIPRISTINO.md).
