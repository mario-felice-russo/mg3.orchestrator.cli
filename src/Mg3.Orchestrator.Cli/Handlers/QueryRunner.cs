using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Cli.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Mg3.Orchestrator.Cli.Handlers;

/// <summary>Esegue una richiesta (singola o batch) con la configurazione effettiva e scrive l'output.</summary>
public static class QueryRunner
{
    public static async Task<int> RunAsync(CliContext context, BatchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        var profile = context.LoadProfile(request.Config);
        await using var services = context.BuildServices(profile);
        var service = services.GetRequiredService<IOrchestratorQueryService>();
        try
        {
            var result = await service.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            return context.WriteResult(services, result, profile.Output);
        }
        catch (Mg3Exception ex)
        {
            return context.WriteError(ex, profile.Output);
        }
    }

    public static BatchRequest SingleCommand(CliContext context, string command, IReadOnlyDictionary<string, string> args, string? query)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new BatchRequest([new CommandRequest("cmd", command, args, query)], context.BatchOptions(), IsBatch: false);
    }

    public static Dictionary<string, string> Args(params (string Name, string? Value)[] values) =>
        values.Where(v => v.Value is not null).ToDictionary(v => v.Name, v => v.Value!, StringComparer.OrdinalIgnoreCase);

    public static JsonObject? EmptyToNull(JsonObject? value) => value is { Count: > 0 } ? value : null;
}
