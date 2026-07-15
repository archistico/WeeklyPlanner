# ADR-0020 — Packaging MVP locale

- **Stato:** accettata
- **Data:** 15 luglio 2026
- **Milestone:** M4

## Contesto

L'MVP deve essere distribuibile su Windows senza dipendere dal repository o dagli artefatti di
sviluppo. Gli utenti possono avere o meno il runtime .NET installato. I dati SQLite e le preferenze
sono locali alla postazione e non devono essere incorporati negli archivi di rilascio.

## Decisione

La release M4 produce per `win-x64`:

1. un pacchetto framework-dependent denominato `portable`;
2. un pacchetto `self-contained` con runtime incluso.

Entrambi restano distribuzioni a cartella e non usano single-file o trimming. Questa scelta evita
rischi prematuri con risorse Avalonia, provider SQLite e diagnostica, mantenendo il contenuto
ispezionabile.

Il processo di release:

- verifica build e test in configurazione Release;
- esegue `dotnet publish` per entrambi i modi;
- copia la documentazione distributiva;
- genera `package-info.json`;
- crea archivi ZIP versionati;
- genera checksum SHA-256;
- verifica automaticamente struttura e assenza di dati locali.

## Dati utente

Database, sidecar SQLite, `settings.json` e log non vengono mai copiati nei pacchetti. Backup e
ripristino restano procedure manuali con tutte le istanze chiuse.

## Alternative escluse

- **Single-file:** rinviato finché non è verificato su tutte le dipendenze native.
- **Trimming:** escluso per evitare rimozioni basate su reflection o markup Avalonia.
- **Installer MSI/MSIX:** fuori dal perimetro MVP.
- **Database preconfigurato nel pacchetto:** escluso per separare software e dati.

## Conseguenze

- il pacchetto portable è più piccolo ma richiede .NET 10 x64;
- il self-contained è più grande ma autonomo;
- gli archivi sono riproducibili tramite script e verificabili tramite checksum;
- un installer potrà essere introdotto successivamente senza cambiare il modello dei dati.
