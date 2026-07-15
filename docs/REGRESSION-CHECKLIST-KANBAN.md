# WeeklyPlanner — Checklist di regressione kanban

Questa checklist sostituisce la verifica legata alla singola milestone M3.14 e deve essere usata dopo
modifiche rilevanti alla board, all'editing, al polling o ai temi.

## Verifica automatica

```powershell
.\scripts\verify.ps1
```

Atteso:

- restore completato;
- build Release senza warning;
- tutti i test verdi;
- test headless, doppia istanza, temi, contrasto e scala inclusi.

## Avvio

1. Avviare l'applicazione.
2. Verificare che la finestra sia massimizzata e visibile in primo piano.
3. Completare l'onboarding su un profilo pulito e verificare il passaggio alla board.
4. Chiudere e riaprire: posizione e dimensione precedenti non devono essere ripristinate.

## Board e navigazione

1. Provare tema Chiaro e Scuro.
2. Verificare intestazioni e pulsanti `+` dei cinque stati.
3. Scorrere fino all'ultima fascia e visualizzare completamente l'ultima card.
4. Usare il pan con tasto centrale in entrambe le direzioni.
5. Navigare con `Tab` fra card, priorità e azioni del footer.

## Card

1. Creare una card in ogni stato.
2. Modificare titolo, note e priorità.
3. Lasciare una bozza aperta per più cicli di polling.
4. Salvare e verificare feedback e timestamp relativo.
5. Annullare una bozza e verificare il ripristino.
6. Attivare la priorità con click, `Invio` e `Spazio`.
7. Spostare una card con mouse e tastiera.
8. Aprire Informazioni card e raggiungere l'ultimo evento con lo scroll.

## Concorrenza

1. Aprire due istanze sullo stesso database locale.
2. Modificare una card nella prima istanza.
3. Verificare lock e blocco delle azioni nella seconda.
4. Rilasciare il lock e verificare la successiva acquisizione.
5. Simulare una copia obsoleta e verificare la conservazione della bozza.

## Configurazione

1. Creare, rinominare, riordinare e disattivare una fascia utente.
2. Verificare che Generica resti protetta.
3. Eliminare una fascia vuota.
4. Eliminare una fascia usata trasferendo le card.
5. Modificare priorità e regole di scadenza.

## Screenshot documentali

```powershell
.\scripts\capture-ui.ps1
```

Il comando deve aggiornare gli screenshot chiaro e scuro usando dati fittizi, senza aprire o
modificare il database configurato dall'utente.

## Backup e ripristino M5.1

- [ ] La finestra backup si apre dall'intestazione senza alterare la board.
- [ ] **Crea backup ora** produce un file valido e selezionabile.
- [ ] Data, dimensione, schema e integrità sono leggibili.
- [ ] Un file corrotto non abilita il pulsante di ripristino.
- [ ] Il restore crea un backup preventivo e riavvia l'app.
- [ ] Dopo il riavvio viene mostrato l'esito.
- [ ] Con una seconda istanza aperta il restore viene rifiutato.
- [ ] Dopo un restore riuscito drag&drop, editing, polling e cronologia funzionano ancora.
