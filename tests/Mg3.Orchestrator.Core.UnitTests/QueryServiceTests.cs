using Mg3.Orchestrator.Core.Batch;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Execution;
using static Mg3.Orchestrator.Core.UnitTests.CoreHost;

namespace Mg3.Orchestrator.Core.UnitTests;

public sealed class QueryServiceTests
{
    [Fact]
    public async Task ListWithoutQueryReturnsOriginalPayload()
    {
        var (service, reader, _) = Create();
        var result = await service.ExecuteAsync(Single(Command("cmd", "jobs list")), CancellationToken.None);
        result.ExitCode.ShouldBe(0);
        var command = result.Results.Single();
        command.Rows.Count.ShouldBe(40);
        command.TotalCount.ShouldBe(40);
        command.Payload!["@odata.count"]!.GetValue<int>().ShouldBe(40);
        reader.Requests.Single().Folder!.Path.ShouldBe("Shared");
    }

    [Fact]
    public async Task PushesDownAndRunsTheRestInMemory()
    {
        var (service, reader, _) = Create();
        var result = await service.ExecuteAsync(Single(Command("cmd", "jobs list", "where State == 'Faulted' | groupby ReleaseName")), CancellationToken.None);
        var request = reader.Requests.Single();
        request.Options.Filter.ShouldBe("State eq 'Faulted'");
        var command = result.Results.Single();
        command.Pushdown.ShouldBe("$filter=State eq 'Faulted'");
        command.Rows.Count.ShouldBe(4);
        command.Payload!["@odata.count"]!.GetValue<int>().ShouldBe(4);
        command.TotalCount.ShouldBeNull();
    }

    [Fact]
    public async Task NoPushdownSendsNothing()
    {
        var (service, reader, _) = Create();
        var result = await service.ExecuteAsync(Single(Command("cmd", "jobs list", "where State == 'Faulted'"), new BatchOptions { PushdownEnabled = false }), CancellationToken.None);
        reader.Requests.Single().Options.Filter.ShouldBeNull();
        result.Results.Single().Rows.Count.ShouldBe(8);
    }

    [Fact]
    public async Task ReferencesAndJoinsUseOtherResults()
    {
        var (service, reader, _) = Create();
        var batch = BatchOf(
            null,
            Command("rel", "releases list"),
            Command("faults", "jobs list", "where Info contains 'importo' | select Id, ReleaseName"),
            Command("joined", null, "from $faults as f | join $rel as r on f.ReleaseName == r.Name | select f.Id, r.ProcessKey | take 2"),
            Command("withResource", null, "from $faults | join releases as r on ReleaseName == r.Name | count"));
        var result = await service.ExecuteAsync(batch, CancellationToken.None);
        result.ExitCode.ShouldBe(0);
        result.Results.Select(r => r.Id).ShouldBe(["rel", "faults", "joined", "withResource"]);
        result.Results[2].Rows.Select(r => r!.ToJsonString()).ShouldBe(["{\"Id\":1001,\"r.ProcessKey\":\"OrdiniProcess\"}", "{\"Id\":1006,\"r.ProcessKey\":\"PagamentiProcess\"}"]);
        result.Results[3].Rows.Single()!["count"]!.GetValue<int>().ShouldBe(8);
        reader.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ReferenceWithoutStagesReturnsDependencyPayload()
    {
        var (service, _, _) = Create();
        var result = await service.ExecuteAsync(BatchOf(null, Command("a", "stats jobs"), Command("b", null, "from $a")), CancellationToken.None);
        result.Results[1].Payload!.ToJsonString().ShouldBe(result.Results[0].Payload!.ToJsonString());
        result.Results[1].Shape.ShouldBe(PayloadShape.Array);
    }

    [Fact]
    public async Task GetByFilterReturnsEntityOrNotFound()
    {
        var (service, _, _) = Create();
        var found = await service.ExecuteAsync(Single(Command("cmd", "jobs get 00000000-0000-0000-0000-000000000003")), CancellationToken.None);
        found.Results.Single().Shape.ShouldBe(PayloadShape.Entity);
        found.Results.Single().Payload!["Id"]!.GetValue<int>().ShouldBe(1003);

        var many = await service.ExecuteAsync(Single(Command("cmd", "jobs get Ordini")), CancellationToken.None);
        many.Results.Single().Warnings.ShouldContain(w => w.Contains("Più elementi", StringComparison.Ordinal));

        var missing = await service.ExecuteAsync(Single(Command("cmd", "jobs get 00000000-0000-0000-0000-0000000000ff")), CancellationToken.None);
        missing.ExitCode.ShouldBe(ExitCodes.AccessDenied);
    }

    [Fact]
    public async Task GetByPathAndQueryOnEntity()
    {
        var (service, _, _) = Create();
        var result = await service.ExecuteAsync(Single(Command("cmd", "jobs get 1001", "select Id, State")), CancellationToken.None);
        result.Results.Single().Payload!.ToJsonString().ShouldBe("{\"Id\":1001,\"State\":\"Faulted\"}");
    }

    [Fact]
    public async Task CountWithAndWithoutQuery()
    {
        var (service, reader, _) = Create();
        var plain = await service.ExecuteAsync(Single(Command("cmd", "jobs count")), CancellationToken.None);
        plain.Results.Single().Rows.Single()!["count"]!.GetValue<long>().ShouldBe(40);
        reader.Requests.Single().Options.Top.ShouldBe(0);

        var filtered = await service.ExecuteAsync(Single(Command("cmd", "jobs count", "where Info contains 'importo'")), CancellationToken.None);
        filtered.Results.Single().Rows.Single()!["count"]!.GetValue<int>().ShouldBe(8);
    }

    [Fact]
    public async Task ExplainDoesNotRead()
    {
        var (service, reader, _) = Create();
        var result = await service.ExecuteAsync(
            BatchOf(new BatchOptions { ExplainOnly = true }, Command("a", "jobs list", "where State == 'Faulted' | take 3 | groupby ReleaseName"), Command("b", null, "from $a | count")),
            CancellationToken.None);
        reader.Requests.ShouldBeEmpty();
        var plan = result.Results[0].Payload!;
        plan["server"]!["$filter"]!.GetValue<string>().ShouldBe("State eq 'Faulted'");
        plan["server"]!["$top"]!.GetValue<string>().ShouldBe("3");
        plan["clientStages"]!.AsArray().Single()!.GetValue<string>().ShouldBe("groupby ReleaseName");
        result.Results[0].Rows.Count.ShouldBe(3);
        result.Results[1].Payload!["clientStages"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task FailuresAreIsolatedUnlessStopOnError()
    {
        var (service, _, _) = Create();
        var partial = await service.ExecuteAsync(BatchOf(null, Command("ok", "jobs list"), Command("bad", "jobs list", "where ("), Command("dep", null, "from $bad | count")), CancellationToken.None);
        partial.ExitCode.ShouldBe(ExitCodes.PartialSuccess);
        partial.Results.Select(r => r.Status).ShouldBe([CommandStatus.Ok, CommandStatus.Error, CommandStatus.Skipped]);
        partial.Results[2].Error!.Message.ShouldContain("$bad");

        var stopped = await service.ExecuteAsync(BatchOf(new BatchOptions { StopOnError = true, Parallelism = 1 }, Command("bad", "pippo list"), Command("next", "jobs list")), CancellationToken.None);
        stopped.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
        stopped.Results[1].Status.ShouldBe(CommandStatus.Skipped);
    }

    [Fact]
    public async Task StopOnErrorCancelsRunningCommands()
    {
        var (service, reader, _) = Create();
        reader.BeforeRead = async (request, ct) =>
        {
            if (request.Path == "odata/Releases")
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
        };
        var result = await service.ExecuteAsync(BatchOf(new BatchOptions { StopOnError = true }, Command("slow", "releases list"), Command("bad", "jobs get 00000000-0000-0000-0000-0000000000ff")), CancellationToken.None);
        result.Results[0].Status.ShouldBe(CommandStatus.Skipped);
        result.ExitCode.ShouldBe(ExitCodes.AccessDenied);
    }

    [Fact]
    public async Task GlobalTimeoutProducesExitCode6()
    {
        var (service, reader, _) = Create(Profile(timeoutSeconds: 1));
        reader.BeforeRead = (_, ct) => Task.Delay(Timeout.Infinite, ct);
        var result = await service.ExecuteAsync(BatchOf(new BatchOptions { Parallelism = 1 }, Command("a", "jobs list"), Command("b", "jobs list")), CancellationToken.None);
        result.ExitCode.ShouldBe(ExitCodes.Timeout);
        result.Results.ShouldAllBe(r => r.Status == CommandStatus.Timeout);
        result.Results[0].Error!.ExitCode.ShouldBe(ExitCodes.Timeout);
    }

    [Fact]
    public async Task UserCancellationPropagates()
    {
        var (service, reader, _) = Create();
        using var cts = new CancellationTokenSource();
        reader.BeforeRead = async (_, ct) =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
        };
        await Should.ThrowAsync<OperationCanceledException>(() => service.ExecuteAsync(Single(Command("a", "jobs list")), cts.Token));
    }

    [Fact]
    public async Task ReaderErrorsBecomeResults()
    {
        var (service, reader, _) = Create();
        reader.Failure = new OrchestratorUnavailableException("giù");
        var result = await service.ExecuteAsync(Single(Command("a", "jobs list")), CancellationToken.None);
        result.ExitCode.ShouldBe(ExitCodes.Unavailable);
        result.Results.Single().Error!.Message.ShouldBe("giù");
    }

    [Fact]
    public async Task WarnsWhenFolderIsMissing()
    {
        var (service, _, _) = Create(Profile() with { Orchestrator = new Abstractions.Configuration.OrchestratorSettings { BaseUrl = "https://x.test/" } });
        var result = await service.ExecuteAsync(Single(Command("a", "jobs list")), CancellationToken.None);
        result.Results.Single().Warnings.ShouldContain(w => w.Contains("Nessuna cartella", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidParallelismIsRejected()
    {
        var (service, _, _) = Create();
        await Should.ThrowAsync<ConfigurationException>(() => service.ExecuteAsync(BatchOf(new BatchOptions { Parallelism = 0 }, Command("a", "jobs list")), CancellationToken.None));
    }

    [Fact]
    public async Task JoinOnResourceUsesCommandFolder()
    {
        var (service, reader, _) = Create();
        await service.ExecuteAsync(Single(Command("a", "jobs list", "join releases as r on ReleaseName == r.Name | count", new() { ["folder"] = "99" })), CancellationToken.None);
        reader.Requests.Select(r => r.Folder!.Id).ShouldBe([99L, 99L]);
    }

    [Fact]
    public void SkippedAndTimeoutFactories()
    {
        var command = PreparedCommand.Prepare(Command("x", "jobs list"), new Query.Parsing.QueryParser([]));
        var skipped = CommandExecutor.Skipped(command, "motivo", 3);
        skipped.Status.ShouldBe(CommandStatus.Skipped);
        skipped.Error!.Code.ShouldBe("skipped");
        CommandExecutor.Timeout().ExitCode.ShouldBe(ExitCodes.Timeout);
    }
}
