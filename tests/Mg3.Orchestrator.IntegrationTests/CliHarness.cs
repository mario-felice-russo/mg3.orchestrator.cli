using System.Text.Json.Nodes;
using Mg3.Orchestrator.Cli.Hosting;
using Mg3.Orchestrator.Tests.Shared;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Mg3.Orchestrator.IntegrationTests;

public sealed record CliRun(int ExitCode, string Out, string Error)
{
    public JsonNode Json => JsonNode.Parse(Out)!;
}

/// <summary>Orchestrator simulato con WireMock e invocazione in-process della CLI. Nessuna chiamata reale.</summary>
public sealed class CliHarness : IDisposable
{
    public const string Prefix = "/org/tenant/orchestrator_";
    public const string TokenPath = "/identity_/connect/token";

    private readonly string _directory = Directory.CreateTempSubdirectory("mg3orch-it-").FullName;

    public CliHarness()
    {
        Server = WireMockServer.Start();
        Server.Given(Request.Create().WithPath(TokenPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("{\"access_token\":\"tok\",\"expires_in\":3600}"));
    }

    public WireMockServer Server { get; }

    public string BaseUrl => Server.Url + Prefix + "/";

    public Dictionary<string, string> Environment { get; } = new() { ["APP_SECRET"] = "segreto" };

    public void Dispose()
    {
        Server.Stop();
        Server.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Scrive mg3orch.json nella cartella di lavoro; <paramref name="patch"/> viene fuso nel profilo.</summary>
    public void Configure(string? patch = null)
    {
        var profile = JsonNode.Parse($$"""
            {
              "orchestrator": { "baseUrl": "{{BaseUrl}}", "folder": { "path": "Shared" } },
              "auth": { "mode": "ClientCredentials", "identityUrl": "{{Server.Url}}{{TokenPath}}", "clientId": "app", "clientSecret": "env:APP_SECRET", "scopes": "OR.Jobs.Read OR.Folders.Read" },
              "http": { "maxRetries": 2, "retryBaseDelayMs": 0, "pageSize": 15, "timeoutSeconds": 10 },
              "limits": { "globalTimeoutSeconds": 20 },
              "output": { "timeZone": "UTC", "culture": "en-US" }
            }
            """)!.AsObject();
        if (patch is not null)
        {
            Infrastructure.Configuration.JsonMerge.Merge(profile, JsonNode.Parse(patch)!.AsObject());
        }

        var root = new JsonObject { ["profiles"] = new JsonObject { ["default"] = profile } };
        File.WriteAllText(Path.Combine(_directory, "mg3orch.json"), root.ToJsonString());
    }

    /// <summary>Endpoint OData con paginazione reale ($top/$skip/$count) sulle fixture.</summary>
    public void ServeCollection(string path, string fixture)
    {
        var rows = Fixtures.Rows(fixture);
        Server.Given(Request.Create().WithPath(Prefix + "/" + path).UsingGet())
            .RespondWith(Response.Create().WithCallback(request =>
            {
                var query = request.Query ?? new Dictionary<string, WireMock.Types.WireMockList<string>>();
                int Param(string name, int fallback) => query.TryGetValue(name, out var v) ? int.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture) : fallback;
                var page = new JsonObject { ["@odata.context"] = "ctx" };
                if (query.ContainsKey("$count"))
                {
                    page["@odata.count"] = rows.Count;
                }

                page["value"] = new JsonArray(rows.Skip(Param("$skip", 0)).Take(Param("$top", 1000)).Select(r => r!.DeepClone()).ToArray());
                return new WireMock.ResponseMessage
                {
                    StatusCode = 200,
                    Headers = new Dictionary<string, WireMock.Types.WireMockList<string>> { ["Content-Type"] = new("application/json") },
                    BodyData = new WireMock.Util.BodyData { BodyAsString = page.ToJsonString(), DetectedBodyType = WireMock.Types.BodyType.String },
                };
            }));
    }

    public void ServeRaw(string path, string body, int status = 200, int delayMs = 0)
    {
        var response = Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body);
        if (delayMs > 0)
        {
            response = response.WithDelay(delayMs);
        }

        Server.Given(Request.Create().WithPath(Prefix + "/" + path).UsingGet()).RespondWith(response);
    }

    public async Task<CliRun> RunAsync(string[] args, string stdin = "", CancellationToken cancellationToken = default)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var input = new StringReader(stdin);
        var environment = new CliEnvironment(output, error, input, Environment, _directory);
        var code = await CliApplication.RunAsync(args, environment, cancellationToken);
        return new CliRun(code, output.ToString(), error.ToString());
    }

    public IEnumerable<WireMock.Logging.ILogEntry> OrchestratorRequests() =>
        Server.LogEntries.Where(e => e.RequestMessage!.Path.StartsWith(Prefix, StringComparison.Ordinal));

    public static string Query(WireMock.Logging.ILogEntry entry, string name) => entry.RequestMessage!.Query![name][0];

    public static string Header(WireMock.Logging.ILogEntry entry, string name) => entry.RequestMessage!.Headers![name][0];

    public static string Method(WireMock.Logging.ILogEntry entry) => entry.RequestMessage!.Method;

    public static string PathOf(WireMock.Logging.ILogEntry entry) => entry.RequestMessage!.Path;
}
