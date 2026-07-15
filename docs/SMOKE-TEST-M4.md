# WeeklyPlanner M4 — Smoke test dei pacchetti Windows

## 1. Obiettivo

Verificare che i pacchetti `portable` e `self-contained` siano completi, avviabili e privi di dati
locali dell'ambiente di sviluppo.

## 2. Verifica automatica della struttura

Dopo la generazione degli archivi:

```powershell
.\scripts\verify-package.ps1 -PackagePath .\artifacts\release\<pacchetto>.zip
```

Lo script controlla:

- eseguibile, assembly, file `.deps.json` e `.runtimeconfig.json`;
- metadati `package-info.json`;
- documentazione distributiva;
- presenza di `coreclr.dll` soltanto nel pacchetto self-contained;
- assenza di database, settings e log utente.

## 3. Macchina pulita

Usare una macchina o una VM Windows x64 che non contenga il repository né artefatti di build.
Estrarre ciascun archivio in una cartella distinta.

### Pacchetto portable

1. Verificare che .NET 10 x64 sia installato.
2. Avviare `WeeklyPlanner.App.exe`.
3. Completare l'onboarding con un database locale temporaneo.
4. Verificare che la finestra si apra massimizzata e in primo piano.

Ripetere il test su una macchina senza .NET 10: il pacchetto deve richiedere il runtime e non deve
essere presentato come autonomo.

### Pacchetto self-contained

1. Usare una macchina senza runtime .NET installato, quando disponibile.
2. Avviare `WeeklyPlanner.App.exe`.
3. Completare l'onboarding con un database locale temporaneo.
4. Verificare che l'app parta senza installazioni aggiuntive.

## 4. Flusso funzionale minimo

Eseguire su entrambi i pacchetti:

1. creare una card da ciascuna intestazione;
2. modificare titolo, note e priorità;
3. salvare e verificare l'indicatore relativo;
4. trascinare una card in un'altra fascia e in un altro stato;
5. usare le scorciatoie di movimento da tastiera;
6. aprire Informazioni card e scorrere fino all'ultimo evento;
7. aprire Impostazioni e Diagnostica;
8. chiudere e riaprire l'app verificando la persistenza;
9. aprire due istanze sullo stesso database e verificare il lock di modifica.

## 5. Dati e file locali

Verificare che:

- il database venga creato nel percorso scelto, non nella cartella del pacchetto;
- `settings.json` venga scritto in `%APPDATA%\WeeklyPlanner`;
- i log vengano scritti in `%LOCALAPPDATA%\WeeklyPlanner\Logs`;
- la cartella estratta non venga modificata con database o file di configurazione.

## 6. Backup e ripristino

Eseguire almeno una volta la procedura descritta in `BACKUP-RIPRISTINO.md` usando un database di test.
Dopo il ripristino devono essere presenti card, cataloghi e cronologia.

## 7. Esito

Registrare per ogni pacchetto:

- nome archivio e checksum SHA-256;
- versione di Windows;
- presenza o assenza del runtime .NET;
- esito avvio;
- esito flusso funzionale;
- anomalie e riferimenti errore.
