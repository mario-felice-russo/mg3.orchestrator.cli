using System.Text.Json.Nodes;
using Mg3.Orchestrator.Query.Evaluation;

namespace Mg3.Orchestrator.Query.Execution;

/// <summary>Stato condiviso durante l'esecuzione della pipeline.</summary>
public sealed class StageExecutionContext(
    ExpressionEvaluator evaluator,
    IReadOnlyDictionary<string, IReadOnlyList<JsonNode?>> namedSources,
    string? sourceAlias,
    int maxJoinRows,
    DateTimeOffset now)
{
    public ExpressionEvaluator Evaluator { get; } = evaluator;

    public IReadOnlyDictionary<string, IReadOnlyList<JsonNode?>> NamedSources { get; } = namedSources;

    /// <summary>Alias della sorgente; diventa null dopo una proiezione.</summary>
    public string? SourceAlias { get; set; } = sourceAlias;

    public int MaxJoinRows { get; } = maxJoinRows;

    public DateTimeOffset Now { get; } = now;

    public FieldUsageTracker Tracker { get; } = new();

    public List<string> Warnings { get; } = [];

    public bool Truncated { get; set; }

    public EvaluationScope Scope(JsonNode? row) => new(row, SourceAlias, Now, Tracker);
}
