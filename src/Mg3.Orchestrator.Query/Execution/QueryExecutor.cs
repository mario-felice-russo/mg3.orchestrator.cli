using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Query;
using Mg3.Orchestrator.Query.Evaluation;

namespace Mg3.Orchestrator.Query.Execution;

/// <summary>Esegue in memoria la pipeline, delegando ogni stadio al proprio <see cref="IStageExecutor"/>.</summary>
public sealed class QueryExecutor : IQueryExecutor
{
    private readonly ExpressionEvaluator _evaluator;
    private readonly Dictionary<Type, IStageExecutor> _executors;

    public QueryExecutor(ExpressionEvaluator evaluator, IEnumerable<IStageExecutor> executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        _evaluator = evaluator;
        _executors = executors.ToDictionary(e => e.StageType);
    }

    public QueryExecutionResult Execute(IReadOnlyList<StageNode> stages, IReadOnlyList<JsonNode?> rows, QueryExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(context);
        var state = new StageExecutionContext(_evaluator, context.NamedSources, context.SourceAlias, context.MaxJoinRows, context.Now);
        var current = rows;
        var inputCount = rows.Count;
        foreach (var stage in Normalize(stages))
        {
            if (!_executors.TryGetValue(stage.GetType(), out var executor))
            {
                throw new QueryException($"Stadio non supportato: {stage.GetType().Name}", stage.Position);
            }

            current = executor.Execute(stage, current, state);
        }

        var warnings = new List<string>(state.Warnings);
        if (inputCount > 0)
        {
            warnings.AddRange(state.Tracker.MissingFields.Select(f => $"Il campo '{f}' è assente da tutte le righe."));
        }

        return new QueryExecutionResult(current, warnings.Distinct(StringComparer.Ordinal).ToArray(), state.Truncated);
    }

    /// <summary>Fonde groupby + aggregate in un unico stadio; un groupby isolato produce il conteggio.</summary>
    internal static IEnumerable<StageNode> Normalize(IReadOnlyList<StageNode> stages)
    {
        for (var i = 0; i < stages.Count; i++)
        {
            switch (stages[i])
            {
                case GroupByStage group when i + 1 < stages.Count && stages[i + 1] is AggregateStage aggregate:
                    yield return new GroupAggregateStage(group.Fields, aggregate.Aggregates, group.Position);
                    i++;
                    break;
                case GroupByStage group:
                    yield return new GroupAggregateStage(group.Fields, [new AggregateDefinition("count", "count", null)], group.Position);
                    break;
                case AggregateStage aggregate:
                    yield return new GroupAggregateStage([], aggregate.Aggregates, aggregate.Position);
                    break;
                default:
                    yield return stages[i];
                    break;
            }
        }
    }
}
