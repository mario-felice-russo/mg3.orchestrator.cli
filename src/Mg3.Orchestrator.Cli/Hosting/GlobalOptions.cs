using System.CommandLine;

namespace Mg3.Orchestrator.Cli.Hosting;

/// <summary>Opzioni comuni. Tutta la configurazione sta nel file indicato da --config.</summary>
public sealed class GlobalOptions
{
    public Option<string?> Config { get; } = new("--config", "-c") { Description = "File di configurazione JSON (crealo con: mg3orch init --config <percorso>).", Recursive = true };

    public Option<bool> Explain { get; } = new("--explain") { Description = "Mostra il piano di esecuzione della query senza contattare Orchestrator.", Recursive = true };

    public IEnumerable<Option> All => [Config, Explain];
}
