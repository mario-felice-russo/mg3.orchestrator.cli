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

    public string ConfigPath => Path.Combine(_directory, "mg3orch.json");

    public string WorkingDirectory => _directory;

    public void Dispose()
    {
        Server.Stop();
        Server.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Scrive il file di configurazione; <paramref name="patch"/> viene fuso (null rimuove una chiave).</summary>
    public void Configure(string? patch = null)
    {
        var config = JsonNode.Parse($$"""
            {
              "url": "{{BaseUrl}}",
              "folderPath": "Shared",
              "clientId": "app",
              "clientSecret": "segreto",
              "scopes": "OR.Jobs.Read OR.Folders.Read",
              "identityUrl": "{{Server.Url}}{{TokenPath}}",
              "advanced": { "maxRetries": 2, "retryBaseDelayMs": 0, "pageSize": 15, "timeoutSeconds": 10, "globalTimeoutSeconds": 20, "timeZone": "UTC", "culture": "en-US" }
            }
            """)!.AsObject();
        if (patch is not null)
        {
            Merge(config, JsonNode.Parse(patch)!.AsObject());
        }

        File.WriteAllText(ConfigPath, config.ToJsonString());
    }

    /// <summary>Usa un Personal Access Token al posto dell'applicazione esterna.</summary>
    public void ConfigurePat(string token, string? patch = null)
    {
        Configure($$"""{ "clientId": null, "clientSecret": null, "scopes": null, "identityUrl": null, "token": "{{token}}" }""");
        if (patch is not null)
        {
            var config = JsonNode.Parse(File.ReadAllText(ConfigPath))!.AsObject();
            Merge(config, JsonNode.Parse(patch)!.AsObject());
            File.WriteAllText(ConfigPath, config.ToJsonString());
        }
    }

    private static void Merge(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch.ToArray())
        {
            if (value is null)
            {
                target.Remove(key);
            }
            else if (value is JsonObject child && target[key] is JsonObject existing)
            {
                Merge(existing, child);
            }
            else
            {
                target[key] = value.DeepClone();
            }
        }
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

    /// <summary>Esegue la CLI aggiungendo --config, salvo che gli argomenti lo indichino già o <paramref name="withConfig"/> sia false.</summary>
    public async Task<CliRun> RunAsync(string[] args, string stdin = "", bool withConfig = true, CancellationToken cancellationToken = default)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var input = new StringReader(stdin);
        if (withConfig && !args.Contains("--config") && !args.Contains("-c"))
        {
            args = [.. args, "--config", ConfigPath];
        }

        var environment = new CliEnvironment(output, error, input, _directory);
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
