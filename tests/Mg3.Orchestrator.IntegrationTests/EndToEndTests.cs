using System.Text.Json.Nodes;
using Mg3.Orchestrator.Tests.Shared;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Mg3.Orchestrator.IntegrationTests;

public sealed class EndToEndTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public EndToEndTests() => _cli.Configure();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task RawJsonIsIdenticalToOrchestratorPayload()
    {
_cli.Configure("{ \"format\": \"json\" }");
        _cli.ServeRaw("odata/Jobs", Fixtures.Text("jobs.json"));
        var run = await _cli.RunAsync(["jobs", "list"]);
        run.ExitCode.ShouldBe(0);
        run.Out.ShouldBe(Fixtures.Text("jobs.json") + Environment.NewLine);
    }

    [Fact]
    public async Task FollowsPaginationAndSendsHeaders()
    {
        _cli.Configure("{ \"format\": \"json\", \"folderPath\": \"Finance/Fatture\" }");
        _cli.ServeCollection("odata/Jobs", "jobs.json");
        var run = await _cli.RunAsync(["jobs", "list"]);
        run.Json["value"]!.AsArray().Count.ShouldBe(40);
        var requests = _cli.OrchestratorRequests().ToList();
        requests.Count.ShouldBe(3);
        requests.ShouldAllBe(r => CliHarness.Header(r, "X-UIPATH-FolderPath") == "Finance/Fatture");
        requests.ShouldAllBe(r => CliHarness.Header(r, "Authorization") == "Bearer tok");
        requests.ShouldAllBe(r => CliHarness.Method(r) == "GET");
    }

    [Fact]
    public async Task PushesQueryToODataAndReportsIt()
    {
        _cli.Configure("{ \"format\": \"json\", \"advanced\": { \"envelope\": true } }");
        _cli.ServeCollection("odata/Jobs", "jobs.json");
        var run = await _cli.RunAsync(["jobs", "list", "-q", "where State == 'Faulted' | orderby Id desc | take 3"]);
        run.ExitCode.ShouldBe(0);
        var envelope = run.Json;
        envelope["pushdown"]!.GetValue<string>().ShouldBe("$filter=State eq 'Faulted'; $orderby=Id desc; $top=3");
        var request = _cli.OrchestratorRequests().Single();
        CliHarness.Query(request, "$filter").ShouldBe("State eq 'Faulted'");
        CliHarness.Query(request, "$orderby").ShouldBe("Id desc");
        CliHarness.Query(request, "$top").ShouldBe("3");
        envelope["status"]!.GetValue<string>().ShouldBe("ok");
    }

    [Fact]
    public async Task RetriesOn429HonouringRetryAfter()
    {
        _cli.Server.Given(Request.Create().WithPath(CliHarness.Prefix + "/odata/Jobs").UsingGet()).InScenario("rate").WillSetStateTo("ok")
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "0"));
        _cli.Server.Given(Request.Create().WithPath(CliHarness.Prefix + "/odata/Jobs").UsingGet()).InScenario("rate").WhenStateIs("ok")
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("{\"value\":[{\"Id\":1}]}"));
        _cli.Configure("{ \"format\": \"json\" }");
        var run = await _cli.RunAsync(["jobs", "list"]);
        run.ExitCode.ShouldBe(0);
        _cli.OrchestratorRequests().Count().ShouldBe(2);
    }

    [Fact]
    public async Task DoesNotRetryOn404()
    {
        _cli.ServeRaw("odata/Jobs(1)", "{}", status: 404);
        await _cli.RunAsync(["jobs", "get", "1"]);
        _cli.OrchestratorRequests().Count().ShouldBe(1);
    }

    [Fact]
    public async Task PatAuthenticationSendsTokenWithoutIdentityCall()
    {
        _cli.ConfigurePat("pat-123");
        _cli.ServeCollection("odata/Releases", "releases.json");
        var run = await _cli.RunAsync(["releases", "list"]);
        run.ExitCode.ShouldBe(0);
        CliHarness.Header(_cli.OrchestratorRequests().Single(), "Authorization").ShouldBe("Bearer pat-123");
        _cli.Server.LogEntries.ShouldNotContain(e => CliHarness.PathOf(e) == CliHarness.TokenPath);
    }

    [Fact]
    public async Task LegacyAuthenticationUsesAccountEndpoint()
    {
        _cli.Configure("{ \"clientId\": null, \"clientSecret\": null, \"scopes\": null, \"identityUrl\": null, \"tenantName\": \"T1\", \"username\": \"admin\", \"password\": \"pw\", \"format\": \"json\" }");
        _cli.Server.Given(Request.Create().WithPath(CliHarness.Prefix + "/api/Account/Authenticate").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("{\"result\":\"legacy\"}"));
        _cli.ServeRaw("api/Stats/GetJobsStats", Fixtures.Text("jobstats.json"));
        var run = await _cli.RunAsync(["stats", "jobs", "-q", "where count > 0 | select title"]);
        run.ExitCode.ShouldBe(0);
        run.Out.TrimEnd().ShouldBe("[{\"title\":\"Successful\"},{\"title\":\"Faulted\"},{\"title\":\"Running\"}]");
        var get = _cli.OrchestratorRequests().Single(r => CliHarness.Method(r) == "GET");
        CliHarness.Header(get, "Authorization").ShouldBe("Bearer legacy");
        CliHarness.Header(get, "X-UIPATH-TenantName").ShouldBe("T1");
    }

    [Fact]
    public async Task BatchWithDependenciesAndTextOutput()
    {
        _cli.Configure("{ \"style\": \"markdown\" }");
        _cli.ServeCollection("odata/Jobs", "jobs.json");
        _cli.ServeCollection("odata/Releases", "releases.json");
        const string request = """
            {
              "commands": [
                { "id": "rel", "command": "releases list" },
                { "id": "faults", "command": "jobs list", "args": { "top": 40 }, "query": "where Info contains 'importo' | select Id, ReleaseName" },
                { "id": "sum", "query": "from $faults as f | join $rel as r on f.ReleaseName == r.Name | groupby r.ProcessKey | orderby count desc, r.ProcessKey" }
              ],
              "options": { "parallelism": 2, "stopOnError": true }
            }
            """;
        var run = await _cli.RunAsync(["run", "--request", "-"], request);
        run.ExitCode.ShouldBe(0);
        run.Out.ShouldContain("## sum — 4 righe");
        run.Out.ShouldContain("| r.ProcessKey | count |");
        run.Out.ShouldContain("| FattureProcess | 2 |");
    }

    [Fact]
    public async Task BatchRequestFromFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mg3-req-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """{ "commands": [ { "id": "s", "command": "stats jobs" } ] }""");
        try
        {
            _cli.Configure("{ \"format\": \"json\" }");
            _cli.ServeRaw("api/Stats/GetJobsStats", Fixtures.Text("jobstats.json"));
            var run = await _cli.RunAsync(["run", "-r", path]);
            run.Json["s"]!.AsArray().Count.ShouldBe(3);
            (await _cli.RunAsync(["run", "-r", path + ".missing"])).ExitCode.ShouldBe(2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("non json")]
    [InlineData("[]")]
    [InlineData("{ \"commands\": 1 }")]
    [InlineData("{ \"commands\": [1] }")]
    [InlineData("{ \"commands\": [ { \"command\": \"jobs\" } ] }")]
    [InlineData("{ \"commands\": [ { \"id\": \"a\", \"extra\": 1 } ] }")]
    [InlineData("{ \"commands\": [ { \"id\": \"a\", \"args\": [] } ] }")]
    [InlineData("{ \"commands\": [ { \"id\": \"a\", \"args\": { \"top\": {} } } ] }")]
    [InlineData("{ \"commands\": [ { \"id\": 1 } ] }")]
    [InlineData("{ \"commands\": [], \"options\": { \"parallelism\": \"x\" } }")]
    [InlineData("{ \"commands\": [], \"options\": { \"stopOnError\": 1 } }")]
    [InlineData("{ \"commands\": [], \"options\": { \"other\": 1 } }")]
    [InlineData("{ \"commands\": [], \"config\": {} }")]
    [InlineData("{ \"commands\": [], \"pippo\": 1 }")]
    [InlineData("{ \"commands\": [ { \"id\": \"a\", \"command\": \"jobs list\", \"args\": { \"pippo\": \"1\" } } ] }")]
    public async Task InvalidBatchRequestsExitWith2(string request)
    {
        (await _cli.RunAsync(["run", "-r", "-"], request)).ExitCode.ShouldBe(2);
    }

    [Fact]
    public async Task ExplainMakesNoCalls()
    {
        _cli.Configure("{ \"format\": \"json\" }");
        var run = await _cli.RunAsync(["jobs", "list", "-q", "where Id > 5 | groupby State", "--explain"]);
        run.ExitCode.ShouldBe(0);
        run.Json["server"]!["$filter"]!.GetValue<string>().ShouldBe("Id gt 5");
        _cli.Server.LogEntries.ShouldBeEmpty();
        _cli.Configure();
        var text = await _cli.RunAsync(["jobs", "list", "-q", "where lower(State) == 'x'", "--explain"]);
        text.Out.ShouldContain("client  where (lower(State) == 'x')");
    }

    [Fact]
    public async Task ODataGetReadsArbitraryPaths()
    {
        _cli.ServeRaw("odata/QueueDefinitions", "{\"value\":[{\"Id\":1,\"Name\":\"Coda\"}]}");
        _cli.Configure("{ \"format\": \"json\" }");
        var run = await _cli.RunAsync(["odata", "get", "odata/QueueDefinitions", "--top", "5"]);
        run.ExitCode.ShouldBe(0);
        run.Json["value"]![0]!["Name"]!.GetValue<string>().ShouldBe("Coda");
        CliHarness.Query(_cli.OrchestratorRequests().Single(), "$top").ShouldBe("5");
        (await _cli.RunAsync(["odata", "get", "https://evil.test/x"])).ExitCode.ShouldBe(2);
    }

    [Fact]
    public async Task ResourceCommandsCoverOptionsAndActions()
    {
        _cli.ServeCollection("odata/Jobs", "jobs.json");
        _cli.ServeRaw("odata/Processes/UiPath.Server.Configuration.OData.GetProcessVersions(processId='Pkg')", "{\"value\":[{\"Version\":\"1.0.0\"}]}");
        _cli.Configure("{ \"format\": \"json\" }");
        var since = await _cli.RunAsync(["jobs", "count", "--since", "2d", "--filter", "Source eq 'Manual'"]);
        since.ExitCode.ShouldBe(0);
        CliHarness.Query(_cli.OrchestratorRequests().Last(), "$filter").ShouldStartWith("(Source eq 'Manual') and (CreationTime ge ");
        _cli.Configure("{ \"style\": \"list\" }");
        var versions = await _cli.RunAsync(["packages", "versions", "Pkg"]);
        versions.ExitCode.ShouldBe(0);
        versions.Out.ShouldContain("Version: 1.0.0");
        var alias = await _cli.RunAsync(["processes", "list", "--select", "Id", "--orderby", "Id", "--skip", "1", "--expand", "Robot"]);
        alias.ExitCode.ShouldBe(4);
    }

    [Fact]
    public async Task CatalogSchemaAndConfigCommands()
    {
        _cli.Configure("{ \"format\": \"json\" }");
        var catalog = await _cli.RunAsync(["catalog"]);
        catalog.ExitCode.ShouldBe(0);
        catalog.Json.AsArray().Count.ShouldBe(33);

        var schema = await _cli.RunAsync(["schema"]);
        schema.ExitCode.ShouldBe(0);
        var tools = schema.Json["tools"]!.AsArray();
        tools.ShouldContain(t => t!["name"]!.GetValue<string>() == "jobs_list");
        tools.ShouldContain(t => t!["name"]!.GetValue<string>() == "run");
        schema.Json["exitCodes"]!["130"]!.GetValue<string>().ShouldNotBeEmpty();

        var config = await _cli.RunAsync(["config", "validate"]);
        config.ExitCode.ShouldBe(0);
        config.Json["clientSecret"]!.GetValue<string>().ShouldBe("***");
        config.Json["identityUrl"]!.GetValue<string>().ShouldEndWith("/identity_/connect/token");
        config.Json["advanced"]!["maxRows"]!.GetValue<int>().ShouldBe(200);
        config.Out.ShouldNotContain("segreto");
        config.Out.ShouldNotContain("isEmpty");
        _cli.Configure();
        var configText = await _cli.RunAsync(["config", "validate"]);
        configText.Out.ShouldContain("clientSecret");
        configText.Out.ShouldContain("advanced.timeZone");
    }

    [Fact]
    public async Task CatalogWorksWithoutConfiguration()
    {
        var run = await _cli.RunAsync(["catalog"], withConfig: false);
        run.ExitCode.ShouldBe(0);
        run.Out.ShouldStartWith("## catalog — 33 righe");
    }

    [Fact]
    public async Task AuthTestChecksTokenAndFolders()
    {
        _cli.Configure("{ \"format\": \"json\" }");
        _cli.ServeRaw("odata/Folders", "{\"value\":[]}");
        var run = await _cli.RunAsync(["auth", "test"]);
        run.ExitCode.ShouldBe(0);
        run.Json["status"]!.GetValue<string>().ShouldBe("ok");
        _cli.Server.Reset();
        _cli.ServeRaw("odata/Folders", "{}", status: 401);
        (await _cli.RunAsync(["auth", "test"])).ExitCode.ShouldBe(3);
    }

    [Fact]
    public async Task WarningsGoToStderrOnly()
    {
        _cli.Configure("{ \"folderPath\": null, \"format\": \"json\" }");
        _cli.ServeCollection("odata/Jobs", "jobs.json");
        var run = await _cli.RunAsync(["jobs", "list", "-q", "where Sconosciuto == 1"]);
        run.ExitCode.ShouldBe(0);
        run.Error.ShouldContain("AVVISO [cmd]: Nessuna cartella");
        run.Error.ShouldContain("Sconosciuto");
        JsonNode.Parse(run.Out)!["value"]!.AsArray().ShouldBeEmpty();
        _cli.Configure("{ \"folderPath\": null, \"advanced\": { \"verbosity\": \"quiet\" } }");
        var quiet = await _cli.RunAsync(["jobs", "list", "-q", "where Sconosciuto == 1"]);
        quiet.Error.ShouldBeEmpty();
    }

    [Fact]
    public async Task DiagnosticVerbosityLogsUrlsButNotTokens()
    {
        _cli.Configure("{ \"advanced\": { \"verbosity\": \"diagnostic\" } }");
        _cli.ServeCollection("odata/Jobs", "jobs.json");
        var run = await _cli.RunAsync(["jobs", "count"]);
        run.ExitCode.ShouldBe(0);
        run.Out.ShouldContain("count");
        run.Out.ShouldNotContain("HTTP GET");
    }

    [Fact]
    public async Task HelpAndVersionExitZero()
    {
        (await _cli.RunAsync(["--help"], withConfig: false)).ExitCode.ShouldBe(0);
        (await _cli.RunAsync(["--version"], withConfig: false)).ExitCode.ShouldBe(0);
    }
}
