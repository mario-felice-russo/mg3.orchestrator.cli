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
11. [Architettura](#architettura)
12. [Sviluppo e verifica](#sviluppo-e-verifica)
13. [Dipendenze](#dipendenze)
14. [Tracciabilità dei requisiti](#tracciabilità-dei-requisiti)
15. [Possibili estensioni](#possibili-estensioni)

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
cp mg3orch.sample.json mg3orch.json          # poi modificare baseUrl, cartella e credenziali
export UIPATH_CLIENT_ID=...  UIPATH_CLIENT_SECRET=...

mg3orch config validate                       # controlla la configurazione (segreti mascherati)
mg3orch auth test                             # ottiene un token e chiama Orchestrator
mg3orch jobs list --since 24h -q "where State == 'Faulted' | select Key, ReleaseName, Info"
mg3orch stats jobs -o json
```

## Configurazione

### Strati

La configurazione effettiva si ottiene fondendo quattro strati, dal meno al più prioritario:

1. **file JSON**: `--config <path>`, altrimenti la variabile `MG3ORCH_CONFIG`, altrimenti `./mg3orch.json`;
2. **variabili d'ambiente** nella forma `MG3ORCH__<Sezione>__<Chiave>`, ad esempio `MG3ORCH__Output__Format=json`;
3. **opzioni da riga di comando**: `--folder`, `--output`, `--text-style`, `--envelope`;
4. **blocco `config` della richiesta batch** (vedi [Richiesta batch](#richiesta-batch)).

Il file può contenere più **profili** sotto `profiles`; si sceglie quale usare con `--profile`, con `MG3ORCH_PROFILE` o con `config.profile` nella richiesta batch. Il profilo predefinito è `default`. Un file senza `profiles` viene trattato come un unico profilo.

Le chiavi non distinguono maiuscole e minuscole. Una chiave sconosciuta o un valore del tipo sbagliato producono un errore che indica la chiave (exit code `2`). Un esempio completo è in [`mg3orch.sample.json`](mg3orch.sample.json).

### Chiavi

| Chiave | Default | Significato |
|---|---|---|
| `orchestrator.baseUrl` | — | URL di Orchestrator. Cloud: `https://cloud.uipath.com/{org}/{tenant}/orchestrator_/` |
| `orchestrator.tenantName` | — | Tenant on‑prem; inviato come `X-UIPATH-TenantName` |
| `orchestrator.folder.id` / `.key` / `.path` | — | Cartella. Priorità: id → key → path |
| `auth.mode` | `ClientCredentials` | `ClientCredentials`, `Pat` oppure `Legacy` |
| `auth.identityUrl` | — | Endpoint del token per `ClientCredentials` (cloud: `https://cloud.uipath.com/identity_/connect/token`) |
| `auth.clientId`, `auth.clientSecret` | — | Credenziali dell'applicazione esterna |
| `auth.scopes` | — | Scope separati da spazi; **solo** scope `*.Read` |
| `auth.token` | — | Personal Access Token (`Pat`) |
| `auth.username`, `auth.password` | — | Credenziali `Legacy` (on‑prem, `api/Account/Authenticate`) |
| `auth.tokenCache` / `auth.tokenCachePath` | `Memory` | `Disk` attiva una cache su file con permessi `600` (default `~/.mg3orch/token-cache.json`) |
| `http.timeoutSeconds` | 30 | Timeout di ogni tentativo HTTP |
| `http.maxRetries` | 3 | Tentativi aggiuntivi su 408, 429, 5xx ed errori di rete |
| `http.retryBaseDelayMs` | 500 | Attesa base del backoff esponenziale con jitter |
| `http.pageSize` | 500 | Elementi per pagina (`$top`), massimo 1000 |
| `limits.maxItems` | 1000 | Elementi massimi letti per comando |
| `limits.maxPages` | 50 | Pagine massime lette per comando |
| `limits.globalTimeoutSeconds` | 120 | Durata massima dell'intera esecuzione, retry compresi |
| `limits.maxJoinRows` | 100000 | Righe massime prodotte da un join |
| `output.format` | `text` | `text` oppure `json` |
| `output.textStyle` | `table` | `table`, `list`, `markdown` oppure `csv` |
| `output.envelope` | `false` | Incapsula ogni risultato con metadati |
| `output.maxRows` | 200 | Righe mostrate in testo (il JSON non viene troncato) |
| `output.maxCellWidth` | 80 | Larghezza massima di una cella in testo |
| `output.timeZone` | `UTC` | Fuso orario delle date nel testo |
| `output.culture` | `it-IT` | Formato dei numeri nel testo (`csv` usa sempre la cultura invariante) |

### Segreti

Un valore nella forma `env:NOME` viene letto dalla variabile d'ambiente `NOME`; un valore nella forma `file:percorso` dal contenuto del file indicato. I segreti (`clientSecret`, `token`, `password`) non compaiono mai in output, log o messaggi d'errore: `config validate` li mostra come `***`.

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

**Opzioni globali**: `--config`, `--profile`, `--folder`, `--output/-o`, `--text-style`, `--envelope`, `--verbosity quiet|normal|detailed|diagnostic`, `--explain`, `--no-pushdown`.

**Comandi di servizio**:

| Comando | Scopo |
|---|---|
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

`--explain` mostra il piano senza eseguire nulla; `--no-pushdown` esegue tutto in memoria.

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

### `--output json`

- **Senza stadi in memoria**: il payload originale di Orchestrator. Le pagine vengono unite in un unico `value`; campi e valori restano invariati. Il JSON è compatto e con escaping minimo.
- **Con stadi in memoria**: la stessa struttura (`@odata.context`, `@odata.count`, `value`) con le righe trasformate; i nomi dei campi originali restano invariati.
- **Batch**: un oggetto `{ "<id>": <payload> }` nell'ordine della richiesta.
- **`--envelope`**: ogni payload è incapsulato in `{ command, status, durationMs, pages, truncated, pushdown, warnings, error, data }`. Lo schema è pubblicato da `mg3orch schema`. `durationMs` è l'unico campo non deterministico.
- **Errore**: `{ "error": { "code", "exitCode", "message", "commandId" } }`.

### `--output text`

```
## jobs list — 37 righe
Id    State    ReleaseName  StartTime
----  -------  -----------  -------------------
1001  Faulted  Ordini       2026-11-01 08:38:00
...
[troncato: mostrate 200 di 1543 — usa take/skip o filtri più stretti]
```

- Stili: `table`, `list` (`chiave: valore`), `markdown`, `csv` (RFC 4180, cultura invariante, senza intestazione di sezione).
- Le date sono convertite in `output.timeZone`; le celle oltre `maxCellWidth` terminano con `…`.
- L'output è deterministico e non contiene colori né sequenze di controllo.
- Gli errori diventano `ERRORE [exit code]: messaggio`.

`stdout` contiene solo il risultato. Avvisi, errori dettagliati e log vanno su `stderr`.

## Richiesta batch

`mg3orch run --request <file|->` legge la richiesta da un file o da `stdin`:

```json
{
  "config": { "profile": "default", "output": { "format": "json" } },
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
- `args` accetta `folder`, `filter`, `select`, `orderby`, `top`, `skip`, `expand`, `since`, `key`.
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

- nessuna esecuzione supera `limits.globalTimeoutSeconds`, retry compresi. Allo scadere escono i risultati parziali con `truncated = true` ed exit code `6`;
- `maxItems`, `maxPages` e `maxJoinRows` rendono finite paginazione e join; il loro superamento è segnalato come troncamento, non come errore;
- i retry sono finiti e riguardano solo 408, 429 (rispettando `Retry-After`), 5xx ed errori di rete;
- anche in caso d'errore `stdout` contiene un output valido nel formato richiesto;
- nessuna eccezione non gestita raggiunge l'utente: un handler globale la converte in exit code `1`;
- la CLI non chiede mai input, non apre browser e non mostra barre di avanzamento.

## Integrazione con un harness IA

1. Generare il manifest dei tool: `mg3orch schema > mg3orch.tools.json`. Contiene un tool per comando (`jobs_list`, `jobs_get`, `stats_jobs`, `odata_get`, `run`…) con descrizione e JSON Schema dei parametri, la tabella degli exit code e lo schema dell'envelope.
2. Registrare i tool nell'harness. Ogni invocazione diventa una riga di comando, ad esempio `mg3orch jobs list --query "<query>" --output text`.
3. Per più domande in una volta usare `run` con la richiesta su `stdin`:

```bash
echo '{ "commands": [ { "id": "f", "command": "jobs list", "args": { "since": "24h" }, "query": "where State == '"'"'Faulted'"'"' | count" } ] }' \
  | mg3orch run --request - --output json --envelope --verbosity quiet
```

Consigli: `--output text` è il formato più economico in token; `--envelope` dà all'agente `truncated` e `warnings` per decidere se affinare la query; l'exit code va sempre controllato.

## Integrazione in un'applicazione .NET

Come processo esterno, si leggono `stdout` (JSON) e l'exit code. Come libreria, si referenzia `Mg3.Orchestrator.Core`:

```csharp
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Core;
using Microsoft.Extensions.DependencyInjection;

var profile = new Mg3Profile
{
    Orchestrator = new OrchestratorSettings { BaseUrl = "https://cloud.uipath.com/org/tenant/orchestrator_/", Folder = new FolderSettings { Path = "Shared" } },
    Auth = new AuthSettings { Mode = AuthMode.Pat, Token = Environment.GetEnvironmentVariable("UIPATH_PAT") },
};

await using var provider = new ServiceCollection().AddMg3Orchestrator(profile).BuildServiceProvider();
var service = provider.GetRequiredService<IOrchestratorQueryService>();

var request = new BatchRequest(
    [new CommandRequest("faults", "jobs list", new Dictionary<string, string> { ["since"] = "24h" }, "where State == 'Faulted' | select Key, Info")],
    new BatchOptions(),
    IsBatch: false);

var result = await service.ExecuteAsync(request, CancellationToken.None);
foreach (var row in result.Results[0].Rows) { /* JsonNode */ }
```

In modalità libreria il profilo è fisso per il container: il blocco `config` di `BatchRequest` è usato solo dalla CLI. Il profilo non viene validato automaticamente e i riferimenti `env:`/`file:` non vengono risolti. Per ottenere lo stesso comportamento della CLI si carica il profilo con `new ProfileLoader().Load(...)` (namespace `Mg3.Orchestrator.Infrastructure.Configuration`), che fonde gli strati, risolve i segreti e valida.

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
dotnet test                                               # 354 test
dotnet test --settings coverage.runsettings --collect:"XPlat Code Coverage" --results-directory coverage
reportgenerator -reports:"coverage/**/coverage.cobertura.xml" -targetdir:coverage/report -reporttypes:TextSummary
dotnet publish src/Mg3.Orchestrator.Cli -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o artifacts/linux-x64
```

Copertura misurata (righe, codice generato dal compilatore escluso): Query 96,9 %, Core 99,3 %, Output 98,4 %.

Gli snapshot dei formatter sono in `tests/Mg3.Orchestrator.Output.UnitTests/*.verified.txt`. Se un cambiamento dell'output è voluto, il test fallisce e produce un file `*.received.txt` da controllare e rinominare in `*.verified.txt`.

## Dipendenze

| Pacchetto | Motivo |
|---|---|
| `System.CommandLine` | Parsing degli argomenti, help e albero dei comandi generato dal catalogo |
| `Microsoft.Extensions.DependencyInjection` | Composition root e registrazione delle strategie (principio O) |
| `Microsoft.Extensions.Http` | `IHttpClientFactory` e catena di `DelegatingHandler` |
| `Microsoft.Extensions.Http.Resilience` | Retry con backoff e jitter, rispetto di `Retry-After`, circuit breaker, timeout per tentativo |
| `Microsoft.Extensions.Logging(.Console)` | Log su `stderr` regolati da `--verbosity` |
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
| §3.1 Strati, profili, chiavi case-insensitive, errori con chiave | `ConfigurationTests` |
| §3.1 Modalità di autenticazione | `AuthTests`, `EndToEndTests.PatAuthenticationSendsTokenWithoutIdentityCall`, `EndToEndTests.LegacyAuthenticationUsesAccountEndpoint` |
| §3.1 Scope solo `*.Read` | `ValidatorTests.WriteScopesAreRejected`, `ExitCodeTests.Exit2InvalidConfiguration` |
| §3.1 Segreti `env:` / `file:` | `ConfigurationTests.LoadsDefaultFileAndResolvesSecrets`, `ConfigurationTests.ProfileSelectionPrefersRequestThenCliThenEnvironment` |
| §3.1 Header della cartella e del tenant | `HandlerTests.FolderHeadersFollowPriority`, `EndToEndTests.FollowsPaginationAndSendsHeaders` |
| §3.1 Cache del token (memoria, disco `600`) | `AuthTests.TokenProviderCachesUntilExpiry`, `AuthTests.FileTokenCachePersistsWithPrivatePermissions` |
| §3.2 Comandi singoli e opzioni | `CommandResolverTests`, `EndToEndTests.ResourceCommandsCoverOptionsAndActions` |
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
