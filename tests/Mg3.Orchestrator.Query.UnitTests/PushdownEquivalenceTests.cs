using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Http;
using Mg3.Orchestrator.Abstractions.Query;
using Mg3.Orchestrator.Catalog.Resources;
using Mg3.Orchestrator.Tests.Shared;

namespace Mg3.Orchestrator.Query.UnitTests;

/// <summary>
/// Il risultato con pushdown deve coincidere con quello senza pushdown. La parte spinta sul server è simulata
/// applicandola nell'ordine OData ($filter → $orderby → $skip → $top → $select), non nell'ordine della pipeline.
/// </summary>
public sealed class PushdownEquivalenceTests
{
    public static TheoryData<string, bool> Queries => new()
    {
        { "where State == 'Faulted'", true },
        { "where State == 'Faulted' | take 3", true },
        { "take 5 | where State == 'Faulted'", true },
        { "orderby CreationTime desc | take 4", true },
        { "take 10 | orderby Id desc", true },
        { "where Id > 1010 and Id <= 1030 | orderby Id desc | skip 2 | take 5", true },
        { "skip 5 | take 5 | skip 2", true },
        { "take 10 | skip 3", true },
        { "where CreationTime >= now() - 12h", true },
        { "where HostMachineName in ('VM01', 'VM03') | select Id, HostMachineName", true },
        { "select Id, State | where State == 'Running'", true },
        { "where ReleaseName startswith 'Fa' or ReleaseName endswith 'ni'", true },
        { "where Info contains 'importo'", false },
        { "where lower(ReleaseName) == 'ordini'", false },
        { "where not (State == 'Successful') | orderby Id asc | take 7", true },
        { "where EndTime is null", true },
        { "where EndTime is not null and State != 'Faulted'", true },
        { "orderby ReleaseName asc, Id desc | take 6", true },
        { "where Robot.Name == 'Robot-VM01'", false },
        { "groupby ReleaseName | aggregate n = count() | orderby n desc", false },
        { "where State == 'Faulted' | groupby HostMachineName", true },
        { "where Id >= 1035 | count", true },
        { "orderby StartTime desc | take 3 | select Id, StartTime", true },
        { "where JobPriority == 'High' | distinct ReleaseName", true },
        { "where 1020 < Id | take 2", true },
        { "where CreationTime > date('2026-11-01T20:00:00Z') and CreationTime < date('2026-11-02T00:00:00Z')", true },
        { "skip 38 | take 10", true },
        { "where Key == '00000000-0000-0000-0000-000000000003'", true },
        { "where state == 'faulted' | orderby releasename | take 4", true },
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public void PushdownDoesNotChangeTheResult(string query, bool expectsPushdown)
    {
        var rows = Fixtures.Rows("jobs.json");
        var stages = QueryHost.Parser.Parse(query).Stages;

        var expected = QueryHost.Run(query, rows).Rows;
        var plan = QueryHost.Planner.Plan(stages, new PlanningContext(new JobsResource().Descriptor, ODataQueryOptions.Empty, true, Fixtures.Now));
        var serverRows = SimulateServer(plan.PushedStages, rows);
        var actual = QueryHost.Executor.Execute(plan.ClientStages, serverRows, Context()).Rows;

        (plan.PushedStages.Count > 0).ShouldBe(expectsPushdown);
        QueryHost.Json(actual).ShouldBe(QueryHost.Json(expected));
    }

    private static List<JsonNode?> SimulateServer(IReadOnlyList<StageNode> pushed, IReadOnlyList<JsonNode?> rows)
    {
        var serverOrder = new List<StageNode>();
        serverOrder.AddRange(pushed.OfType<WhereStage>());
        serverOrder.AddRange(pushed.OfType<OrderByStage>());
        var skip = pushed.OfType<SkipStage>().Sum(s => s.Count);
        if (skip > 0)
        {
            serverOrder.Add(new SkipStage(skip, 0));
        }

        if (pushed.OfType<TakeStage>().Any())
        {
            serverOrder.Add(new TakeStage(pushed.OfType<TakeStage>().Min(t => t.Count), 0));
        }

        serverOrder.AddRange(pushed.OfType<SelectStage>());
        return QueryHost.Executor.Execute(serverOrder, rows, Context()).Rows.ToList();
    }

    private static QueryExecutionContext Context() => new(new Dictionary<string, IReadOnlyList<JsonNode?>>(), null, 100_000, Fixtures.Now);
}
