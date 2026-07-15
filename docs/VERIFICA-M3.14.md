# Verifica manuale M3.14

## Build automatica

```powershell
.\scripts\verify.ps1
```

Atteso:

- restore completato;
- build Release senza warning;
- tutti i test verdi, inclusi headless, doppia istanza, temi, contrasto e scala.

## Smoke test desktop

1. Avviare l'app e verificare che sia massimizzata e in primo piano.
2. Provare tema Chiaro e Scuro.
3. Navigare con `Tab` fino ai pulsanti `+`, alla priorità e alle icone del footer.
4. Attivare la priorità con `Invio` e `Spazio`.
5. Creare una card in ogni stato.
6. Spostare una card con mouse e tastiera.
7. Aprire Informazioni card e raggiungere l'ultimo elemento con lo scroll.
8. Aprire due istanze sullo stesso database e verificare il lock condiviso.
9. Lasciare una card in modifica durante alcuni cicli di polling: la bozza deve rimanere aperta.

## Screenshot documentali

```powershell
.\scripts\capture-ui.ps1
```

Il comando deve produrre `docs\screenshots\board-light.png` e `board-dark.png` usando dati fittizi.
