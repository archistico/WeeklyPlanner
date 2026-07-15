# Screenshot dell'interfaccia

Gli screenshot sono generati dal XAML reale tramite Avalonia Headless con backend Skia.

Da PowerShell, nella radice del repository:

```powershell
.\scripts\capture-ui.ps1
```

Il comando aggiorna:

- `board-light.png`;
- `board-dark.png`.

La cattura non usa dati reali: il test costruisce una board dimostrativa in memoria e non apre o
modifica il database configurato dall'utente.
