# WeeklyPlanner M5.1 — Smoke test Windows

## Preparazione

Usare un database di prova con alcune card e annotare un titolo riconoscibile.

## Backup

1. Aprire la board.
2. Aprire **Backup, ripristino e integrità**.
3. Creare un backup.
4. Verificare stato Valido, schema v5 e dimensione maggiore di zero.
5. Aprire la cartella backup e verificare il file.

## Restore

1. Modificare o creare una card dopo il backup.
2. Selezionare il backup precedente.
3. Confermare **Ripristina e riavvia**.
4. Verificare che WeeklyPlanner si riapra massimizzato.
5. Verificare il messaggio di esito.
6. Controllare che la modifica successiva al backup non sia presente.
7. Verificare che il backup preventivo contenga invece la modifica.

## Concorrenza

1. Aprire due istanze sullo stesso database.
2. Nella prima tentare il restore.
3. Verificare che l'operazione venga rifiutata.
4. Chiudere la seconda istanza e ripetere.

## Pacchetti

Ripetere i controlli essenziali sia sul pacchetto portable sia sul self-contained.
