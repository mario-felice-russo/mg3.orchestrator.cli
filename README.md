# mg3orch

CLI di **sola lettura** per UiPath Orchestrator, scritta in C# su .NET 10.

- Espone i comandi di lettura di Orchestrator (job, processi, code, robot, asset, log, statistiche…).
- Esegue **query complesse** sui risultati con un linguaggio a pipeline: filtri, proiezioni, ordinamenti, raggruppamenti, aggregazioni e join tra comandi.
- Restituisce **testo leggibile** oppure il **JSON originale** di Orchestrator.
- È pensata sia per gli **harness di agenti IA** (output deterministico, nessuna interazione, manifest dei tool) sia per le **applicazioni proprietarie** (processo esterno o libreria .NET).

La CLI non invia mai richieste di scrittura: l'unico verbo HTTP ammesso è `GET`, con l'eccezione della richiesta del token.

---

## Indice

1. [Installazione](#installazione)
2. [Avvio rapido](#avvio-rapido)
3. [Configurazione](#configurazione)
4. [Comandi](#comandi)
5. [Linguaggio di query (DSL)](#linguaggio-di-query-dsl)
6. [Output](#output)
7. [Richiesta batch](#richiesta-batch)
8. [Exit code e garanzie di terminazione](#exit-code-e-garanzie-di-terminazione)
9. [Integrazione con un harness IA](#integrazione-con-un-harness-ia)
10. [Integrazione in un'applicazione .NET](#integrazione-in-unapplicazione-net)
11. [Script PowerShell: processi in errore](#script-powershell-processi-in-errore)
12. [Architettura](#architettura)
13. [Sviluppo e verifica](#sviluppo-e-verifica)
14. [Dipendenze](#dipendenze)
15. [Tracciabilità dei requisiti](#tracciabilità-dei-requisiti)
16. [Possibili estensioni](#possibili-estensioni)

---

## Installazione

Serve l'SDK .NET 10.

| Modalità | Comando |
|---|---|
| Eseguibile self-contained (un solo file) | `dotnet publish src/Mg3.Orchestrator.Cli -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o artifacts/linux-x64` |
| `dotnet tool` globale | `dotnet pack src/Mg3.Orchestrator.Cli -c Release -o artifacts/nupkg` e poi `dotnet tool install -g Mg3.Orchestrator.Cli --add-source artifacts/nupkg` |

Per Windows e macOS sostituire `linux-x64` con `win-x64` oppure `osx-arm64`. In entrambi i casi il comando si chiama `mg3orch`.

## Avvio rapido

```bash
# 1. Crea il file di configurazione: incolla l'URL di Orchestrator copiato dal browser
mg3orch init --config ~/mg3orch/dev.json --url "https://cloud.uipath.com/<org>/<tenant>/orchestrator_/jobs?fid=123456" --token <PAT>

# 2. Verifica
mg3orch auth test --config ~/mg3orch/dev.json

# 3. Usa
mg3orch jobs list --since 24h -q "where State == 'Faulted' | select Key, ReleaseName, Info" --config ~/mg3orch/dev.json
```

Senza `--token` (o `--client-id`/`--client-secret`), `init` chiede i valori mancanti.

## Configurazione

Tutta la configurazione sta in **un solo file JSON**, indicato con `--config <percorso>` (o `-c`). Non ci sono variabili d'ambiente, profili né opzioni da riga di comando che la sovrascrivono. Per un altro ambiente si usa un altro file, ad esempio `dev.json` e `prod.json`.

Il file più piccolo possibile:

```json
{
  "url": "https://cloud.uipath.com/<org>/<tenant>/orchestrator_/",
  "folderId": 123456,
  "token": "<Personal Access Token>"
}
```

### Chiavi

| Chiave | Obbligatoria | Significato |
|---|---|---|
| `url` | sì | URL di Orchestrator. Cloud: `https://cloud.uipath.com/<org>/<tenant>/orchestrator_/` |
| `folderId` **oppure** `folderPath` **oppure** `folderKey` | per le risorse legate a una cartella | Cartella: id (il valore `fid` nell'URL del browser), percorso (`Shared/Finance`) o GUID |
| `token` | una delle tre credenziali | Personal Access Token |
| `clientId` + `clientSecret` + `scopes` | una delle tre credenziali | Applicazione esterna (confidential). `scopes`: solo scope `*.Read`, separati da spazi |
| `username` + `password` | una delle tre credenziali | Autenticazione legacy, solo on‑prem |
| `identityUrl` | no | Endpoint del token. Default: `https://<host>/identity_/connect/token` in cloud, `https://<host>/identity/connect/token` on‑prem |
| `tenantName` | no | Tenant on‑prem |
| `format` | no | `text` (default) oppure `json` |
| `style` | no | Stile del testo: `table` (default), `list`, `markdown`, `csv` |
| `advanced` | no | Impostazioni facoltative, vedi sotto |

La modalità di autenticazione si ricava dalle credenziali presenti: `token`, oppure `clientId`/`clientSecret`, oppure `username`/`password`. Indicarne più di un tipo è un errore.

### Sezione `advanced`

| Chiave | Default | Significato |
|---|---|---|
| `envelope` | `false` | Incapsula ogni risultato JSON con metadati |
| `maxRows` | 200 | Righe mostrate in testo (il JSON non viene troncato) |
| `maxCellWidth` | 80 | Larghezza massima di una cella in testo |
| `timeZone` | `UTC` | Fuso orario delle date nel testo |
| `culture` | `it-IT` | Formato dei numeri nel testo (`csv` usa sempre la cultura invariante) |
| `timeoutSeconds` | 30 | Timeout di ogni tentativo HTTP |
| `maxRetries` | 3 | Tentativi aggiuntivi su 408, 429, 5xx ed errori di rete |
| `retryBaseDelayMs` | 500 | Attesa base del backoff esponenziale con jitter |
| `pageSize` | 500 | Elementi per pagina (`$top`), massimo 1000 |
| `maxItems` | 1000 | Elementi massimi letti per comando |
| `maxPages` | 50 | Pagine massime lette per comando |
| `globalTimeoutSeconds` | 120 | Durata massima dell'intera esecuzione, retry compresi |
| `maxJoinRows` | 100000 | Righe massime prodotte da un join |
| `tokenCache` / `tokenCachePath` | `memory` | `disk` attiva una cache su file con permessi `600` (default `~/.mg3orch/token-cache.json`) |
| `verbosity` | `normal` | `quiet` (nulla su stderr), `normal` (solo avvisi), `detailed`, `diagnostic` (anche le URL chiamate) |

Le chiavi non distinguono maiuscole e minuscole e il file ammette commenti `//`. Una chiave sconosciuta o un valore sbagliato producono un errore che indica la chiave e l'elenco di quelle ammesse (exit code `2`). Un esempio è in [`mg3orch.sample.json`](mg3orch.sample.json).

### `mg3orch init`

Crea il file e lo valida prima di scriverlo, con permessi `600`.

| Opzione | Significato |
|---|---|
| `--config <percorso>` | Dove scrivere il file (obbligatoria) |
| `--url <url>` | URL di Orchestrator; va bene quello copiato dal browser: organizzazione, tenant e cartella (`fid`) vengono ricavati |
| `--token` | Personal Access Token |
| `--client-id`, `--client-secret`, `--scopes` | Applicazione esterna (`--scopes` ha un default di sola lettura) |
| `--username`, `--password`, `--tenant` | On‑prem legacy |
| `--folder-id`, `--folder-path` | Cartella, se non è nell'URL |
| `--force` | Sovrascrive un file esistente |

I valori mancanti vengono chiesti uno alla volta. È l'unico comando che fa domande: con tutte le opzioni funziona senza interazione.

### Segreti

Token, secret e password stanno nel file. Proteggilo come una password: `init` lo crea leggibile solo dal proprietario, e il file non va messo sotto controllo di versione. I segreti non compaiono mai in output o nei log: `config validate` li mostra come `***`.

## Comandi

```
mg3orch <risorsa> <azione> [chiave] [opzioni]
```

| Azione | Significato |
|---|---|
| `list` | Elenco paginato |
| `get <id\|key\|nome>` | Singolo elemento. Un id numerico usa `Risorsa(id)`, un GUID filtra su `Key`, un testo filtra sul campo nome |
| `count` | Conteggio tramite `$count` |

Le risorse senza elenco (`stats jobs`, `stats sessions`, `queuestats`, `license`) si invocano senza azione. `packages versions <id>` elenca le versioni di un pacchetto.

**Opzioni di lettura** (`list`, `count`): `--query/-q`, `--filter`, `--select`, `--orderby`, `--top`, `--skip`, `--expand`, `--since <durata>`. Le opzioni OData vengono inviate così come sono; `--since 24h` filtra sul campo temporale della risorsa.

**Opzioni globali**: `--config/-c <file>` (obbligatoria, salvo per `catalog`, `schema` e `--help`) e `--explain`. Formato dell'output, cartella e tutto il resto stanno nel file di configurazione.

**Comandi di servizio**:

| Comando | Scopo |
|---|---|
| `init` | Crea il file di configurazione |
| `catalog` | Elenca risorse, azioni, campi filtrabili. Funziona anche senza configurazione |
| `schema` | Manifest JSON dei tool per gli harness IA |
| `config validate` | Valida e stampa la configurazione con i segreti mascherati |
| `auth test` | Ottiene un token e legge `odata/Folders` |
| `odata get <percorso>` | `GET` generico su un percorso `odata/...` o `api/...` non presente nel catalogo |
| `run --request <file\|->` | Esecuzione batch |

### Risorse

| CLI | Endpoint | Cartella |
|---|---|---|
| `jobs` | `odata/Jobs` | sì |
| `releases` (alias `processes`) | `odata/Releases` | sì |
| `packages` | `odata/Processes` | no |
| `robots`, `sessions`, `environments` | `odata/Robots`, `odata/Sessions`, `odata/Environments` | sì |
| `machines` | `odata/Machines` | no |
| `queues`, `queueitems`, `queueitemevents` | `odata/QueueDefinitions`, `odata/QueueItems`, `odata/QueueItemEvents` | sì |
| `queuestats` | `odata/QueueProcessingRecords/UiPathODataSvc.RetrieveQueuesProcessingStatus` | sì |
| `assets`, `buckets`, `tasks`, `triggers` (alias `schedules`) | `odata/Assets`, `odata/Buckets`, `odata/Tasks`, `odata/ProcessSchedules` | sì |
| `folders`, `personalworkspaces`, `users`, `roles` | `odata/Folders`, `odata/PersonalWorkspaces`, `odata/Users`, `odata/Roles` | no |
| `robotlogs` (alias `logs`), `auditlogs` | `odata/RobotLogs`, `odata/AuditLogs` | sì / no |
| `libraries`, `webhooks`, `calendars`, `alerts`, `credentialstores`, `settings`, `license` | omonimi in `odata/` | no |
| `testsets`, `testsetexecutions`, `testcaseexecutions` | omonimi in `odata/` | sì |
| `stats jobs`, `stats sessions` | `api/Stats/GetJobsStats`, `api/Stats/GetSessionsStats` | sì |

`mg3orch catalog` mostra anche i campi di ogni risorsa. Nessun endpoint è stato verificato su uno Swagger reale: vedi [ASSUMPTIONS.md](ASSUMPTIONS.md).

## Linguaggio di query (DSL)

Una query è una pipeline di stadi separati da `|`. Può iniziare con una sorgente (`from`), altrimenti la sorgente è il comando.

```
query  = [ "from" ( risorsa | "$id" ) [ "as" alias ] ] { "|" stadio }
stadio = where | select | orderby | take | skip | distinct | groupby | aggregate
       | [left] join | flatten | count
```

| Stadio | Esempio | Effetto |
|---|---|---|
| `where` | `where State == 'Faulted'` | Filtra |
| `select` | `select Key, Robot.Name, durata = EndTime - StartTime` | Proietta; `alias = espr` oppure `espr as alias` |
| `orderby` | `orderby StartTime desc, Id` | Ordina (stabile) |
| `take` / `skip` | `skip 10 \| take 5` | Pagina |
| `distinct` | `distinct ReleaseName` | Righe distinte (sui campi indicati o sull'intera riga) |
| `groupby` + `aggregate` | `groupby ReleaseName \| aggregate n = count(), ultimo = max(EndTime)` | Raggruppa; senza `aggregate` produce `count` |
| `aggregate` | `aggregate n = count()` | Aggregato sull'intero insieme |
| `join` / `left join` | `join releases as r on ReleaseName == r.Name` | Hash join; le colonne della destra stanno sotto l'alias |
| `flatten` | `flatten Tags` | Una riga per elemento dell'array |
| `count` | `count` | Una riga `{ "count": n }` |

**Operatori**: `==`, `!=` (o `<>`), `<`, `<=`, `>`, `>=`, `in`, `contains`, `startswith`, `endswith`, `matches` (regex), `is null`, `is not null`, `and`, `or`, `not`, `+`, `-`, `*`, `/`.

**Letterali**: stringhe `'...'` o `"..."` (apice raddoppiato per l'escape), numeri, `true`, `false`, `null`, durate (`500ms`, `30s`, `15m`, `24h`, `7d`, `1w`), liste `('a', 'b')` o `['a', 'b']`.

**Funzioni**: `now()`, `today()`, `date(s)`, `lower(x)`, `upper(x)`, `len(x)`, `coalesce(a, b, …)`, `year(d)`, `month(d)`, `day(d)`, `hour(d)`. Aggregati: `count()`, `count(x)`, `countif(cond)`, `sum(x)`, `avg(x)`, `min(x)`, `max(x)`.

### Semantica

- I campi si raggiungono con la notazione puntata (`Robot.Name`). Un campo assente vale `null` e non genera errore; un campo assente da **tutte** le righe genera un avviso su `stderr`.
- Il confronto tra stringhe non distingue maiuscole e minuscole, come Orchestrator.
- Le stringhe in formato ISO 8601 vengono confrontate come date, anche con `now() - 7d` o `date('2026-11-01')`.
- `matches` usa un timeout di 100 ms per espressione: superato il timeout, la query fallisce con exit code `5`.
- Le parole chiave non distinguono maiuscole e minuscole. Gli errori di sintassi riportano la posizione, il token atteso e quello trovato (exit code `5`).

### Pushdown verso OData

Il planner traduce in `$filter`, `$orderby`, `$skip`, `$top` e `$select` il **prefisso più lungo** di stadi traducibili; tutto il resto viene eseguito in memoria. Le regole sono conservative, perché il risultato deve essere identico a quello calcolato interamente in memoria:

- si traducono solo campi dichiarati filtrabili nel catalogo, con i nomi canonici;
- i confronti d'ordine (`<`, `>`…) si traducono solo su numeri e date;
- un `where` dopo `take`/`skip` o dopo `select` non viene tradotto, perché cambierebbe il significato;
- un `select` si traduce solo se elenca campi di primo livello senza alias.

`--explain` mostra il piano senza contattare Orchestrator.

### Esempi

```text
 1. where State == 'Faulted'
 2. where State in ('Faulted', 'Stopped') and CreationTime >= now() - 24h
 3. where Info contains 'timeout' | select Key, ReleaseName, StartTime, Info
 4. orderby StartTime desc | take 10
 5. where EndTime is not null | select Id, durata = EndTime - StartTime | orderby durata desc | take 5
 6. groupby ReleaseName | aggregate n = count(), errori = countif(State == 'Faulted') | orderby errori desc
 7. groupby HostMachineName, State
 8. where ReleaseName startswith 'Fatt' | count
 9. from jobs as j | join releases as r on j.ReleaseName == r.Name | select j.Key, r.ProcessVersion
10. from $faults | left join $rel as r on ReleaseName == r.Name | where r.Name is null
11. where Info matches '^Errore [0-9]{3}'
12. flatten Tags | distinct Tags
13. aggregate primo = min(CreationTime), ultimo = max(CreationTime), media = avg(Id)
14. where year(CreationTime) == 2026 and hour(CreationTime) >= 18
15. where coalesce(EndTime, StartTime) > date('2026-11-01T00:00:00Z')
16. select stato = lower(State), len(Info) as lunghezza | where lunghezza > 100
17. from stats jobs | where count > 0 | orderby count desc
```

## Output

### `"format": "json"`

- **Senza stadi in memoria**: il payload originale di Orchestrator. Le pagine vengono unite in un unico `value`; campi e valori restano invariati. Il JSON è compatto e con escaping minimo.
- **Con stadi in memoria**: la stessa struttura (`@odata.context`, `@odata.count`, `value`) con le righe trasformate; i nomi dei campi originali restano invariati.
- **Batch**: un oggetto `{ "<id>": <payload> }` nell'ordine della richiesta.
- **`"advanced": { "envelope": true }`**: ogni payload è incapsulato in `{ command, status, durationMs, pages, truncated, pushdown, warnings, error, data }`. Lo schema è pubblicato da `mg3orch schema`. `durationMs` è l'unico campo non deterministico.
- **Errore**: `{ "error": { "code", "exitCode", "meaning", "message", "commandId" } }`.

### `"format": "text"`

```
## jobs list — 37 righe
Id    State    ReleaseName  StartTime
----  -------  -----------  -------------------
1001  Faulted  Ordini       2026-11-01 08:38:00
...
[troncato: mostrate 200 di 1543 — usa take/skip o filtri più stretti]
```

- Stili: `table`, `list` (`chiave: valore`), `markdown`, `csv` (RFC 4180, cultura invariante, senza intestazione di sezione).
- Le date sono convertite in `advanced.timeZone`; le celle oltre `maxCellWidth` terminano con `…`.
- L'output è deterministico e non contiene colori né sequenze di controllo.
- Gli errori diventano una sola riga: `ERRORE (exit code – significato): messaggio`.

`stdout` contiene solo il risultato, oppure l'errore. Gli avvisi vanno su `stderr`; i log solo con `verbosity` `detailed` o `diagnostic`.

### Errori d'uso

Ogni errore indica cosa non va e, quando possibile, il comando corretto:

```
$ mg3orch --auth test
ERRORE (2 – uso o configurazione non validi): '--auth' non è un'opzione valida per 'mg3orch'. Forse intendevi: mg3orch auth test

$ mg3orch jobs
ERRORE (2 – uso o configurazione non validi): 'mg3orch jobs' richiede un sottocomando: list, count, get. Usa 'mg3orch jobs --help' per l'elenco.

$ mg3orch auth test
ERRORE (2 – uso o configurazione non validi): Manca il file di configurazione: aggiungi --config <percorso>. Per crearlo: mg3orch init --config <percorso>.
```

## Richiesta batch

`mg3orch run --request <file|-> --config <file>` legge la richiesta da un file o da `stdin`. Configurazione e formato dell'output vengono dal file indicato con `--config`.

```json
{
  "commands": [
    { "id": "rel",    "command": "releases list" },
    { "id": "faults", "command": "jobs list", "args": { "since": "24h" },
      "query": "where State == 'Faulted' | select Key, ReleaseName, StartTime, Info" },
    { "id": "sum",    "query": "from $faults | groupby ReleaseName | aggregate n = count() | orderby n desc | take 10" }
  ],
  "options": { "stopOnError": false, "parallelism": 4 }
}
```

- `id` è obbligatorio e univoco. Un comando usa il risultato di un altro con `from $id` o `join $id as alias`.
- Le dipendenze formano un grafo aciclico, validato prima dell'esecuzione (un ciclo produce exit code `2`).
- I comandi indipendenti girano in parallelo fino a `parallelism` (1–64). I risultati escono sempre nell'ordine della richiesta.
- `args` accetta `folder` (sostituisce la cartella del file solo per quel comando), `filter`, `select`, `orderby`, `top`, `skip`, `expand`, `since`, `key`.
- Con `stopOnError: true` il primo errore interrompe gli altri comandi, che risultano `skipped`. Un comando la cui dipendenza fallisce risulta sempre `skipped`.

## Exit code e garanzie di terminazione

| Codice | Significato |
|---|---|
| 0 | Successo |
| 1 | Errore non classificato |
| 2 | Uso o configurazione non validi (inclusi scope di scrittura e cicli nel batch) |
| 3 | Autenticazione fallita (401 o errore sul token) |
| 4 | Accesso negato o risorsa non trovata (403/404) |
| 5 | Errore di parsing o semantico della query |
| 6 | Timeout globale superato |
| 7 | Batch con successo parziale |
| 8 | Orchestrator non raggiungibile o risposta 5xx dopo i retry |
| 130 | Esecuzione annullata dall'utente (Ctrl+C o SIGTERM) |

Garanzie:

- nessuna esecuzione supera `advanced.globalTimeoutSeconds`, retry compresi. Allo scadere escono i risultati parziali con `truncated = true` ed exit code `6`;
- `maxItems`, `maxPages` e `maxJoinRows` rendono finite paginazione e join; il loro superamento è segnalato come troncamento, non come errore;
- i retry sono finiti e riguardano solo 408, 429 (rispettando `Retry-After`), 5xx ed errori di rete;
- anche in caso d'errore `stdout` contiene un output valido nel formato richiesto;
- nessuna eccezione non gestita raggiunge l'utente: un handler globale la converte in exit code `1`;
- la CLI non chiede mai input (salvo `init` quando mancano valori), non apre browser e non mostra barre di avanzamento.

## Integrazione con un harness IA

1. Generare il manifest dei tool: `mg3orch schema > mg3orch.tools.json`. Contiene un tool per comando (`jobs_list`, `jobs_get`, `stats_jobs`, `odata_get`, `run`…) con descrizione e JSON Schema dei parametri, la tabella degli exit code e lo schema dell'envelope.
2. Preparare un file di configurazione per l'harness, ad esempio con `"format": "json"`, `"advanced": { "envelope": true, "verbosity": "quiet" }`.
3. Registrare i tool nell'harness. Ogni invocazione diventa una riga di comando, ad esempio `mg3orch jobs list --query "<query>" --config harness.json`.
4. Per più domande in una volta usare `run` con la richiesta su `stdin`:

```bash
echo '{ "commands": [ { "id": "f", "command": "jobs list", "args": { "since": "24h" }, "query": "where State == '"'"'Faulted'"'"' | count" } ] }' \
  | mg3orch run --request - --config harness.json
```

Consigli: `"format": "text"` è il formato più economico in token; l'envelope dà all'agente `truncated` e `warnings` per decidere se affinare la query; l'exit code va sempre controllato.

## Integrazione in un'applicazione .NET

Come processo esterno, si leggono `stdout` (JSON) e l'exit code. Come libreria, si referenzia `Mg3.Orchestrator.Core`:

```csharp
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Core;
using Mg3.Orchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Stesso file usato dalla CLI: lo carica, ricava i default e lo valida.
var profile = new ConfigFileLoader().Load("dev.json", AppContext.BaseDirectory);

await using var provider = new ServiceCollection().AddMg3Orchestrator(profile).BuildServiceProvider();
var service = provider.GetRequiredService<IOrchestratorQueryService>();

var request = new BatchRequest(
    [new CommandRequest("faults", "jobs list", new Dictionary<string, string> { ["since"] = "24h" }, "where State == 'Faulted' | select Key, Info")],
    new BatchOptions(),
    IsBatch: false);

var result = await service.ExecuteAsync(request, CancellationToken.None);
foreach (var row in result.Results[0].Rows) { /* JsonNode */ }
```

Il profilo è fisso per il container. In alternativa al file si può costruire direttamente un `Mg3Profile`; in quel caso va validato con `new Mg3ProfileValidator().Validate(null, profile)`.

## Script PowerShell: processi in errore

[`scripts/mg3orch-menu.ps1`](scripts/mg3orch-menu.ps1) apre un menu con le verifiche più frequenti sui processi falliti. Mostra solo i job in errore, con il messaggio d'errore e la VM su cui sono stati eseguiti.

```powershell
.\scripts\mg3orch-menu.ps1 -Config .\mg3orch.json            # ultime 24 ore
.\scripts\mg3orch-menu.ps1 -Config .\mg3orch.json -Since 7d   # ultimi 7 giorni
```

| Voce | Cosa mostra |
|---|---|
| 1 | Job in errore: ora di fine, processo, VM, messaggio d'errore |
| 2 | Numero di errori per processo e VM |
| 3 | Numero di errori per VM |
| 4 | Messaggi di livello `Error`/`Fatal` scritti dai robot, con processo e VM |
| 5 | Messaggi di errore di un singolo job (numero dall'elenco della voce 1, oppure la sua Key) |
| 6 | Esporta i job in errore in un CSV (separatore `;`) nella cartella corrente |
| 7 | Cambia il periodo (es. `12h`, `7d`) |

Note:
- Funziona con Windows PowerShell 5.1 e con PowerShell 7.
- `mg3orch` viene cercato in `-Exe`, accanto allo script, in `artifacts\win-x64` e nel `PATH`.
- Per leggere l'output, lo script crea una copia temporanea della configurazione con `"format": "json"` nella cartella temporanea dell'utente e la elimina all'uscita.
- Un job è "in errore nelle ultime 24 ore" se è terminato (`EndTime`) nel periodo con stato `Faulted`. I filtri vengono eseguiti da Orchestrator, non in memoria.

## Architettura

```
src/
  Mg3.Orchestrator.Abstractions    contratti e modelli immutabili; nessuna dipendenza
  Mg3.Orchestrator.Catalog         un ResourceDescriptor per risorsa, registrato via DI
  Mg3.Orchestrator.Query           Lexer → Parser (AST) → Planner (pushdown) → Executor
  Mg3.Orchestrator.Infrastructure  HTTP, ReadOnlyGuardHandler, resilienza, auth, paginazione, configurazione
  Mg3.Orchestrator.Output          formatter JSON e testo; un renderer per stile
  Mg3.Orchestrator.Core            risoluzione comandi, DAG del batch, esecuzione; AddMg3Orchestrator
  Mg3.Orchestrator.Cli             System.CommandLine e composition root (eseguibile mg3orch)
tests/
  *.UnitTests                      xUnit + Shouldly (+ Verify per gli snapshot)
  Mg3.Orchestrator.IntegrationTests  WireMock.Net + CLI in-process; nessuna chiamata reale
```

Come si applicano i principi SOLID:

- **S**: parser, planner ed executor sono classi distinte; ogni modalità di autenticazione e ogni formato di output ha la propria classe.
- **O**: risorse (`IResourceDefinition`), stadi (`IStageParser`, `IStageExecutor`), funzioni (`IQueryFunction`), renderer (`ITextRenderer`) e formatter (`IOutputFormatter`) sono strategie registrate. Aggiungerne una significa creare una classe e registrarla, senza modificare `switch` esistenti.
- **L**: ogni implementazione di `IOutputFormatter`, `ITextRenderer` e `IAuthProvider` è sostituibile senza casi speciali nel chiamante.
- **I**: interfacce piccole, ad esempio `ITokenProvider` distinto da `ITokenCache` e `IAuthProvider`.
- **D**: i servizi dipendono dalle astrazioni; le implementazioni si collegano solo in `AddMg3Orchestrator` e nella composition root della CLI.

Catena HTTP verso Orchestrator, dall'esterno verso l'interno: `ReadOnlyGuardHandler` → resilienza (retry, circuit breaker, timeout per tentativo) → `AuthenticationHandler` → `FolderHeadersHandler` → `DiagnosticLoggingHandler`.

## Sviluppo e verifica

```bash
dotnet build -c Release                                   # 0 errori, 0 warning (TreatWarningsAsErrors)
dotnet test                                               # 385 test
dotnet test --settings coverage.runsettings --collect:"XPlat Code Coverage" --results-directory coverage
reportgenerator -reports:"coverage/**/coverage.cobertura.xml" -targetdir:coverage/report -reporttypes:TextSummary
dotnet publish src/Mg3.Orchestrator.Cli -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o artifacts/linux-x64
```

Copertura misurata (righe, codice generato dal compilatore escluso): Query 96,3 %, Core 99,5 %, Output 98,9 %.

La pipeline [CI](.github/workflows/ci.yml) esegue gli stessi passi su ogni pull request e su ogni push su `main`. Ricompila con 0 warning, esegue i test, pubblica il riepilogo della copertura e fallisce sotto l'85 % su Query, Core e Output (controllo in [`build/check-coverage.py`](build/check-coverage.py)). Produce infine gli eseguibili `linux-x64`, `win-x64` e `osx-arm64` come artifact.

Gli snapshot dei formatter sono in `tests/Mg3.Orchestrator.Output.UnitTests/*.verified.txt`. Se un cambiamento dell'output è voluto, il test fallisce e produce un file `*.received.txt` da controllare e rinominare in `*.verified.txt`.

## Dipendenze

| Pacchetto | Motivo |
|---|---|
| `System.CommandLine` | Parsing degli argomenti, help e albero dei comandi generato dal catalogo |
| `Microsoft.Extensions.DependencyInjection` | Composition root e registrazione delle strategie (principio O) |
| `Microsoft.Extensions.Http` | `IHttpClientFactory` e catena di `DelegatingHandler` |
| `Microsoft.Extensions.Http.Resilience` | Retry con backoff e jitter, rispetto di `Retry-After`, circuit breaker, timeout per tentativo |
| `Microsoft.Extensions.Logging(.Console)` | Log su `stderr` regolati da `advanced.verbosity` |
| `Microsoft.Extensions.Options` | `IValidateOptions<T>` per la validazione del profilo |
| `xunit`, `Shouldly` | Test (Shouldly al posto di FluentAssertions, che dalla v8 ha licenza commerciale) |
| `Verify.Xunit` | Snapshot test dei formatter |
| `WireMock.Net` | Orchestrator simulato nei test d'integrazione |
| `coverlet.collector` | Misura della copertura |

## Tracciabilità dei requisiti

I riferimenti § sono al [prompt di generazione](docs/PROMPT_GENERAZIONE.md).

| Requisito | Verifica |
|---|---|
| §2.1 Sola lettura: solo GET + POST del token | `HandlerTests.ReadOnlyGuardBlocksEveryMethodButGet`, `HandlerTests.ReadOnlyGuardAllowsGetAndMarkedTokenPost`, `EndToEndTests.FollowsPaginationAndSendsHeaders` |
| §2.2 Nessuna interazione | Nessun input letto tranne `run --request -`; tutti i test end-to-end girano senza input |
| §2.3 `stdout` solo risultato, diagnostica su `stderr` | `EndToEndTests.WarningsGoToStderrOnly` |
| §2.4 Nessun segreto in uscita | `ExitCodeTests.Exit3AuthenticationFailed`, `EndToEndTests.CatalogSchemaAndConfigCommands` |
| §2.5 SOLID | [Architettura](#architettura) |
| §2.6 Niente codice dinamico | `Mg3.Orchestrator.Query` (parser proprio); `ParserTests` |
| §3.1 Un solo file di configurazione, errori con chiave e chiavi ammesse | `ConfigurationTests`, `ExitCodeTests.Exit2InvalidConfiguration`, `ExitCodeTests.Exit2MissingConfiguration` |
| §3.1 Creazione guidata del file (`init`) | `InitTests` |
| §3.1 Modalità di autenticazione | `AuthTests`, `EndToEndTests.PatAuthenticationSendsTokenWithoutIdentityCall`, `EndToEndTests.LegacyAuthenticationUsesAccountEndpoint` |
| §3.1 Scope solo `*.Read` | `ValidatorTests.WriteScopesAreRejected`, `ExitCodeTests.Exit2InvalidConfiguration`, `InitTests.ValidatesBeforeWriting` |
| §3.1 Header della cartella e del tenant | `HandlerTests.FolderHeadersFollowPriority`, `EndToEndTests.FollowsPaginationAndSendsHeaders` |
| §3.1 Cache del token (memoria, disco `600`) | `AuthTests.TokenProviderCachesUntilExpiry`, `AuthTests.FileTokenCachePersistsWithPrivatePermissions` |
| §3.2 Comandi singoli e opzioni | `CommandResolverTests`, `EndToEndTests.ResourceCommandsCoverOptionsAndActions` |
| §3.2 Errori d'uso chiari con suggerimento | `ExitCodeTests.Exit2ExplainsUsageErrors` |
| §3.2 Comandi di servizio | `EndToEndTests.CatalogSchemaAndConfigCommands`, `EndToEndTests.AuthTestChecksTokenAndFolders`, `EndToEndTests.ODataGetReadsArbitraryPaths` |
| §3.3 Batch, DAG, parallelismo, ordine dei risultati | `BatchTests`, `QueryServiceTests.ReferencesAndJoinsUseOtherResults`, `EndToEndTests.BatchWithDependenciesAndTextOutput` |
| §4 Catalogo e schema aperto | `EndToEndTests.CatalogSchemaAndConfigCommands`; [ASSUMPTIONS.md](ASSUMPTIONS.md) |
| §5.1 Grammatica | `ParserTests` |
| §5.2 Semantica, null, stringhe, date, regex | `ExecutorTests` |
| §5.2 Pushdown ed equivalenza | `PlannerTests`, `PushdownEquivalenceTests` (29 query) |
| §5.2 `--explain` | `QueryServiceTests.ExplainDoesNotRead`, `EndToEndTests.ExplainMakesNoCalls` |
| §6.1 JSON originale, batch, envelope | `JsonOutputTests.RawPayloadIsByteIdenticalToFixture`, `EndToEndTests.RawJsonIsIdenticalToOrchestratorPayload`, `SnapshotTests.JsonEnvelope` |
| §6.2 Testo, stili, troncamento, determinismo | `SnapshotTests`, `JsonOutputTests.CellFormatterConvertsDatesNumbersAndTruncates` |
| §7 Retry, `Retry-After`, nessun retry su 4xx | `EndToEndTests.RetriesOn429HonouringRetryAfter`, `EndToEndTests.DoesNotRetryOn404`, `ExitCodeTests.Exit8UnavailableAfterRetries` |
| §7 Paginazione e limiti | `ReaderTests` |
| §7 Cancellazione e timeout | `QueryServiceTests.GlobalTimeoutProducesExitCode6`, `ReaderTests.ReturnsPartialResultsWhenCancelledAfterFirstPage`, `ExitCodeTests.Exit130Cancelled` |
| §7 Distribuzione e uso come libreria | [Installazione](#installazione), [Integrazione in un'applicazione .NET](#integrazione-in-unapplicazione-net); `QueryServiceTests` usa `AddMg3Orchestrator` |
| §9 Exit code | `ExitCodeTests` (un test per codice) |
| §10 Copertura ≥ 85 % su Query, Core, Output | [Sviluppo e verifica](#sviluppo-e-verifica) |

## Possibili estensioni

- **Server MCP**: un nuovo progetto host che espone `IOrchestratorQueryService` come tool MCP, senza modificare `Core`.
- **Verifica del catalogo**: un comando che legga lo Swagger dell'istanza e segnali endpoint e campi mancanti, aggiornando `Verified`.
- **Pushdown di `count`**: tradurre un `count` finale in `$count=true&$top=0` quando l'intero prefisso è traducibile.
- **Header `X-UIPATH-FolderPath-Encoded`**: per supportare percorsi di cartella non ASCII.
