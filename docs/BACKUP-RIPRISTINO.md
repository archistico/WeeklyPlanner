# WeeklyPlanner — Backup, ripristino e integrità

## 1. Percorsi

Database predefinito:

```text
%LOCALAPPDATA%\WeeklyPlanner\Data\weeklyplanner.db
```

Backup creati dalla UI:

```text
%LOCALAPPDATA%\WeeklyPlanner\Backups\Manual
```

Backup automatici delle migrazioni:

```text
%LOCALAPPDATA%\WeeklyPlanner\Backups\Migrations
```

I due insiemi hanno scopi differenti: i backup manuali proteggono i dati operativi, quelli di
migrazione proteggono esclusivamente un aggiornamento dello schema.

## 2. Creare un backup dalla UI

1. Aprire il comando **Backup, ripristino e integrità** nell'intestazione della board.
2. Premere **Crea backup ora**.
3. Attendere la conferma.
4. Verificare nell'elenco:
   - data e ora;
   - dimensione;
   - versione dello schema;
   - stato **Valido**.

WeeklyPlanner usa il backup online di SQLite: non è necessario chiudere la board. Il file prodotto è
una copia autonoma e non dipende dai sidecar del database operativo.

## 3. Verificare i backup

La finestra controlla ogni file prima di considerarlo ripristinabile:

- integrità interna SQLite;
- riferimenti foreign key;
- presenza della tabella di versione;
- compatibilità dello schema.

Possibili stati:

| Stato | Significato |
|---|---|
| Valido | il file può essere ripristinato |
| Corrotto | SQLite o le foreign key non superano la verifica |
| Incompatibile | schema non riconoscibile o più recente dell'app |
| Mancante | il file è stato spostato o eliminato |
| Errore | file non leggibile o accesso negato |

## 4. Ripristinare un backup

1. Chiudere eventuali altre istanze di WeeklyPlanner che usano lo stesso database.
2. Aprire **Backup, ripristino e integrità**.
3. Selezionare un backup con stato **Valido**.
4. Premere **Ripristina questo backup**.
5. Leggere la conferma e scegliere **Ripristina e riavvia**.

Dopo il riavvio, quando tutte le istanze hanno chiuso il database, WeeklyPlanner crea
automaticamente un backup preventivo della versione corrente. Solo dopo questa copia sostituisce il
file operativo, prima che la board apra connessioni.

Al termine viene mostrato l'esito e il percorso del backup preventivo.

## 5. Sicurezza del restore

Durante il ripristino WeeklyPlanner:

- rifiuta l'operazione se un'altra istanza usa il database;
- verifica il backup prima e dopo la copia;
- elimina sidecar SQLite non più compatibili;
- conserva temporaneamente il database precedente;
- esegue rollback se la verifica finale fallisce;
- non elimina il backup selezionato né quello preventivo.

Se il riavvio non può essere avviato, la richiesta viene annullata e il database operativo non viene
toccato.

## 6. Backup manuale di emergenza

La copia manuale resta una procedura di emergenza:

1. chiudere tutte le istanze di WeeklyPlanner;
2. verificare da Gestione attività che `WeeklyPlanner.App.exe` non sia attivo;
3. copiare `weeklyplanner.db` in una cartella datata;
4. copiare anche `-wal` e `-journal` se presenti;
5. non usare una copia manuale ottenuta mentre l'app era aperta.

Per il normale utilizzo preferire sempre **Crea backup ora**.

## 7. Conservazione

M5.1 non applica una retention automatica ai backup manuali. L'utente può archiviarli o eliminarli
dalla cartella backup. Prima di eliminare copie vecchie conservarne almeno una su un supporto fisico
differente dal disco operativo.

## 8. Problemi

In caso di errore conservare:

- database operativo;
- backup selezionato;
- backup preventivo;
- riferimento `WP-XXXXXX`, se mostrato;
- log in `%LOCALAPPDATA%\WeeklyPlanner\Logs`.

Non modificare manualmente il contenuto binario dei file SQLite.
