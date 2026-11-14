# Ipotesi e decisioni

Questo file raccoglie le ipotesi fatte durante la generazione, dove il prompt o la documentazione disponibile non bastavano a decidere. Per ciascuna è indicata la scelta, sempre la più conservativa.

## Catalogo ed endpoint

1. **Nessun endpoint è stato verificato su uno Swagger reale.** L'ambiente di generazione non aveva accesso a un'istanza di Orchestrator. Percorsi e campi vengono dalla documentazione pubblica dell'API OData di Orchestrator e sono tutti marcati `Verified = false` (visibile con `mg3orch catalog`). Prima dell'uso in produzione vanno confrontati con `/swagger/v{n}/swagger.json` dell'istanza target, impostando `Verified = true` nei descrittori controllati.
2. Endpoint con il rischio maggiore di differenze tra versioni: `QueueItemEvents`, `QueueProcessingRecords/UiPathODataSvc.RetrieveQueuesProcessingStatus`, `Settings/UiPath.Server.Configuration.OData.GetLicense`, `api/Stats/GetJobsStats`, `api/Stats/GetSessionsStats`, `Processes/UiPath.Server.Configuration.OData.GetProcessVersions`.
3. **Campi filtrabili.** Solo i campi elencati nel catalogo con `Filterable = true` vengono tradotti in `$filter`/`$orderby`. Un campo non elencato funziona comunque, ma viene filtrato in memoria. È una scelta prudente: un campo non filtrabile sul server farebbe fallire la richiesta.
4. **Letterali GUID** in `$filter` vengono scritti senza apici (`Key eq 0000...`), come prevede OData v4.
5. **Enum** in `$filter` vengono scritti come stringhe (`State eq 'Faulted'`), forma accettata da Orchestrator.
6. **Scope OAuth** dei descrittori (`OR.Jobs.Read`, `OR.Monitoring.Read`…) sono indicativi e servono solo come documentazione in `catalog`. La CLI non li richiede automaticamente: gli scope sono quelli configurati in `auth.scopes`.
7. `stats jobs` e `stats sessions` sono marcati come legati a una cartella: l'header viene inviato se configurato ed è innocuo se ignorato.

## Semantica della DSL

8. **Stringhe case-insensitive**, per coerenza con le collation tipiche di SQL Server usate da Orchestrator. L'ordinamento in memoria usa il confronto ordinale case-insensitive. Per testi con caratteri accentati o simboli, l'ordine prodotto dal server (`$orderby` in pushdown) potrebbe differire leggermente da quello in memoria. Per questo i confronti d'ordine (`<`, `>`) sui testi non vengono mai tradotti in OData; l'`orderby` sui testi invece sì, perché è molto utile e la differenza riguarda solo casi limite.
9. **Valori null in ordinamento**: compaiono per primi in ordine crescente, come in SQL Server.
10. **Date**: una stringa viene trattata come data solo se ha forma ISO 8601 (`aaaa-mm-gg[Thh:mm[:ss[.f]]][Z|±hh:mm]`). Le date senza fuso sono considerate UTC.
11. **Numeri** interni in `decimal`. Un numero JSON fuori dal range di `decimal` (es. `1e400`) vale `null` nelle espressioni, ma resta invariato nel JSON originale.
12. **Nomi delle colonne**: un campo annidato proiettato con `select` o `groupby` mantiene il nome puntato (`Robot.Name`, `r.ProcessKey`) e può essere referenziato così negli stadi successivi. L'alias della sorgente (`from jobs as j`) viene rimosso dai nomi (`j.Id` → `Id`).
13. **`groupby` senza `aggregate`** produce le chiavi e un campo `count`.
14. **`distinct` con campi** si comporta come in SQL: proietta i campi e poi rimuove i duplicati.
15. **Regex con timeout**: superare i 100 ms è un errore di query (exit code `5`), non un semplice "non corrisponde".
16. **Equivalenza del pushdown**: il test simula la parte spinta sul server applicandola con l'executor in memoria, nell'ordine di OData. Verifica quindi la correttezza della **scomposizione** del piano; la correttezza delle stringhe OData generate è verificata separatamente in `PlannerTests`.

## Configurazione e HTTP

17. **`--folder`** sostituisce l'intera cartella del profilo (id, key e path), così che un id nel file non abbia la precedenza su un percorso passato da riga di comando.
18. **Percorsi di cartella non ASCII** non sono ammessi negli header HTTP: la validazione chiede di usare `folder.id` o `folder.key`.
19. **Durata del token legacy**: `api/Account/Authenticate` non restituisce la scadenza; si assumono 30 minuti. Per i PAT si assume un anno (la scadenza reale è gestita dal server, che risponderà 401).
20. **Errore 400** di Orchestrator (ad esempio un `--filter` scritto male) produce exit code `2` (uso non valido); uno status non previsto (es. 418) produce `1`.
21. **Circuit breaker**: soglie alte (90 % di errori su almeno 20 richieste in 60 s), perché una singola esecuzione della CLI fa poche richieste; serve soprattutto a fermare raffiche di errori in batch molto grandi.
22. **Paginazione**: `$count=true` viene richiesto alla prima pagina per conoscere il totale e segnalare correttamente il troncamento. Le letture non paginate (`get`, funzioni, `odata get`) non seguono le pagine; con `odata get` si usano `--top`/`--skip`.
23. **Cancellazione a metà paginazione**: se il timeout globale scade dopo aver ricevuto almeno una pagina, i dati parziali vengono restituiti con `truncated = true` ed exit code `6`.

## Output

24. **JSON compatto** con `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, per restare il più vicino possibile ai byte originali (niente `é` al posto di `é`). Il JSON viene riserializzato da `JsonNode`: i numeri mantengono il testo originale; eventuali escape facoltativi dell'originale (es. `\/`) possono differire.
25. **Testo**: date convertite nel formato `aaaa-MM-gg HH:mm:ss`; caratteri di controllo (a capo, tab) sostituiti da spazi; oggetti e array mostrati come JSON compatto.
26. **`durationMs`** nell'envelope è l'unico campo non deterministico.

## Strumenti

27. **Shouldly** al posto di FluentAssertions, che dalla versione 8 ha licenza commerciale.
28. **xUnit 2** e **Microsoft.Extensions 10.x**, allineati a .NET 10. Le versioni 11.x disponibili su NuGet sono pensate per .NET 11.
29. **Copertura**: il codice generato dal compilatore (`[GeneratedRegex]`) è escluso dalla misura tramite `coverage.runsettings`, perché non è scritto a mano.
30. **Publish AOT** rimosso dai requisiti su richiesta: la distribuzione è self-contained single-file.
