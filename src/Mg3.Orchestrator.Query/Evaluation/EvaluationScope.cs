using System.Text.Json.Nodes;

namespace Mg3.Orchestrator.Query.Evaluation;

/// <param name="SourceAlias">Alias della sorgente: <c>j.Campo</c> equivale a <c>Campo</c>.</param>
public sealed record EvaluationScope(JsonNode? Row, string? SourceAlias, DateTimeOffset Now, FieldUsageTracker? Tracker = null);
