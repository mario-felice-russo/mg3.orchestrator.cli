using Mg3.Orchestrator.Abstractions.Errors;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Mg3.Orchestrator.IntegrationTests;

/// <summary>Ogni exit code documentato è prodotto da almeno un test end-to-end.</summary>
public sealed class ExitCodeTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task Exit0Success()
    {
        _cli.Configure();
        _cli.ServeCollection("odata/Jobs", "jobs.json");
        var run = await _cli.RunAsync(["jobs", "list", "--query", "where State == 'Faulted' | select Id, State | take 2"]);
        run.ExitCode.ShouldBe(ExitCodes.Success);
        run.Out.ShouldStartWith("## jobs list — 2 righe");
    }

    [Fact]
    public async Task Exit1UnexpectedStatus()
    {
        _cli.Configure();
        _cli.ServeRaw("odata/Jobs", "{\"message\":\"teiera\"}", status: 418);
        var run = await _cli.RunAsync(["jobs", "list", "-o", "json"]);
        run.ExitCode.ShouldBe(ExitCodes.Generic);
        run.Json["error"]!["code"]!.GetValue<string>().ShouldBe("http_error");
    }

    [Theory]
    [InlineData("{ \"auth\": { \"scopes\": \"OR.Jobs.Write\" } }", "non è di sola lettura")]
    [InlineData("{ \"http\": { \"pageSize\": 0 } }", "http.pageSize")]
    public async Task Exit2InvalidConfiguration(string patch, string fragment)
    {
        _cli.Configure(patch);
        var run = await _cli.RunAsync(["jobs", "list"]);
        run.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
        run.Out.ShouldStartWith("ERRORE [2]:");
        run.Out.ShouldContain(fragment);
        _cli.OrchestratorRequests().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("jobs", "delete")]
    [InlineData("jobs", "list", "--output", "xml")]
    [InlineData("jobs", "list", "--verbosity", "loud")]
    [InlineData("--sconosciuta")]
    public async Task Exit2InvalidUsage(params string[] args)
    {
        _cli.Configure();
        var run = await _cli.RunAsync(args);
        run.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
    }

    [Fact]
    public async Task Exit2CyclicBatch()
    {
        _cli.Configure();
        var run = await _cli.RunAsync(["run", "--request", "-"], """{ "commands": [ { "id": "a", "query": "from $b" }, { "id": "b", "query": "from $a" } ] }""");
        run.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
        run.Out.ShouldContain("cicliche");
    }

    [Fact]
    public async Task Exit3AuthenticationFailed()
    {
        _cli.Configure();
        _cli.Server.Reset();
        _cli.Server.Given(Request.Create().WithPath(CliHarness.TokenPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(401).WithBody("{\"error\":\"invalid_client\"}"));
        var run = await _cli.RunAsync(["jobs", "list", "-o", "json"]);
        run.ExitCode.ShouldBe(ExitCodes.Authentication);
        run.Json["error"]!["code"]!.GetValue<string>().ShouldBe("authentication_failed");
        run.Out.ShouldNotContain("segreto");
        run.Error.ShouldNotContain("segreto");
    }

    [Fact]
    public async Task Exit3Unauthorized()
    {
        _cli.Configure();
        _cli.ServeRaw("odata/Jobs", "{}", status: 401);
        (await _cli.RunAsync(["jobs", "list"])).ExitCode.ShouldBe(ExitCodes.Authentication);
    }

    [Fact]
    public async Task Exit4NotFound()
    {
        _cli.Configure();
        _cli.ServeRaw("odata/Jobs(999)", "{\"message\":\"Job inesistente\"}", status: 404);
        var run = await _cli.RunAsync(["jobs", "get", "999"]);
        run.ExitCode.ShouldBe(ExitCodes.AccessDenied);
        run.Out.ShouldContain("Job inesistente");
    }

    [Fact]
    public async Task Exit4Forbidden()
    {
        _cli.Configure();
        _cli.ServeRaw("odata/Jobs", "{}", status: 403);
        (await _cli.RunAsync(["jobs", "list"])).ExitCode.ShouldBe(ExitCodes.AccessDenied);
    }

    [Fact]
    public async Task Exit5QueryError()
    {
        _cli.Configure();
        var run = await _cli.RunAsync(["jobs", "list", "-q", "where State = 'x'", "-o", "json"]);
        run.ExitCode.ShouldBe(ExitCodes.Query);
        run.Json["error"]!["message"]!.GetValue<string>().ShouldContain("posizione 12");
        _cli.OrchestratorRequests().ShouldBeEmpty();
    }

    [Fact]
    public async Task Exit6GlobalTimeout()
    {
        _cli.Configure("{ \"limits\": { \"globalTimeoutSeconds\": 1 } }");
        _cli.ServeRaw("odata/Jobs", "{\"value\":[]}", delayMs: 4000);
        var started = DateTimeOffset.UtcNow;
        var run = await _cli.RunAsync(["jobs", "list", "-o", "json"]);
        run.ExitCode.ShouldBe(ExitCodes.Timeout);
        (DateTimeOffset.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(3.5));
        run.Json["error"]!["exitCode"]!.GetValue<int>().ShouldBe(ExitCodes.Timeout);
    }

    [Fact]
    public async Task Exit7PartialBatch()
    {
        _cli.Configure();
        _cli.ServeCollection("odata/Jobs", "jobs.json");
        _cli.ServeRaw("odata/Jobs(5)", "{}", status: 404);
        var run = await _cli.RunAsync(["run", "-r", "-", "-o", "json"], """{ "commands": [ { "id": "ok", "command": "jobs count" }, { "id": "ko", "command": "jobs get 5" } ] }""");
        run.ExitCode.ShouldBe(ExitCodes.PartialSuccess);
        run.Json["ok"]!["@odata.count"]!.GetValue<int>().ShouldBe(40);
        run.Json["ko"]!["error"]!["exitCode"]!.GetValue<int>().ShouldBe(4);
    }

    [Fact]
    public async Task Exit8UnavailableAfterRetries()
    {
        _cli.Configure("{ \"http\": { \"maxRetries\": 2 } }");
        _cli.ServeRaw("odata/Jobs", "{}", status: 503);
        var run = await _cli.RunAsync(["jobs", "list"]);
        run.ExitCode.ShouldBe(ExitCodes.Unavailable);
        _cli.OrchestratorRequests().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Exit8Unreachable()
    {
        _cli.Configure("{ \"orchestrator\": { \"baseUrl\": \"http://127.0.0.1:1/\" }, \"http\": { \"maxRetries\": 0 } }");
        (await _cli.RunAsync(["jobs", "list"])).ExitCode.ShouldBe(ExitCodes.Unavailable);
    }

    [Fact]
    public async Task Exit130Cancelled()
    {
        _cli.Configure();
        _cli.ServeRaw("odata/Jobs", "{\"value\":[]}", delayMs: 5000);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var run = await _cli.RunAsync(["jobs", "list", "-o", "json"], cancellationToken: cts.Token);
        run.ExitCode.ShouldBe(ExitCodes.Cancelled);
        run.Json["error"]!["code"]!.GetValue<string>().ShouldBe("cancelled");
    }
}
