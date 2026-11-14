using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Core.Batch;
using Mg3.Orchestrator.Query.Parsing;

namespace Mg3.Orchestrator.Core.UnitTests;

public sealed class BatchTests
{
    private static readonly QueryParser Parser = new([new WhereStageParser(), new CountStageParser(), new JoinStageParser()]);

    private static PreparedCommand Prepared(string id, string? command, string? query = null) =>
        PreparedCommand.Prepare(CoreHost.Command(id, command, query), Parser);

    [Fact]
    public void OrdersByDependenciesKeepingRequestOrder()
    {
        var order = DependencyGraph.Order(
        [
            Prepared("c", null, "from $b | count"),
            Prepared("a", "jobs list"),
            Prepared("b", null, "from $a | join $a as x on Id == x.Id"),
            Prepared("d", "jobs list"),
        ]);
        order.Select(c => c.Id).ShouldBe(["a", "b", "c", "d"]);
    }

    [Fact]
    public void RejectsInvalidGraphs()
    {
        Should.Throw<ConfigurationException>(() => DependencyGraph.Order([])).Message.ShouldContain("non contiene");
        Should.Throw<ConfigurationException>(() => DependencyGraph.Order([Prepared("a", "jobs"), Prepared("a", "jobs")])).Message.ShouldContain("duplicato");
        Should.Throw<ConfigurationException>(() => DependencyGraph.Order([Prepared(" ", "jobs")])).Message.ShouldContain("'id'");
        Should.Throw<ConfigurationException>(() => DependencyGraph.Order([Prepared("a", null, "from $z")])).Message.ShouldContain("'$z'");
        Should.Throw<ConfigurationException>(() => DependencyGraph.Order([Prepared("a", null, "from $b"), Prepared("b", null, "from $a")])).Message.ShouldContain("cicliche");
    }

    [Fact]
    public void PreparationErrorsStayOnTheCommand()
    {
        Prepared("a", null).Error!.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
        Prepared("b", "jobs list", "where (").Error!.ExitCode.ShouldBe(ExitCodes.Query);
        Prepared("c", "jobs list", "from jobs").Error!.ExitCode.ShouldBe(ExitCodes.Query);
        Prepared("d", "jobs list").DisplayCommand.ShouldBe("jobs list");
        Prepared("e", null, "from $x").DisplayCommand.ShouldBe("from $x");
    }

    private static CommandResult Result(CommandStatus status, int exit = 0) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Command = "c",
        Status = status,
        Error = status == CommandStatus.Ok ? null : new ErrorInfo(exit, "x", "x", null),
    };

    [Fact]
    public void ResolvesExitCodes()
    {
        ExitCodeResolver.Resolve([Result(CommandStatus.Ok)], false, false).ShouldBe(0);
        ExitCodeResolver.Resolve([Result(CommandStatus.Error, 4)], false, false).ShouldBe(4);
        ExitCodeResolver.Resolve([Result(CommandStatus.Ok), Result(CommandStatus.Error, 4)], true, false).ShouldBe(7);
        ExitCodeResolver.Resolve([Result(CommandStatus.Ok), Result(CommandStatus.Error, 4)], true, true).ShouldBe(4);
        ExitCodeResolver.Resolve([Result(CommandStatus.Skipped, 1), Result(CommandStatus.Error, 3)], true, false).ShouldBe(3);
        ExitCodeResolver.Resolve([Result(CommandStatus.Ok), Result(CommandStatus.Timeout, 6)], true, false).ShouldBe(6);
    }

    [Fact]
    public void PayloadBuilderKeepsShape()
    {
        var rows = new JsonNode?[] { new JsonObject { ["a"] = 1 } };
        var original = JsonNode.Parse("{\"@odata.context\":\"ctx\",\"@odata.count\":99,\"value\":[]}");
        PayloadBuilder.Build(original, PayloadShape.ODataCollection, rows)!.ToJsonString().ShouldBe("{\"@odata.context\":\"ctx\",\"@odata.count\":1,\"value\":[{\"a\":1}]}");
        PayloadBuilder.Build(null, PayloadShape.ODataCollection, rows)!.ToJsonString().ShouldBe("{\"value\":[{\"a\":1}]}");
        PayloadBuilder.Build(null, PayloadShape.Array, rows)!.ToJsonString().ShouldBe("[{\"a\":1}]");
        PayloadBuilder.Build(null, PayloadShape.Entity, rows)!.ToJsonString().ShouldBe("{\"a\":1}");
        PayloadBuilder.Build(null, PayloadShape.Entity, [.. rows, .. rows])!.ToJsonString().ShouldBe("[{\"a\":1},{\"a\":1}]");
    }
}
