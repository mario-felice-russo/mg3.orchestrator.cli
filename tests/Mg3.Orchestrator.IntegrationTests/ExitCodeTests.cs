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
        _cli.Configure("{ \"format\": \"json\" }");
        _cli.ServeRaw("odata/Jobs", "{\"message\":\"teiera\"}", status: 418);
        var run = await _cli.RunAsync(["jobs", "list"]);
        run.ExitCode.ShouldBe(ExitCodes.Generic);
        run.Json["error"]!["code"]!.GetValue<string>().ShouldBe("http_error");
    }

    [Theory]
    [InlineData("{ \"scopes\": \"OR.Jobs.Write\" }", "non è di sola lettura")]
    [InlineData("{ \"advanced\": { \"pageSize\": 0 } }", "advanced.pageSize")]
    [InlineData("{ \"token\": \"t\" }", "un solo tipo di credenziali")]
    [InlineData("{ \"tokn\": \"t\" }", "chiave sconosciuta 'tokn'")]
    public async Task Exit2InvalidConfiguration(string patch, string fragment)
    {
        _cli.Configure(patch);
        var run = await _cli.RunAsync(["jobs", "list"]);
        run.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
        run.Out.ShouldStartWith("ERRORE (2 – uso o configurazione non validi):");
        run.Out.ShouldContain(fragment);
        _cli.OrchestratorRequests().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("jobs", "delete")]
    [InlineData("jobs", "list", "--output", "json")]
    [InlineData("jobs", "list", "--profile", "x")]
    [InlineData("--sconosciuta")]
    public async Task Exit2InvalidUsage(params string[] args)
    {
        _cli.Configure();
        var run = await _cli.RunAsync(args);
        run.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
    }

    [Theory]
    [InlineData(new[] { "--auth", "test" }, "'--auth' non è un'opzione valida per 'mg3orch'. Forse intendevi: mg3orch auth test")]
    [InlineData(new[] { "aut", "test" }, "Forse intendevi: mg3orch auth test")]
    [InlineData(new[] { "jobs", "lst" }, "Forse intendevi: mg3orch jobs list")]
    [InlineData(new[] { "jobs" }, "'mg3orch jobs' richiede un sottocomando: list, count, get.")]
    [InlineData(new[] { "jobs", "get" }, "'mg3orch jobs get' richiede l'argomento <key>.")]
    [InlineData(new[] { "run" }, "'mg3orch run' richiede l'opzione --request.")]
    [InlineData(new[] { "zzzzzz" }, "'zzzzzz' non è un comando valido per 'mg3orch'. Usa 'mg3orch --help' per l'elenco.")]
    public async Task Exit2ExplainsUsageErrors(string[] args, string expected)
    {
        var run = await _cli.RunAsync(args, withConfig: false);
        run.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
        run.Out.ShouldStartWith("ERRORE (2 – uso o configurazione non validi): ");
        run.Out.ShouldContain(expected);
        run.Out.Trim().Split('\n').Length.ShouldBe(1);
        run.Error.ShouldBeEmpty();
    }

    [Fact]
    public async Task Exit2MissingConfiguration()
    {
        var run = await _cli.RunAsync(["auth", "test"], withConfig: false);
        run.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
        run.Out.ShouldContain("aggiungi --config <percorso>");
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
        _cli.Configure("{ \"format\": \"json\" }");
        _cli.Server.Reset();
        _cli.Server.Given(Request.Create().WithPath(CliHarness.TokenPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(401).WithBody("{\"error\":\"invalid_client\"}"));
        var run = await _cli.RunAsync(["jobs", "list"]);
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
        _cli.Configure("{ \"format\": \"json\" }");
        var run = await _cli.RunAsync(["jobs", "list", "-q", "where State = 'x'"]);
        run.ExitCode.ShouldBe(ExitCodes.Query);
        run.Json["error"]!["message"]!.GetValue<string>().ShouldContain("posizione 12");
        _cli.OrchestratorRequests().ShouldBeEmpty();
    }

    [Fact]
    public async Task Exit6GlobalTimeout()
    {
        _cli.Configure("{ \"format\": \"json\", \"advanced\": { \"globalTimeoutSeconds\": 1 } }");
        _cli.ServeRaw("odata/Jobs", "{\"value\":[]}", delayMs: 4000);
        var started = DateTimeOffset.UtcNow;
        var run = await _cli.RunAsync(["jobs", "list"]);
        run.ExitCode.ShouldBe(ExitCodes.Timeout);
        (DateTimeOffset.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(3.5));
        run.Json["error"]!["exitCode"]!.GetValue<int>().ShouldBe(ExitCodes.Timeout);
    }

    [Fact]
    public async Task Exit7PartialBatch()
    {
        _cli.Configure("{ \"format\": \"json\" }");
        _cli.ServeCollection("odata/Jobs", "jobs.json");
        _cli.ServeRaw("odata/Jobs(5)", "{}", status: 404);
        var run = await _cli.RunAsync(["run", "-r", "-"], """{ "commands": [ { "id": "ok", "command": "jobs count" }, { "id": "ko", "command": "jobs get 5" } ] }""");
        run.ExitCode.ShouldBe(ExitCodes.PartialSuccess);
        run.Json["ok"]!["@odata.count"]!.GetValue<int>().ShouldBe(40);
        run.Json["ko"]!["error"]!["exitCode"]!.GetValue<int>().ShouldBe(4);
    }

    [Fact]
    public async Task Exit8UnavailableAfterRetries()
    {
        _cli.Configure("{ \"advanced\": { \"maxRetries\": 2 } }");
        _cli.ServeRaw("odata/Jobs", "{}", status: 503);
        var run = await _cli.RunAsync(["jobs", "list"]);
        run.ExitCode.ShouldBe(ExitCodes.Unavailable);
        _cli.OrchestratorRequests().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Exit8Unreachable()
    {
        _cli.Configure("{ \"url\": \"http://127.0.0.1:1/\", \"advanced\": { \"maxRetries\": 0 } }");
        (await _cli.RunAsync(["jobs", "list"])).ExitCode.ShouldBe(ExitCodes.Unavailable);
    }

    [Fact]
    public async Task Exit130Cancelled()
    {
        _cli.Configure("{ \"format\": \"json\" }");
        _cli.ServeRaw("odata/Jobs", "{\"value\":[]}", delayMs: 5000);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var run = await _cli.RunAsync(["jobs", "list"], cancellationToken: cts.Token);
        run.ExitCode.ShouldBe(ExitCodes.Cancelled);
        run.Json["error"]!["code"]!.GetValue<string>().ShouldBe("cancelled");
    }
}
