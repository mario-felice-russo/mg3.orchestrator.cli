# Changelog

Il formato segue [Keep a Changelog](https://keepachangelog.com/it-IT/1.1.0/) e il progetto adotta il [versionamento semantico](https://semver.org/lang/it/).

## [Non rilasciato]

### Aggiunto

- `mg3orch` senza comandi apre un menu interattivo per i processi in errore: job falliti con messaggio e VM, riepiloghi per processo e VM, log di errore dei robot, errori di un singolo job, export CSV, cambio del periodo e aiuto. Se manca la configurazione esegue prima `init`.
- Comando `mg3orch help`: aiuto in linea.

## [2.0.0] - 2026-11-15

### Modificato (incompatibile)

- La configurazione sta in **un solo file JSON** indicato con `--config <percorso>`, ora obbligatorio. Il formato è piatto: `url`, cartella (`folderId`, `folderPath` o `folderKey`), un tipo di credenziali (`token`, `clientId`/`clientSecret`/`scopes`, `username`/`password`), `format`, `style` e la sezione facoltativa `advanced`.
- La modalità di autenticazione si ricava dalle credenziali presenti; `identityUrl` si ricava dall'URL.
- Gli errori sono su una sola riga, in italiano, con il significato dell'exit code (`ERRORE (2 – uso o configurazione non validi): …`) e, quando possibile, il comando corretto. Non sono più ripetuti su `stderr`. Il JSON d'errore ha il campo `meaning`.
- Con la verbosità predefinita su `stderr` compaiono solo gli avvisi, non più i log dei retry.

### Aggiunto

- `mg3orch init`: crea il file di configurazione a partire dall'URL copiato dal browser, chiedendo solo i valori mancanti.

### Rimosso

- Variabili d'ambiente `MG3ORCH_CONFIG`, `MG3ORCH_PROFILE` e `MG3ORCH__…`; riferimenti `env:` e `file:` nei valori.
- Profili e file predefinito `./mg3orch.json`.
- Opzioni `--profile`, `--folder`, `--output`, `--text-style`, `--envelope`, `--verbosity`, `--no-pushdown`.
- Blocco `config` nella richiesta batch.

### Corretto

- `config validate` non mostra più la proprietà interna `isEmpty`.

## [1.0.0] - 2026-11-14

### Aggiunto

- CLI `mg3orch` di sola lettura per UiPath Orchestrator, in C# su .NET 10.
- Catalogo di 33 risorse di lettura (job, processi, pacchetti, robot, macchine, sessioni, code, asset, cartelle, utenti, log, audit, trigger, statistiche e altre), con azioni `list`, `get`, `count` e azioni specifiche.
- Comando `odata get` per i percorsi non presenti nel catalogo.
- Linguaggio di query a pipeline: `where`, `select`, `orderby`, `take`, `skip`, `distinct`, `groupby`, `aggregate`, `join`, `left join`, `flatten`, `count`, con funzioni su testo e date.
- Planner con pushdown verso OData e verifica di equivalenza; `--explain` e `--no-pushdown`.
- Esecuzione batch con dipendenze tra comandi (`from $id`), parallelismo e `stopOnError`.
- Output JSON originale (pagine unite), envelope opzionale, testo in stile table, list, markdown e csv.
- Autenticazione OAuth client credentials, Personal Access Token e legacy on‑prem; cache del token in memoria o su disco.
- Configurazione a strati (file con profili, variabili d'ambiente, riga di comando, richiesta batch) con validazione e segreti `env:`/`file:`.
- Retry con backoff e `Retry-After`, circuit breaker, timeout per tentativo e timeout globale.
- Exit code documentati e garanzie di terminazione.
- Comandi di servizio `catalog`, `schema` (manifest dei tool per harness IA), `config validate`, `auth test`.
- Uso come libreria tramite `AddMg3Orchestrator` e `IOrchestratorQueryService`.
- 354 test (unitari, snapshot e d'integrazione con WireMock).
