using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Cli.Handlers;

namespace Mg3.Orchestrator.IntegrationTests;

public sealed class InitTests : IDisposable
{
    private const string BrowserUrl = "https://cloud.uipath.com/reale_ites/Dev_Test/orchestrator_/overview-global/overview?tid=68140&fid=298109&index=0&size=25";

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private JsonObject Written() => JsonNode.Parse(File.ReadAllText(_cli.ConfigPath))!.AsObject();

    [Fact]
    public async Task CreatesPatConfigurationWithoutQuestions()
    {
        var run = await _cli.RunAsync(["init", "--url", BrowserUrl, "--token", "pat-1"]);
        run.ExitCode.ShouldBe(ExitCodes.Success);
        run.Out.ShouldContain("Verifica con: mg3orch auth test --config");
        Written().ToJsonString().ShouldBe("{\"url\":\"https://cloud.uipath.com/reale_ites/Dev_Test/orchestrator_/\",\"folderId\":298109,\"token\":\"pat-1\"}");
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(_cli.ConfigPath).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task AsksForMissingValues()
    {
        var run = await _cli.RunAsync(["init"], stdin: $"{BrowserUrl}\n2\napp\nsecret\n\n");
        run.ExitCode.ShouldBe(ExitCodes.Success);
        var file = Written();
        file["clientId"]!.GetValue<string>().ShouldBe("app");
        file["scopes"]!.GetValue<string>().ShouldBe(InitCommand.DefaultScopes);
        file["folderId"]!.GetValue<long>().ShouldBe(298109);
        file.ContainsKey("identityUrl").ShouldBeFalse();
        run.Out.ShouldContain("Tipo di credenziale");
    }

    [Fact]
    public async Task OnPremLegacyAsksForFolderAndTenant()
    {
        var run = await _cli.RunAsync(["init", "--url", "https://orch.local/"], stdin: "42\n3\nadmin\npw\nT1\n");
        run.ExitCode.ShouldBe(ExitCodes.Success);
        Written().ToJsonString().ShouldBe("{\"url\":\"https://orch.local/\",\"folderId\":42,\"tenantName\":\"T1\",\"username\":\"admin\",\"password\":\"pw\"}");
    }

    [Fact]
    public async Task ClientCredentialsFromOptions()
    {
        var run = await _cli.RunAsync(["init", "--url", BrowserUrl, "--client-id", "a", "--client-secret", "b", "--scopes", "OR.Jobs.Read", "--folder-path", "Shared"]);
        run.ExitCode.ShouldBe(ExitCodes.Success);
        var file = Written();
        file["folderPath"]!.GetValue<string>().ShouldBe("Shared");
        file.ContainsKey("folderId").ShouldBeFalse();
        (await _cli.RunAsync(["init", "--url", "https://orch.local/", "--username", "u", "--password", "p", "--tenant", "T", "--folder-id", "5", "--force"])).ExitCode.ShouldBe(0);
        Written()["folderId"]!.GetValue<long>().ShouldBe(5);
    }

    [Theory]
    [InlineData("", "indicalo con --url")]
    [InlineData("non-url\n", "non è un URL http(s) valido")]
    [InlineData("https://orch.local/\nabc\n", "non è un id di cartella valido")]
    [InlineData("https://orch.local/\n\n9\n", "Scelta non valida")]
    [InlineData("https://orch.local/\n\n1\n", "indicalo con --token")]
    public async Task ReportsMissingOrInvalidAnswers(string stdin, string fragment)
    {
        var run = await _cli.RunAsync(["init"], stdin: stdin);
        run.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
        run.Out.ShouldContain(fragment);
        File.Exists(_cli.ConfigPath).ShouldBeFalse();
    }

    [Fact]
    public async Task ValidatesBeforeWriting()
    {
        var run = await _cli.RunAsync(["init", "--url", BrowserUrl, "--client-id", "a", "--client-secret", "b", "--scopes", "OR.Jobs.Write"]);
        run.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
        run.Out.ShouldContain("non è di sola lettura");
        File.Exists(_cli.ConfigPath).ShouldBeFalse();
    }

    [Fact]
    public async Task RefusesToOverwriteWithoutForceAndRequiresPath()
    {
        _cli.Configure();
        (await _cli.RunAsync(["init", "--url", BrowserUrl, "--token", "x"])).Out.ShouldContain("--force");
        (await _cli.RunAsync(["init", "--url", BrowserUrl, "--token", "x"], withConfig: false)).Out.ShouldContain("mg3orch init --config <percorso>");
    }

    [Fact]
    public async Task CreatedFileWorksEndToEnd()
    {
        await _cli.RunAsync(["init", "--url", _cli.BaseUrl, "--token", "pat-e2e", "--folder-id", "7"]);
        _cli.ServeRaw("odata/Folders", "{\"value\":[]}");
        var run = await _cli.RunAsync(["auth", "test"]);
        run.ExitCode.ShouldBe(0);
        var request = _cli.OrchestratorRequests().Single();
        CliHarness.Header(request, "Authorization").ShouldBe("Bearer pat-e2e");
    }

    [Theory]
    [InlineData(BrowserUrl, "https://cloud.uipath.com/reale_ites/Dev_Test/orchestrator_/", 298109L)]
    [InlineData("https://cloud.uipath.com/org/tenant/orchestrator_", "https://cloud.uipath.com/org/tenant/orchestrator_/", null)]
    [InlineData("https://orch.local:8443/jobs?fid=abc", "https://orch.local:8443/", null)]
    public void ParsesBrowserUrls(string url, string baseUrl, long? folder)
    {
        var parsed = OrchestratorUrl.Parse(url);
        parsed.BaseUrl.ShouldBe(baseUrl);
        parsed.FolderId.ShouldBe(folder);
    }
}
