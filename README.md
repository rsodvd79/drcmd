# AI Shell con Ollama

Questo progetto fornisce una shell interattiva che utilizza un modello locale esposto tramite [Ollama](https://ollama.ai/) per suggerire ed eseguire comandi da terminale.

## Prerequisiti
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com/download) in esecuzione su `localhost:11434`
- Modello `gemma3:1b` installato localmente (`ollama pull gemma3:1b`)

## Avvio rapido
```bash
cd AiShell
dotnet run
```

## Utilizzo
- Digita direttamente un comando per eseguirlo tramite la shell.
- Inizia l'input con `#` per chiedere al modello un suggerimento (`#lista directory`).
- Premi invio (default sì) per eseguire il comando proposto dall'AI oppure digita `n` per annullare.
- `help` mostra i comandi disponibili, `exit`/`quit` chiude la shell.
- Il prompt mostra `ai-shell\<cartella-corrente>` per ricordare dove stai lavorando.
- Premi `Tab` per completare file o cartelle nella directory corrente.
- Usa `cd <cartella>` per cambiare directory all'interno della shell.
- Usa `auto_accetta [on|off]` per attivare o disattivare l'esecuzione automatica dei comandi suggeriti dall'AI.

## Note
- In caso di risposte anomale, la shell mostra il JSON restituito dal modello per facilitare il debug.
- L'output dei comandi è mostrato senza messaggi aggiuntivi rispetto a quanto prodotto dal processo.
- Compatibile con macOS/Linux (usa `/bin/bash`) e Windows (usa `cmd.exe`).
- Premi `Ctrl+C` per interrompere l'esecuzione del comando corrente o chiudere rapidamente l'applicazione.
