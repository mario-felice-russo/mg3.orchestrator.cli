using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Abstractions.Http;
using Mg3.Orchestrator.Abstractions.Query;
using Mg3.Orchestrator.Core.Commands;

namespace Mg3.Orchestrator.Core.Batch;

public interface ICommandExecutor
{
    Task<CommandResult> ExecuteAsync(PreparedCommand command, IReadOnlyDictionary<string, CommandResult> dependencies, ExecutionScope scope, CancellationToken cancellationToken);
}

/// <summary>Esegue un singolo comando: risoluzione, pianificazione, lettura, query in memoria e ricostruzione del payload.</summary>
public sealed class CommandExecutor(
    ICommandResolver resolver,
    IQueryPlanner planner,
    IQueryExecutor queryExecutor,
    IQueryPrinter printer,
    IOrchestratorReader reader,
    Mg3Profile profile,
    TimeProvider time) : ICommandExecutor
{
    private sealed record SourceData(JsonNode? Payload, PayloadShape Shape, IReadOnlyList<JsonNode?> Rows, int Pages, bool Truncated, bool Interrupted, long? TotalCount);

    public async Task<CommandResult> ExecuteAsync(PreparedCommand command, IReadOnlyDictionary<string, CommandResult> dependencies, ExecutionScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(scope);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (command.Error is not null)
            {
                throw command.Error;
            }

            return await ExecuteCoreAsync(command, dependencies, scope, stopwatch, cancellationToken).ConfigureAwait(false);
        }
        catch (Mg3Exception ex)
        {
            return CommandResult.Failed(command.Id, command.DisplayCommand, ex, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (scope.IsTimedOut)
        {
            return CommandResult.Failed(command.Id, command.DisplayCommand, Timeout(), stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (scope.IsStopped)
        {
            return Skipped(command, "Non completato: un altro comando è fallito (stopOnError).", stopwatch.ElapsedMilliseconds);
        }
    }

    public static CommandResult Skipped(PreparedCommand command, string reason, long durationMs)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new CommandResult
        {
            Id = command.Id,
            Command = command.DisplayCommand,
            Status = CommandStatus.Skipped,
            DurationMs = durationMs,
            Error = new ErrorInfo(ExitCodes.Generic, "skipped", reason, command.Id),
        };
    }

    public static GlobalTimeoutException Timeout() =>
        new("Timeout globale superato (limits.globalTimeoutSeconds): risultati parziali o assenti.");

    private async Task<CommandResult> ExecuteCoreAsync(PreparedCommand command, IReadOnlyDictionary<string, CommandResult> dependencies, ExecutionScope scope, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var ast = command.Ast;
        IReadOnlyList<StageNode> stages = ast?.Stages ?? [];
        var warnings = new List<string>();
        QueryPlan plan;
        ResolvedCommand? resolved = null;
        SourceData source;

        if (ast?.Source is { IsReference: true } reference)
        {
            plan = new QueryPlan(ODataQueryOptions.Empty, [], stages);
            if (scope.Options.ExplainOnly)
            {
                return Explain(command, null, plan, stopwatch);
            }

            var dependency = dependencies[reference.Name];
            source = new SourceData(dependency.Payload, dependency.Shape, dependency.Rows, 0, dependency.Truncated, false, dependency.TotalCount);
        }
        else
        {
            var text = command.Request.Command ?? ast!.Source!.Name + " list";
            resolved = resolver.Resolve(text, command.Request.Args, now);
            if (resolved.Kind == CommandKind.Count && stages.Count > 0)
            {
                resolved = resolved with
                {
                    Kind = CommandKind.List,
                    Options = resolved.Options with { Top = null, Count = true },
                    Paginate = resolved.Resource!.SupportsPaging,
                };
                stages = [.. stages, new CountStage(-1)];
            }

            plan = resolved.SupportsPushdown
                ? planner.Plan(stages, new PlanningContext(resolved.Resource, resolved.Options, scope.Options.PushdownEnabled, now))
                : new QueryPlan(resolved.Options, [], stages);
            if (scope.Options.ExplainOnly)
            {
                return Explain(command, resolved, plan, stopwatch);
            }

            if (resolved.Resource is { FolderScoped: true } && (resolved.Folder is null || resolved.Folder.IsEmpty))
            {
                warnings.Add($"Nessuna cartella configurata per '{resolved.Resource.Name}': Orchestrator potrebbe rifiutare la richiesta o restituire dati parziali.");
            }

            source = await ReadAsync(resolved, plan, cancellationToken).ConfigureAwait(false);
            source = Adapt(resolved, source, plan.ClientStages.Count > 0, warnings);
        }

        var namedSources = await LoadJoinSourcesAsync(plan.ClientStages, dependencies, resolved, cancellationToken).ConfigureAwait(false);
        var payload = source.Payload;
        var rows = source.Rows;
        var truncated = source.Truncated;
        var total = source.TotalCount;
        if (plan.ClientStages.Count > 0)
        {
            var execution = queryExecutor.Execute(plan.ClientStages, source.Rows, new QueryExecutionContext(namedSources, ast?.Source?.Alias, profile.Limits.MaxJoinRows, now));
            warnings.AddRange(execution.Warnings);
            rows = execution.Rows;
            payload = PayloadBuilder.Build(source.Payload, source.Shape, rows);
            truncated |= execution.Truncated;
            total = null;
        }

        var timedOut = source.Interrupted && scope.IsTimedOut;
        return new CommandResult
        {
            Id = command.Id,
            Command = command.DisplayCommand,
            Status = timedOut ? CommandStatus.Timeout : CommandStatus.Ok,
            Error = timedOut ? new ErrorInfo(ExitCodes.Timeout, "timeout", Timeout().Message, command.Id) : null,
            DurationMs = stopwatch.ElapsedMilliseconds,
            Pages = source.Pages,
            Truncated = truncated || source.Interrupted,
            Pushdown = Describe(plan.Server),
            Warnings = warnings,
            Payload = payload,
            Shape = source.Shape,
            Rows = rows,
            TotalCount = total,
        };
    }

    private async Task<SourceData> ReadAsync(ResolvedCommand resolved, QueryPlan plan, CancellationToken cancellationToken)
    {
        var result = await reader.ReadAsync(new ReadRequest(resolved.Path, plan.Server, resolved.Folder, resolved.Paginate), cancellationToken).ConfigureAwait(false);
        return new SourceData(result.Payload, result.Shape, result.Rows, result.Pages, result.Truncated, result.Interrupted, result.TotalCount);
    }

    private static SourceData Adapt(ResolvedCommand resolved, SourceData source, bool hasClientStages, List<string> warnings)
    {
        switch (resolved.Kind)
        {
            case CommandKind.GetByFilter:
                if (source.Rows.Count == 0)
                {
                    throw new AccessDeniedException("not_found", $"Nessun elemento di '{resolved.Resource!.Name}' corrisponde a {resolved.Options.Filter}.");
                }

                if (source.Rows.Count > 1)
                {
                    warnings.Add($"Più elementi corrispondono a {resolved.Options.Filter}: restituito il primo.");
                }

                var entity = source.Rows[0]?.DeepClone();
                return source with { Payload = entity, Shape = PayloadShape.Entity, Rows = [entity], TotalCount = null };
            case CommandKind.Count when !hasClientStages:
                var count = source.TotalCount ?? source.Rows.Count;
                return source with { Rows = [new JsonObject { ["count"] = count }] };
            default:
                return source;
        }
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<JsonNode?>>> LoadJoinSourcesAsync(
        IReadOnlyList<StageNode> stages,
        IReadOnlyDictionary<string, CommandResult> dependencies,
        ResolvedCommand? resolved,
        CancellationToken cancellationToken)
    {
        var sources = new Dictionary<string, IReadOnlyList<JsonNode?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var join in stages.OfType<JoinStage>())
        {
            var key = join.Right.SourceKey;
            if (sources.ContainsKey(key))
            {
                continue;
            }

            if (join.Right.IsReference)
            {
                sources[key] = dependencies[join.Right.Name].Rows;
                continue;
            }

            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (resolved?.Folder is { IsEmpty: false } folder)
            {
                args[CommandArguments.Folder] = folder.Id?.ToString(CultureInfo.InvariantCulture) ?? folder.Key ?? folder.Path!;
            }

            var target = resolver.Resolve(join.Right.Name + " list", args, time.GetUtcNow());
            var data = await ReadAsync(target, new QueryPlan(target.Options, [], []), cancellationToken).ConfigureAwait(false);
            sources[key] = data.Rows;
        }

        return sources;
    }

    private CommandResult Explain(PreparedCommand command, ResolvedCommand? resolved, QueryPlan plan, Stopwatch stopwatch)
    {
        var server = new JsonObject();
        foreach (var (name, value) in ServerParts(plan.Server))
        {
            server[name] = value;
        }

        var payload = new JsonObject
        {
            ["command"] = command.DisplayCommand,
            ["resource"] = resolved?.Resource?.Name,
            ["path"] = resolved?.Path,
            ["server"] = server,
            ["pushedStages"] = new JsonArray(plan.PushedStages.Select(s => (JsonNode?)JsonValue.Create(PrintStage(s))).ToArray()),
            ["clientStages"] = new JsonArray(plan.ClientStages.Select(s => (JsonNode?)JsonValue.Create(PrintStage(s))).ToArray()),
        };
        var rows = ServerParts(plan.Server)
            .Select(p => (JsonNode?)new JsonObject { ["phase"] = "server", ["step"] = $"{p.Name}={p.Value}" })
            .Concat(plan.ClientStages.Select(s => (JsonNode?)new JsonObject { ["phase"] = "client", ["step"] = PrintStage(s) }))
            .ToArray();
        return new CommandResult
        {
            Id = command.Id,
            Command = command.DisplayCommand,
            DurationMs = stopwatch.ElapsedMilliseconds,
            Pushdown = Describe(plan.Server),
            Payload = payload,
            Shape = PayloadShape.Entity,
            Rows = rows,
        };
    }

    private string PrintStage(StageNode stage) => printer.Print(new QueryAst(null, [stage]));

    private static List<(string Name, string Value)> ServerParts(ODataQueryOptions options)
    {
        var parts = new List<(string, string)>();
        void Add(string name, string? value)
        {
            if (value is not null)
            {
                parts.Add((name, value));
            }
        }

        Add("$filter", options.Filter);
        Add("$orderby", options.OrderBy);
        Add("$select", options.Select);
        Add("$expand", options.Expand);
        Add("$skip", options.Skip?.ToString(CultureInfo.InvariantCulture));
        Add("$top", options.Top?.ToString(CultureInfo.InvariantCulture));
        return parts;
    }

    private static string? Describe(ODataQueryOptions options)
    {
        var parts = ServerParts(options);
        return parts.Count == 0 ? null : string.Join("; ", parts.Select(p => $"{p.Name}={p.Value}"));
    }
}
