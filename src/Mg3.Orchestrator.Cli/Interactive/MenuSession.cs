using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Abstractions.Output;
using Mg3.Orchestrator.Cli.Hosting;

namespace Mg3.Orchestrator.Cli.Interactive;

/// <summary>Stato condiviso dalle voci del menu e utilità per eseguire query e mostrarne il risultato.</summary>
public sealed class MenuSession(
    CliEnvironment environment,
    IOrchestratorQueryService service,
    IOutputFormatter textFormatter,
    Mg3Profile profile,
    string configPath)
{
    public CliEnvironment Environment { get; } = environment;

    public Mg3Profile Profile { get; } = profile;

    public string ConfigPath { get; } = configPath;

    /// <summary>Periodo esaminato, come durata della DSL (es. 24h, 7d).</summary>
    public string Since { get; set; } = "24h";

    /// <summary>Key dei job mostrati dall'ultimo elenco dei job in errore, nell'ordine numerato.</summary>
    public IReadOnlyList<string> LastFaultedKeys { get; set; } = [];

    /// <summary>Per il menu le celle non vengono troncate: i messaggi d'errore vanno letti per intero.</summary>
    public OutputSettings Display => Profile.Output with { Format = OutputFormat.Text, MaxCellWidth = 10_000 };

    public TextWriter Out => Environment.Out;

    public string? Ask(string question)
    {
        Out.Write(question + ": ");
        return Environment.In.ReadLine()?.Trim();
    }

    /// <summary>Esegue un comando con query. In caso d'errore lo mostra e restituisce null.</summary>
    public async Task<CommandResult?> RunAsync(string command, string query, CancellationToken cancellationToken)
    {
        var request = new BatchRequest([new CommandRequest("menu", command, new Dictionary<string, string>(), query)], new BatchOptions(), IsBatch: false);
        var result = (await service.ExecuteAsync(request, cancellationToken).ConfigureAwait(false)).Results[0];
        if (Profile.Verbosity != LogVerbosity.Quiet)
        {
            foreach (var warning in result.Warnings)
            {
                Out.WriteLine($"Avviso: {warning}");
            }
        }

        if (result.Error is not null && result.Payload is null)
        {
            textFormatter.WriteError(Out, result.Error, Display);
            return null;
        }

        return result;
    }

    /// <summary>Mostra le righe con un titolo; se non ce ne sono, stampa <paramref name="empty"/>.</summary>
    public void Show(string title, IReadOnlyList<JsonNode?> rows, string empty, bool truncated = false)
    {
        Out.WriteLine();
        if (rows.Count == 0)
        {
            Out.WriteLine(empty);
            return;
        }

        var result = new CommandResult { Id = title, Command = title, Rows = rows, Payload = new JsonArray(), Truncated = truncated };
        textFormatter.Write(Out, new BatchResult([result], IsBatch: false, 0), Display);
    }
}
