using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Query;
using Mg3.Orchestrator.Query;
using Mg3.Orchestrator.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Mg3.Orchestrator.Query.UnitTests;

/// <summary>Servizi della DSL costruiti come in produzione.</summary>
internal static class QueryHost
{
    private static readonly ServiceProvider Provider = new ServiceCollection().AddMg3Query().BuildServiceProvider();

    public static IQueryParser Parser => Provider.GetRequiredService<IQueryParser>();

    public static IQueryPrinter Printer => Provider.GetRequiredService<IQueryPrinter>();

    public static IQueryPlanner Planner => Provider.GetRequiredService<IQueryPlanner>();

    public static IQueryExecutor Executor => Provider.GetRequiredService<IQueryExecutor>();

    public static QueryExecutionResult Run(string query, IReadOnlyList<JsonNode?> rows, IReadOnlyDictionary<string, IReadOnlyList<JsonNode?>>? sources = null, int maxJoinRows = 100_000)
    {
        var ast = Parser.Parse(query);
        return Executor.Execute(ast.Stages, rows, new QueryExecutionContext(
            sources ?? new Dictionary<string, IReadOnlyList<JsonNode?>>(),
            ast.Source?.Alias,
            maxJoinRows,
            Fixtures.Now));
    }

    public static string Json(IEnumerable<JsonNode?> rows) => new JsonArray(rows.Select(r => r?.DeepClone()).ToArray()).ToJsonString();
}
