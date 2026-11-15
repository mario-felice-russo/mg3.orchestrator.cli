# Prompt di generazione — `mg3orch`, CLI di sola lettura per UiPath Orchestrator

> Copia tutto ciò che segue la linea orizzontale e passalo all'agente di generazione del codice.

---

## 1. Ruolo e obiettivo

Sei uno sviluppatore senior .NET. Devi generare da zero, in **C# su .NET 10**, la soluzione `Mg3.Orchestrator.Cli`: una CLI di **sola lettura** che interroga UiPath Orchestrator.

La CLI ha tre compiti:

1. esporre **tutti i comandi di lettura** di Orchestrator;
2. eseguire **query complesse** sui risultati di quegli stessi comandi (filtri, proiezioni, ordinamenti, raggruppamenti, aggregazioni, join tra comandi);
3. restituire **testo leggibile** oppure il **JSON originale** di Orchestrator, in base alla configurazione.

Gli utilizzatori sono due:

- un **harness di agenti IA**, che la invoca come tool. Servono quindi output deterministico, compatto e senza interazione;
- **applicazioni proprietarie**, che la invocano come processo oppure ne usano direttamente la libreria.

## 2. Principi non negoziabili

1. **Sola lettura.** L'unico verbo HTTP consentito verso Orchestrator è `GET`. Unica eccezione: il `POST` verso l'endpoint del token. Il vincolo va imposto da un `DelegatingHandler` che rifiuta qualsiasi altra richiesta. Un test deve verificarlo.
2. **Nessuna interazione.** La CLI non chiede mai input all'utente (unica eccezione: `init`, solo per i valori non passati come opzioni), non apre browser e non mostra spinner o barre di avanzamento.
3. **Separazione dei flussi.** Su `stdout` va solo il risultato. Diagnostica e log vanno su `stderr`.
4. **Nessun segreto in uscita.** Segreti e token non compaiono mai in output, log, messaggi d'errore o dump di configurazione: vanno mascherati con `***`.
5. **SOLID.** Ogni nuova risorsa, stadio di query o formato di output si aggiunge creando una nuova classe registrata nel container DI, senza modificare il codice esistente.
6. **Niente codice dinamico.** Il motore di query usa un parser proprio. È vietato valutare espressioni tramite Roslyn scripting, `DynamicLinq` o reflection su input utente.

## 3. Input

### 3.1 Configurazione

Tutta la configurazione sta in **un solo file JSON**, il cui percorso si indica con `--config <percorso>` (obbligatorio, salvo per `catalog`, `schema` e `--help`). Non ci sono variabili d'ambiente, profili, file predefiniti né opzioni da riga di comando che sovrascrivono il file. Per un altro ambiente si usa un altro file.

Il file è piatto e minimale; obbligatori solo `url` e un tipo di credenziali:

```json
{
  "url": "https://cloud.uipath.com/{org}/{tenant}/orchestrator_/",
  "folderId": 298109,
  "token": "<Personal Access Token>",
  "format": "text",
  "style": "table",
  "advanced": { "timeZone": "Europe/Rome" }
}
```

Regole:

- Le credenziali sono una sola tra: `token` (Personal Access Token); `clientId` + `clientSecret` + `scopes` (applicazione esterna OAuth); `username` + `password` (legacy on‑prem, `POST /api/Account/Authenticate`). La modalità si ricava dalle chiavi presenti; indicarne più di una è un errore.
- `identityUrl` è facoltativo: in cloud vale `https://<host>/identity_/connect/token`, on‑prem `https://<host>/identity/connect/token`.
- Gli scope devono essere esclusivamente `*.Read`. Se ne compare uno di scrittura, la validazione fallisce con exit code `2`.
- La cartella è una sola tra `folderId`, `folderPath`, `folderKey`; si comunica con l'header `X-UIPATH-OrganizationUnitId`, `X-UIPATH-FolderKey` o `X-UIPATH-FolderPath`. Per l'on‑prem multi‑tenant `tenantName` diventa `X-UIPATH-TenantName`.
- Tutte le altre impostazioni (envelope, righe e colonne del testo, fuso orario, cultura, timeout, retry, paginazione, limiti, cache del token, verbosità) stanno nella sezione facoltativa `advanced`, con default sensati.
- Il token va tenuto in cache in memoria per la durata del processo. Una cache su disco è opzionale e il file deve avere permessi `600`.
- La configurazione si valida all'avvio con `IValidateOptions<T>`. Un errore indica la chiave, il motivo e, per le chiavi sconosciute, l'elenco di quelle ammesse.
- `mg3orch init --config <percorso>` crea il file: ricava organizzazione, tenant e cartella (`fid`) dall'URL copiato dal browser, chiede solo i valori mancanti, valida e scrive con permessi `600`. È l'unico comando che può fare domande.

### 3.2 Comandi singoli (modalità CLI)

Sintassi: `mg3orch <risorsa> <azione> [argomenti] [opzioni]`

| Azione | Significato |
|---|---|
| `list` | Elenco paginato della risorsa |
| `get <id\|key\|name>` | Singola entità |
| `count` | Conteggio (`$count`) |

Opzioni di lettura: `--query "<DSL>"`, `--filter "<OData $filter>"` (passthrough grezzo), `--select`, `--orderby`, `--top`, `--skip`, `--expand`, `--since <durata>`. Opzioni globali: `--config <file>`, `--explain`. Formato dell'output e cartella stanno solo nel file.

Gli errori d'uso sono in italiano, su una riga, con il significato dell'exit code e, quando possibile, il comando corretto (es. `mg3orch --auth test` → «Forse intendevi: mg3orch auth test»).

Comandi di servizio:

| Comando | Scopo |
|---|---|
| `init` | Crea il file di configurazione (§3.1) |
| `catalog` | Elenca risorse, azioni, campi noti e campi filtrabili |
| `schema` | Emette il manifest JSON dei tool per l'harness IA: nome, descrizione e JSON Schema dei parametri di ogni comando |
| `config validate` | Valida la configurazione e la stampa con i segreti mascherati |
| `auth test` | Ottiene un token e chiama un endpoint leggero |
| `odata get <percorso-relativo>` | Lettura generica, solo `GET`, su qualsiasi percorso `odata/` o `api/` non presente nel catalogo |
| `run` | Esecuzione batch (§3.3) |

### 3.3 Richiesta batch (modalità harness e applicazioni)

`mg3orch run --request <file|->` legge da file oppure da `stdin`:

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

Regole del batch:

- `id` è obbligatorio e univoco. Un comando può usare il risultato di un altro tramite `from $<id>`. Le dipendenze formano un grafico aciclico, che va validato prima dell'esecuzione: un ciclo produce exit code `2`.
- I comandi indipendenti vengono eseguiti in parallelo, fino al valore di `parallelism`.
- L'ordine dei risultati in uscita è sempre quello della richiesta, a prescindere dall'ordine di esecuzione.

## 4. Catalogo delle risorse

Ogni risorsa è descritta da un `ResourceDescriptor` immutabile, che contiene: nome CLI, alias, percorso, chiave, campi noti con tipo, campi filtrabili lato server, scope richiesto e se è legata a una cartella. Il catalogo minimo è il seguente:

| CLI | Endpoint |
|---|---|
| `jobs` | `odata/Jobs` |
| `releases` (processi) | `odata/Releases` |
| `packages` | `odata/Processes` (+ `GetProcessVersions`) |
| `robots` | `odata/Robots` |
| `machines` | `odata/Machines` |
| `sessions` | `odata/Sessions` |
| `queues` | `odata/QueueDefinitions` |
| `queueitems` | `odata/QueueItems` |
| `queueitemevents` | `odata/QueueItemEvents` |
| `queuestats` | `odata/QueueProcessingRecords/UiPathODataSvc.RetrieveQueuesProcessingStatus` |
| `assets` | `odata/Assets` |
| `folders` | `odata/Folders` |
| `personalworkspaces` | `odata/PersonalWorkspaces` |
| `users`, `roles` | `odata/Users`, `odata/Roles` |
| `environments` | `odata/Environments` (cartelle classiche) |
| `triggers` | `odata/ProcessSchedules` |
| `robotlogs` | `odata/RobotLogs` |
| `auditlogs` | `odata/AuditLogs` |
| `buckets` | `odata/Buckets` |
| `libraries` | `odata/Libraries` |
| `webhooks` | `odata/Webhooks` |
| `calendars` | `odata/Calendars` |
| `alerts` | `odata/Alerts` |
| `tasks` | `odata/Tasks` |
| `credentialstores` | `odata/CredentialStores` |
| `testsets`, `testsetexecutions`, `testcaseexecutions` | omonimi in `odata/` |
| `settings`, `license` | `odata/Settings`, `odata/Settings/UiPath.Server.Configuration.OData.GetLicense` |
| `stats jobs`, `stats sessions` | `api/Stats/GetJobsStats`, `api/Stats/GetSessionsStats` |

Vincoli sul catalogo:

- Il catalogo va verificato sul documento Swagger dell'istanza target. Ogni endpoint non verificabile va segnato `Verified = false` e riportato in `ASSUMPTIONS.md`.
- Il JSON di Orchestrator va trattato come schema aperto (`JsonElement`/`JsonNode`): campi sconosciuti non devono mai causare errori.

## 5. Linguaggio di query (DSL)

### 5.1 Grammatica (EBNF)

```
query      = [ source ] { "|" stage } ;
source     = "from" ( resource | "$" ident ) [ "as" ident ] ;
stage      = where | select | orderby | take | skip | distinct
           | groupby | aggregate | join | flatten | count ;
where      = "where" expr ;
select     = "select" proj { "," proj } ;
proj       = expr [ "as" ident ] | ident "=" expr ;
orderby    = "orderby" expr [ "asc" | "desc" ] { "," expr [ "asc" | "desc" ] } ;
take       = "take" int ;        skip = "skip" int ;
distinct   = "distinct" [ field { "," field } ] ;
groupby    = "groupby" field { "," field } ;
aggregate  = "aggregate" ident "=" agg { "," ident "=" agg } ;
agg        = ( "count" | "sum" | "avg" | "min" | "max" | "countif" ) "(" [ expr ] ")" ;
join       = [ "left" ] "join" ( resource | "$" ident ) "as" ident "on" field "==" field ;
flatten    = "flatten" field ;
count      = "count" ;
expr       = or ;   (* precedenza: or < and < not < confronto < additivo < unario *)
cmp-op     = "==" | "!=" | "<" | "<=" | ">" | ">=" | "in" | "contains"
           | "startswith" | "endswith" | "matches" | "is null" | "is not null" ;
literal    = string | number | bool | null | datetime | duration | list ;
functions  = now() | today() | date(s) | lower(x) | upper(x) | len(x)
           | coalesce(a,b) | year(d) | month(d) | day(d) | hour(d) ;
```

### 5.2 Semantica

- **Campi.** Si accede con notazione puntata (`Robot.Name`) o con alias (`j.ReleaseName`). Un campo assente vale `null` e non genera errore. Un campo assente da tutte le righe genera un warning su `stderr`.
- **Stringhe.** Il confronto è case-insensitive e ordinale, per coerenza con il comportamento lato server.
- **Date.** Si usano letterali ISO 8601 e durate come `30m`, `24h`, `7d`. È ammessa l'aritmetica `now() - 7d`.
- **`matches`.** Usa `Regex` con timeout di 100 ms per espressione.
- **Pushdown.** Il planner traduce in parametri OData (`$filter`, `$select`, `$orderby`, `$top`, `$skip`, `$expand`) il prefisso più lungo di stadi traducibili su una sorgente diretta. Tutto il resto si esegue in memoria. Il piano risultante si ottiene con `--explain`, che restituisce il piano e non esegue nulla.
- **Equivalenza del pushdown.** Il risultato con pushdown attivo deve coincidere con quello ottenuto disattivandolo (`--no-pushdown`). Un test parametrico sulle fixture deve dimostrarlo.
- **Join.** È un hash join in memoria e rispetta `advanced.maxJoinRows`.
- **Errori.** Gli errori di parsing riportano posizione, token atteso e token trovato, ed escono con codice `5`.

## 6. Output

### 6.1 `format = json`

- **Senza stadi lato client:** si restituisce il payload originale di Orchestrator (`@odata.context`, `@odata.count`, `value`). Se ci sono più pagine, si concatenano in un unico `value`, conservando campi e valori senza alcuna modifica.
- **Con stadi lato client:** stessa struttura, ma `value` contiene le righe trasformate. I nomi dei campi originali restano invariati.
- **Batch:** si restituisce un oggetto `{ "<id>": <payload> }` nell'ordine della richiesta.
- **`advanced.envelope = true`:** ogni payload viene incapsulato in `{ "command", "status", "durationMs", "pages", "truncated", "pushdown", "warnings", "error", "data" }`. Lo schema dell'envelope è pubblicato da `mg3orch schema`.
- La serializzazione usa `System.Text.Json` con source generator. Il JSON originale non viene mai deserializzato in DTO tipizzati e poi riserializzato.

### 6.2 `format = text`

- Stili: `table`, con colonne allineate e celle troncate a `maxCellWidth` con `…`; `list`, nella forma `chiave: valore` per record; `markdown`; `csv`.
- Ogni sezione si apre con un'intestazione che indica comando e numero di righe, ad esempio `## faults — 37 righe`.
- In caso di troncamento si aggiunge la riga `[troncato: mostrate 200 di 1543 — usa take/skip o filtri più stretti]`.
- Date espresse in `advanced.timeZone`. Numeri formattati secondo `advanced.culture`, ma in `csv` si usa sempre la cultura invariante.
- L'output è deterministico: a parità di dati, l'output è identico byte per byte. Niente colori se `stdout` non è un terminale o se è impostato `NO_COLOR`.

## 7. Requisiti non funzionali

- **HTTP.** `IHttpClientFactory` con `Microsoft.Extensions.Http.Resilience`. Retry con backoff esponenziale e jitter solo su 408, 429 (rispettando `Retry-After`), 5xx ed errori di rete, fino a `maxRetries`. Circuit breaker per singola esecuzione.
- **Paginazione.** Tramite `$top`/`$skip`, finché la pagina restituita è più corta della richiesta oppure si raggiunge `@odata.count`, `maxItems` o `maxPages`.
- **Cancellazione.** Un `CancellationToken` collegato a `Ctrl+C`/SIGTERM e a `globalTimeoutSeconds` attraversa tutti i livelli.
- **Logging.** `ILogger` su `stderr`. Livello impostato con `advanced.verbosity` (`quiet`, `normal`, `detailed`, `diagnostic`); con `normal` su `stderr` vanno solo gli avvisi. A livello `diagnostic` si registra la URL con la query string, ma mai gli header di autorizzazione.
- **Distribuzione.** Come eseguibile self-contained single-file (win-x64, linux-x64, osx-arm64) e come `dotnet tool` (`ToolCommandName=mg3orch`).
- **Libreria.** Il progetto `Mg3.Orchestrator.Core` deve essere utilizzabile senza la CLI, tramite `services.AddMg3Orchestrator(config)` e `IOrchestratorQueryService.ExecuteAsync(request, ct)`.

## 8. Architettura (SOLID)

```
src/
  Mg3.Orchestrator.Abstractions   // contratti: IResourceCatalog, IOrchestratorReader, IQueryParser,
                                  // IQueryPlanner, IQueryExecutor, IOutputFormatter, IAuthProvider,
                                  // ICommandHandler<TRequest>, modelli immutabili (record)
  Mg3.Orchestrator.Core           // orchestrazione batch, risoluzione DAG, IOrchestratorQueryService
  Mg3.Orchestrator.Infrastructure // HTTP, auth (una classe per modalità), paginazione, ReadOnlyGuardHandler
  Mg3.Orchestrator.Catalog        // un ResourceDescriptor per risorsa; registrazione tramite DI
  Mg3.Orchestrator.Query          // Lexer, Parser → AST, Binder, Planner (pushdown), Executor
  Mg3.Orchestrator.Output         // un IOutputFormatter per formato e stile
  Mg3.Orchestrator.Cli            // System.CommandLine, composition root, mappatura exit code
tests/
  *.UnitTests                     // xUnit + FluentAssertions (o Shouldly)
  Mg3.Orchestrator.IntegrationTests // WireMock.Net con fixture JSON realistiche; nessuna chiamata reale
```

Come ciascun principio si applica:

- **S.** Parser, planner ed executor sono classi distinte. Ogni modalità di autenticazione è una classe distinta.
- **O.** Risorse, stadi DSL, funzioni e formatter sono registrati come strategie. Aggiungerne uno non richiede di modificare `switch` esistenti.
- **L.** Ogni `IOutputFormatter` e ogni `IAuthProvider` è sostituibile senza comportamenti speciali a carico del chiamante.
- **I.** Le interfacce sono piccole: ad esempio `ITokenProvider` è separato da `ITokenCache`.
- **D.** La CLI dipende solo da `Abstractions`. Le implementazioni si collegano esclusivamente nella composition root.

Altri vincoli sul codice: `Nullable` abilitato, `TreatWarningsAsErrors=true`, analyzer .NET a livello `latest-recommended`, `Directory.Build.props` e `Directory.Packages.props` (gestione centralizzata dei pacchetti), `.editorconfig`. Dipendenze ridotte al minimo e ciascuna motivata in `README.md`.

## 9. Vincoli di terminazione a runtime

La CLI termina **sempre**, entro limiti noti, con un exit code documentato.

| Codice | Significato |
|---|---|
| 0 | Successo |
| 1 | Errore non classificato |
| 2 | Uso o configurazione non validi (inclusi scope di scrittura e cicli nel batch) |
| 3 | Autenticazione fallita (401 o errore sul token) |
| 4 | Accesso negato o risorsa non trovata (403/404) |
| 5 | Errore di parsing o semantico della query |
| 6 | Timeout globale superato |
| 7 | Batch con successo parziale (`stopOnError = false` e almeno un comando fallito) |
| 8 | Orchestrator non raggiungibile o risposta 5xx dopo i retry |
| 130 | Esecuzione annullata dall'utente (SIGINT) |

Garanzie:

- Nessuna esecuzione supera `globalTimeoutSeconds`, retry compresi. Allo scadere, la CLI emette i risultati parziali con `truncated = true` ed esce con codice `6`.
- I limiti `maxItems`, `maxPages` e `maxJoinRows` rendono finite paginazione e join. Il loro superamento è segnalato come troncamento, non come errore.
- I retry sono finiti e non si eseguono retry su 4xx diversi da 408 e 429.
- In caso d'errore, `stdout` contiene comunque un output valido nel formato richiesto: in `json` un oggetto `{ "error": { "code", "exitCode", "meaning", "message", "commandId" } }`, in `text` una sola riga `ERRORE (codice – significato): messaggio`. L'errore non va ripetuto su `stderr`; il dettaglio tecnico compare lì solo con `verbosity` `detailed` o `diagnostic`.
- Nessuna eccezione non gestita raggiunge l'utente: un handler globale la converte in exit code `1`.
- Il processo rilascia tutte le risorse e non lascia thread o processi figli attivi dopo l'uscita.

## 10. Test richiesti

- **Parser:** casi validi e casi non validi con posizione dell'errore; round-trip AST → testo → AST.
- **Planner:** pushdown atteso per ogni stadio; test di equivalenza con e senza pushdown su almeno 20 query.
- **Executor:** where, groupby, aggregate, join (inner e left), flatten, null-safety, date e durate, timeout delle regex.
- **Infrastruttura:** paginazione (pagine multiple, limiti), retry su 429 con `Retry-After`, header della cartella, i tre flussi di autenticazione, `ReadOnlyGuardHandler` che blocca POST, PUT, PATCH e DELETE.
- **Output:** snapshot test (Verify) per ogni formato e stile; JSON grezzo identico byte per byte al payload della fixture quando non ci sono stadi lato client.
- **CLI end-to-end:** ogni exit code della tabella §9 è prodotto da almeno un test.
- **Copertura:** almeno l'85 % delle righe su `Query`, `Core` e `Output`.

## 11. Fuori ambito

Qualsiasi operazione di scrittura (avvio job, modifica code, asset e simili), interfaccia grafica, server MCP, persistenza su database. Il server MCP è un'estensione futura: l'architettura deve permetterne l'aggiunta come nuovo progetto host, senza modificare `Core`.

## 12. Vincoli di terminazione della generazione (Definition of Done)

L'agente considera il lavoro **concluso** solo quando tutte queste verifiche danno esito positivo, e in quel momento si ferma:

1. `dotnet build -c Release` termina con 0 errori e 0 warning.
2. `dotnet test` termina con tutti i test verdi e la copertura richiesta al §10.
3. `dotnet publish src/Mg3.Orchestrator.Cli -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true` termina con 0 errori e 0 warning.
4. `mg3orch schema`, `mg3orch catalog` e `mg3orch config validate` funzionano contro WireMock.
5. Nel codice non restano `TODO`, `NotImplementedException`, codice commentato o segreti.
6. Sono presenti `README.md` (installazione, configurazione, riferimento della DSL con almeno 15 esempi, exit code, integrazione con un harness IA e con un'applicazione .NET), `ASSUMPTIONS.md` e `CHANGELOG.md`.
7. Ogni requisito dei §2–§9 è collegato ad almeno un test o a una sezione del README, in una tabella di tracciabilità dentro `README.md`.

Condizioni di **arresto anticipato**:

- Se una verifica fallisce dopo **3 tentativi di correzione** sullo stesso problema, fermati. Riporta l'errore esatto, le ipotesi fatte e le opzioni possibili. Non aggirare il problema disattivando test, warning o analyzer.
- Non inventare endpoint, campi o comportamenti di Orchestrator. Se un'informazione non è verificabile, applica l'ipotesi più conservativa, segnala l'endpoint come non verificato e registra l'ipotesi in `ASSUMPTIONS.md`.
- Non ampliare l'ambito oltre quanto descritto. Le idee aggiuntive vanno nella sezione "Possibili estensioni" del README, senza implementarle.
- Non chiedere chiarimenti durante la generazione: decidi, documenta in `ASSUMPTIONS.md` e prosegui.

**Formato della consegna finale:** albero dei file, comandi di verifica eseguiti con il loro esito, elenco delle ipotesi, eventuali limiti noti. Nient'altro.
