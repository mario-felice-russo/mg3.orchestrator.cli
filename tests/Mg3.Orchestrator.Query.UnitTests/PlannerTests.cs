using Mg3.Orchestrator.Abstractions.Http;
using Mg3.Orchestrator.Abstractions.Query;
using Mg3.Orchestrator.Catalog.Resources;
using Mg3.Orchestrator.Tests.Shared;

namespace Mg3.Orchestrator.Query.UnitTests;

public sealed class PlannerTests
{
    private static readonly Abstractions.Catalog.ResourceDescriptor Jobs = new JobsResource().Descriptor;

    private static QueryPlan Plan(string query, ODataQueryOptions? baseOptions = null, bool pushdown = true, Abstractions.Catalog.ResourceDescriptor? resource = null) =>
        QueryHost.Planner.Plan(QueryHost.Parser.Parse(query).Stages, new PlanningContext(resource ?? Jobs, baseOptions ?? ODataQueryOptions.Empty, pushdown, Fixtures.Now));

    [Theory]
    [InlineData("where State == 'Faulted'", "State eq 'Faulted'")]
    [InlineData("where Id > 5 and State != 'Running'", "(Id gt 5) and (State ne 'Running')")]
    [InlineData("where Id >= 5 or Id <= 2", "(Id ge 5) or (Id le 2)")]
    [InlineData("where 5 < Id", "Id gt 5")]
    [InlineData("where 5 >= Id", "Id le 5")]
    [InlineData("where CreationTime >= now() - 1d", "CreationTime ge 2026-11-01T12:00:00Z")]
    [InlineData("where StartTime < '2026-11-01T10:00:00Z'", "StartTime lt 2026-11-01T10:00:00Z")]
    [InlineData("where HostMachineName in ('VM01', 'VM02')", "(HostMachineName eq 'VM01' or HostMachineName eq 'VM02')")]
    [InlineData("where ReleaseName contains 'Fat'", "contains(ReleaseName,'Fat')")]
    [InlineData("where ReleaseName startswith 'Fa' and ReleaseName endswith 're'", "(startswith(ReleaseName,'Fa')) and (endswith(ReleaseName,'re'))")]
    [InlineData("where Key == '00000000-0000-0000-0000-000000000003'", "Key eq 00000000-0000-0000-0000-000000000003")]
    [InlineData("where EndTime is null", "EndTime eq null")]
    [InlineData("where EndTime is not null", "EndTime ne null")]
    [InlineData("where not (State == 'Faulted')", "not (State eq 'Faulted')")]
    [InlineData("where ReleaseName == 'O''Brien'", "ReleaseName eq 'O''Brien'")]
    [InlineData("where State == null", "State eq null")]
    [InlineData("where Id == 1 | where State == 'Faulted'", "(Id eq 1) and (State eq 'Faulted')")]
    public void TranslatesFiltersToOData(string query, string expected)
    {
        var plan = Plan(query);
        plan.Server.Filter.ShouldBe(expected);
        plan.ClientStages.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("where ReleaseName > 'A'")]
    [InlineData("where Info contains 'x'")]
    [InlineData("where lower(ReleaseName) == 'x'")]
    [InlineData("where Robot.Name == 'x'")]
    [InlineData("where Info matches 'x'")]
    [InlineData("where Id == 'abc'")]
    [InlineData("where Key == 'non-guid'")]
    [InlineData("where State in ()")]
    [InlineData("where Id == Id")]
    [InlineData("where State == 'x' or Info == 'y'")]
    [InlineData("where date('non è una data') < CreationTime")]
    public void KeepsUntranslatableFiltersInMemory(string query)
    {
        var plan = Plan(query);
        plan.Server.Filter.ShouldBeNull();
        plan.PushedStages.ShouldBeEmpty();
        plan.ClientStages.Count.ShouldBe(1);
    }

    [Fact]
    public void PushesOrderSkipTakeAndSelect()
    {
        var plan = Plan("where State == 'Faulted' | orderby CreationTime desc, Id | skip 5 | take 10 | select Id, State");
        plan.Server.ShouldBe(new ODataQueryOptions { Filter = "State eq 'Faulted'", OrderBy = "CreationTime desc,Id asc", Skip = 5, Top = 10, Select = "Id,State" });
        plan.ClientStages.ShouldBeEmpty();
    }

    [Fact]
    public void StopsAtFirstStageThatChangesSemantics()
    {
        var plan = Plan("take 5 | where State == 'Faulted'");
        plan.Server.Top.ShouldBe(5);
        plan.Server.Filter.ShouldBeNull();
        plan.ClientStages.Single().ShouldBeOfType<WhereStage>();
    }

    [Fact]
    public void DoesNotPushSkipAfterTake()
    {
        var plan = Plan("take 10 | skip 3");
        plan.Server.Top.ShouldBe(10);
        plan.Server.Skip.ShouldBeNull();
        plan.ClientStages.Single().ShouldBeOfType<SkipStage>();
    }

    [Fact]
    public void CombinesConsecutiveSkipsAndTakes()
    {
        var plan = Plan("skip 2 | skip 3 | take 10 | take 4");
        plan.Server.Skip.ShouldBe(5);
        plan.Server.Top.ShouldBe(4);
    }

    [Fact]
    public void DoesNotPushSelectWithAliasOrNestedField()
    {
        Plan("select Id as X").Server.Select.ShouldBeNull();
        Plan("select Robot.Name").Server.Select.ShouldBeNull();
        Plan("select Sconosciuto").Server.Select.ShouldBeNull();
    }

    [Fact]
    public void DoesNotPushWhereAfterSelect()
    {
        var plan = Plan("select Id, State | where State == 'Faulted'");
        plan.Server.Select.ShouldBe("Id,State");
        plan.ClientStages.Single().ShouldBeOfType<WhereStage>();
    }

    [Fact]
    public void DoesNotPushSecondOrderBy()
    {
        var plan = Plan("orderby Id | orderby State");
        plan.Server.OrderBy.ShouldBe("Id asc");
        plan.ClientStages.Single().ShouldBeOfType<OrderByStage>();
    }

    [Fact]
    public void DoesNotPushOrderByOnUnknownField()
    {
        Plan("orderby Robot.Name").Server.OrderBy.ShouldBeNull();
    }

    [Fact]
    public void CombinesWithExplicitFilter()
    {
        var plan = Plan("where State == 'Faulted'", new ODataQueryOptions { Filter = "Source eq 'Manual'" });
        plan.Server.Filter.ShouldBe("(Source eq 'Manual') and (State eq 'Faulted')");
    }

    [Fact]
    public void ExplicitTopPreventsFilterPushdown()
    {
        var plan = Plan("where State == 'Faulted'", new ODataQueryOptions { Top = 10 });
        plan.Server.Filter.ShouldBeNull();
        plan.Server.Top.ShouldBe(10);
        plan.ClientStages.Count.ShouldBe(1);
    }

    [Fact]
    public void ExplicitOrderByPreventsOrderByPushdown()
    {
        var plan = Plan("orderby Id", new ODataQueryOptions { OrderBy = "State" });
        plan.Server.OrderBy.ShouldBe("State");
        plan.ClientStages.Count.ShouldBe(1);
    }

    [Fact]
    public void ExplicitSelectPreventsFilterPushdown()
    {
        var plan = Plan("where State == 'x' | take 3", new ODataQueryOptions { Select = "Id" });
        plan.Server.Filter.ShouldBeNull();
        plan.ClientStages.Count.ShouldBe(2);
    }

    [Fact]
    public void DisabledPushdownKeepsEverythingInMemory()
    {
        var plan = Plan("where State == 'Faulted' | take 3", pushdown: false);
        plan.Server.ShouldBe(ODataQueryOptions.Empty);
        plan.ClientStages.Count.ShouldBe(2);
    }

    [Fact]
    public void ResourcesWithoutODataAreNotPushed()
    {
        var plan = Plan("where title == 'x'", resource: new JobStatsResource().Descriptor);
        plan.PushedStages.ShouldBeEmpty();
    }

    [Fact]
    public void NonPushableStagesStayInMemory()
    {
        var plan = Plan("where State == 'Faulted' | groupby ReleaseName | count");
        plan.Server.Filter.ShouldBe("State eq 'Faulted'");
        plan.ClientStages.Count.ShouldBe(2);
    }
}
