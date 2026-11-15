namespace Mg3.Orchestrator.Cli.Interactive;

/// <summary>Aiuto in linea, mostrato dal menu (voce Aiuto) e da <c>mg3orch help</c>.</summary>
public static class HelpText
{
    public const string Text = """
        mg3orch - consultazione di UiPath Orchestrator (sola lettura)

        MENU (mg3orch senza comandi)
          1  Job in errore: ora di fine, processo, VM e messaggio d'errore.
          2  Numero di job in errore per processo e VM.
          3  Numero di job in errore per VM.
          4  Messaggi di livello Error/Fatal scritti dai robot, con processo e VM.
          5  Messaggi di errore di un job: indica il numero mostrato dalla voce 1 oppure la Key.
          6  Esporta i job in errore in un file CSV nella cartella corrente.
          7  Cambia il periodo esaminato (default 24h; es. 12h, 7d).
          8  Questo aiuto.
          0  Esci.
          Un job è "in errore nel periodo" se è terminato nel periodo con stato Faulted.

        CONFIGURAZIONE
          Tutto sta in un file JSON. Senza --config si usa mg3orch.json della cartella corrente
          o di quella del programma; se non esiste, mg3orch lo crea chiedendo i dati (init).
            mg3orch init --config mg3orch.json --url "<URL copiato dal browser>" --token <PAT>
            mg3orch auth test --config mg3orch.json
            mg3orch config validate --config mg3orch.json

        COMANDI PRINCIPALI
          mg3orch <risorsa> list|get|count [--query "<query>"] --config <file>
            mg3orch jobs list --since 24h -q "where State == 'Faulted' | select ReleaseName, HostMachineName, Info" -c mg3orch.json
            mg3orch queues list -c mg3orch.json
            mg3orch stats jobs -c mg3orch.json
          mg3orch catalog            elenco delle risorse e dei campi
          mg3orch run --request -    più comandi in una richiesta JSON letta da stdin
          mg3orch <comando> --help   opzioni di un comando

        QUERY (pipeline separata da |)
          where, select, orderby, take, skip, distinct, groupby, aggregate, join, flatten, count
            where State in ('Faulted', 'Stopped') and EndTime >= now() - 24h
            groupby ReleaseName | aggregate n = count() | orderby n desc

        EXIT CODE
          0 ok  1 errore non classificato  2 uso o configurazione  3 autenticazione
          4 accesso negato / non trovato  5 query  6 timeout  7 batch parziale
          8 Orchestrator non raggiungibile  130 annullato

        Documentazione completa: README.md del repository.
        """;
}
