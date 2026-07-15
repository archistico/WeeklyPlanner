# WeeklyPlanner M5.1 — Note di rilascio

M5.1 porta le operazioni di sicurezza del database direttamente nell'interfaccia.

## Novità

- creazione di backup SQLite coerenti con la board aperta;
- elenco dei backup con data, dimensione, schema e integrità;
- apertura della cartella backup;
- restore guidato con conferma;
- backup preventivo automatico;
- ripristino applicato al riavvio prima dell'apertura della board;
- rollback del database precedente in caso di errore finale;
- rilevamento delle altre istanze sullo stesso database;
- messaggio di esito dopo il riavvio.

## Compatibilità

- schema SQLite invariato alla versione 5;
- i database v1–v5 riconosciuti possono essere ripristinati;
- database con schema più recente vengono rifiutati;
- nessun backup automatico pianificato o cloud.
