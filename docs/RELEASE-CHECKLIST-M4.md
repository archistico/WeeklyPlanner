# WeeklyPlanner M4 — Checklist release candidate

## Codice

- [ ] `dotnet restore WeeklyPlanner.sln` completato.
- [ ] `dotnet build WeeklyPlanner.sln -c Release --no-restore` senza warning o errori.
- [ ] `dotnet test WeeklyPlanner.sln -c Release --no-build` completamente verde.
- [ ] Versione, milestone e titolo finestra coerenti con M4.
- [ ] Schema SQLite ancora alla versione 5.

## Generazione

- [ ] Eseguito `scripts\release.ps1`.
- [ ] Creati esattamente due ZIP: portable e self-contained.
- [ ] Creato `SHA256SUMS.txt`.
- [ ] Entrambi gli ZIP superano `verify-package.ps1`.
- [ ] Nessun file `settings.json`, database o log è presente negli archivi.
- [ ] Nessun file PDB o artefatto di sviluppo è necessario per l'esecuzione.

## Verifica manuale

- [ ] Pacchetto portable verificato con .NET 10 x64 installato.
- [ ] Pacchetto self-contained verificato senza runtime .NET separato.
- [ ] Finestra aperta massimizzata e in primo piano.
- [ ] Onboarding completato.
- [ ] Creazione, modifica, priorità, movimento e cancellazione verificati.
- [ ] Informazioni e cronologia verificate fino all'ultimo elemento.
- [ ] Due istanze e lock verificati.
- [ ] Riavvio e persistenza verificati.
- [ ] Backup e ripristino manuale verificati su dati di prova.

## Documentazione

- [ ] `README-DISTRIBUZIONE.txt` presente alla radice del pacchetto.
- [ ] Manuale utente incluso.
- [ ] Procedura backup/ripristino inclusa.
- [ ] Smoke test incluso.
- [ ] Note di rilascio incluse.
- [ ] Checksum pubblicati insieme agli archivi.

## Approvazione

- [ ] Nessun problema bloccante aperto.
- [ ] Nome degli archivi definitivo.
- [ ] Release candidate approvata.
