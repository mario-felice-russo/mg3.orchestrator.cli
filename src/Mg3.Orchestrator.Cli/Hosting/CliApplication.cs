using System.CommandLine;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Cli.Handlers;

namespace Mg3.Orchestrator.Cli.Hosting;

/// <summary>Punto d'ingresso testabile: analizza gli argomenti, esegue e restituisce sempre un exit code documentato.</summary>
public static class CliApplication
{
    public static async Task<int> RunAsync(string[] args, CliEnvironment environment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);
        var builder = new CommandTreeBuilder(environment, ServiceCommands.CreateCatalog());
        var root = builder.Build();
        var parse = root.Parse(args);
        var context = new CliContext(environment, builder.Global, parse);
        try
        {
            if (parse.Errors.Count > 0)
            {
                return context.WriteError(new ConfigurationException(string.Join(" ", parse.Errors.Select(e => e.Message))));
            }

            var configuration = new InvocationConfiguration
            {
                Output = environment.Out,
                Error = environment.Error,
                EnableDefaultExceptionHandler = false,
                ProcessTerminationTimeout = null,
            };
            return await parse.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return context.WriteError(new Mg3Exception(ExitCodes.Cancelled, "cancelled", "Esecuzione annullata dall'utente."));
        }
        catch (Mg3Exception ex)
        {
            return context.WriteError(ex);
        }
#pragma warning disable CA1031 // Handler globale: nessuna eccezione deve raggiungere l'utente non gestita.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return context.WriteError(new Mg3Exception(ExitCodes.Generic, "unexpected", $"Errore inatteso: {ex.Message}", ex));
        }
    }
}
