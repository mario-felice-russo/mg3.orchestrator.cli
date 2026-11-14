using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Query;

namespace Mg3.Orchestrator.Query.Execution;

/// <summary>Esecutore in memoria di un tipo di stadio.</summary>
public interface IStageExecutor
{
    Type StageType { get; }

    IReadOnlyList<JsonNode?> Execute(StageNode stage, IReadOnlyList<JsonNode?> rows, StageExecutionContext context);
}

/// <summary>Base tipizzata per gli esecutori.</summary>
public abstract class StageExecutor<TStage> : IStageExecutor
    where TStage : StageNode
{
    public Type StageType => typeof(TStage);

    public IReadOnlyList<JsonNode?> Execute(StageNode stage, IReadOnlyList<JsonNode?> rows, StageExecutionContext context) =>
        Execute((TStage)stage, rows, context);

    protected abstract IReadOnlyList<JsonNode?> Execute(TStage stage, IReadOnlyList<JsonNode?> rows, StageExecutionContext context);
}

/// <summary>Stadio interno: raggruppamento con aggregati (groupby + aggregate fusi).</summary>
public sealed record GroupAggregateStage(IReadOnlyList<FieldExpr> Keys, IReadOnlyList<AggregateDefinition> Aggregates, int Position)
    : StageNode(Position);
