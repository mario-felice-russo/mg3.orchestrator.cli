using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Abstractions.Query;

namespace Mg3.Orchestrator.Core.Batch;

/// <summary>
/// Esegue il batch: prepara i comandi, ordina il grafo delle dipendenze, esegue in parallelo i comandi indipendenti
/// entro il timeout globale e restituisce i risultati nell'ordine della richiesta.
/// </summary>
public sealed class OrchestratorQueryService(IQueryParser parser, ICommandExecutor executor, Mg3Profile profile, TimeProvider time) : IOrchestratorQueryService
{
    public async Task<BatchResult> ExecuteAsync(BatchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Options.Parallelism < 1 || request.Options.Parallelism > 64)
        {
            throw new ConfigurationException($"options.parallelism deve essere compreso tra 1 e 64, trovato {request.Options.Parallelism}.");
        }

        var prepared = request.Commands.Select(c => PreparedCommand.Prepare(c, parser)).ToList();
        var ordered = DependencyGraph.Order(prepared);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(profile.Limits.GlobalTimeoutSeconds), time);
        using var stopCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token, stopCts.Token);
        using var gate = new SemaphoreSlim(request.Options.Parallelism);
        var scope = new ExecutionScope(request.Options, cancellationToken, timeoutCts.Token, stopCts.Token);
        var tasks = new Dictionary<string, Task<CommandResult>>(StringComparer.Ordinal);
        foreach (var command in ordered)
        {
            tasks[command.Id] = RunAsync(command, tasks, scope, gate, stopCts, linked.Token);
        }

        var results = await Task.WhenAll(prepared.Select(p => tasks[p.Id])).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var exitCode = ExitCodeResolver.Resolve(results, request.IsBatch, request.Options.StopOnError);
        return new BatchResult(results, request.IsBatch, exitCode);
    }

    private async Task<CommandResult> RunAsync(
        PreparedCommand command,
        Dictionary<string, Task<CommandResult>> tasks,
        ExecutionScope scope,
        SemaphoreSlim gate,
        CancellationTokenSource stopCts,
        CancellationToken token)
    {
        var dependencyTasks = command.Dependencies.ToDictionary(d => d, d => tasks[d], StringComparer.Ordinal);
        await Task.WhenAll(dependencyTasks.Values).ConfigureAwait(false);
        var dependencies = dependencyTasks.ToDictionary(kv => kv.Key, kv => kv.Value.Result, StringComparer.Ordinal);
        var failed = dependencies.Values.FirstOrDefault(d => !d.IsSuccess);
        var result = failed is not null
            ? CommandExecutor.Skipped(command, $"Non eseguito: la dipendenza '${failed.Id}' non è riuscita.", 0)
            : await ExecuteGatedAsync(command, dependencies, scope, gate, token).ConfigureAwait(false);

        if (!result.IsSuccess && scope.Options.StopOnError)
        {
            await stopCts.CancelAsync().ConfigureAwait(false);
        }

        return result;
    }

    private async Task<CommandResult> ExecuteGatedAsync(
        PreparedCommand command,
        IReadOnlyDictionary<string, CommandResult> dependencies,
        ExecutionScope scope,
        SemaphoreSlim gate,
        CancellationToken token)
    {
        try
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (scope.IsTimedOut)
        {
            return CommandResult.Failed(command.Id, command.DisplayCommand, CommandExecutor.Timeout(), 0);
        }
        catch (OperationCanceledException) when (scope.IsStopped)
        {
            return CommandExecutor.Skipped(command, "Non eseguito: un altro comando è fallito (stopOnError).", 0);
        }

        try
        {
            return await executor.ExecuteAsync(command, dependencies, scope, token).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
