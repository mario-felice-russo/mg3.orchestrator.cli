using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Http;

namespace Mg3.Orchestrator.Abstractions.Query;

public interface IQueryParser
{
    QueryAst Parse(string text);
}

/// <summary>Piano di esecuzione: parte inviata al server (pushdown) e parte eseguita in memoria.</summary>
public sealed record QueryPlan(
    ODataQueryOptions Server,
    IReadOnlyList<StageNode> PushedStages,
    IReadOnlyList<StageNode> ClientStages);

/// <param name="Resource">Risorsa sorgente; null se la sorgente è il risultato di un altro comando.</param>
/// <param name="BaseOptions">Opzioni OData esplicite già fornite dall'utente (filter, top, ...).</param>
public sealed record PlanningContext(ResourceDescriptor? Resource, ODataQueryOptions BaseOptions, bool PushdownEnabled, DateTimeOffset Now);

public interface IQueryPlanner
{
    QueryPlan Plan(IReadOnlyList<StageNode> stages, PlanningContext context);
}

public sealed record QueryExecutionContext(
    IReadOnlyDictionary<string, IReadOnlyList<JsonNode?>> NamedSources,
    string? SourceAlias,
    int MaxJoinRows,
    DateTimeOffset Now);

public sealed record QueryExecutionResult(IReadOnlyList<JsonNode?> Rows, IReadOnlyList<string> Warnings, bool Truncated);

public interface IQueryExecutor
{
    QueryExecutionResult Execute(IReadOnlyList<StageNode> stages, IReadOnlyList<JsonNode?> rows, QueryExecutionContext context);
}

/// <summary>Converte un AST in testo DSL canonico.</summary>
public interface IQueryPrinter
{
    string Print(QueryAst ast);
}
