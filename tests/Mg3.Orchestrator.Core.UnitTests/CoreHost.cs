using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Abstractions.Http;
using Mg3.Orchestrator.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Mg3.Orchestrator.Core.UnitTests;

/// <summary>Lettore in memoria: applica $top/$skip alle fixture e registra le richieste.</summary>
internal sealed class FakeReader : IOrchestratorReader
{
    public ConcurrentQueue<ReadRequest> Requests { get; } = new();

    public Func<ReadRequest, CancellationToken, Task>? BeforeRead { get; set; }

    public Exception? Failure { get; set; }

    public async Task<ReadResult> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        if (BeforeRead is not null)
        {
            await BeforeRead(request, cancellationToken);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        var (fixture, shape) = request.Path switch
        {
            "odata/Jobs" => ("jobs.json", PayloadShape.ODataCollection),
            "odata/Releases" => ("releases.json", PayloadShape.ODataCollection),
            "api/Stats/GetJobsStats" => ("jobstats.json", PayloadShape.Array),
            "odata/Jobs(1001)" => (null, PayloadShape.Entity),
            _ => throw new AccessDeniedException("not_found", $"Percorso non simulato: {request.Path}"),
        };

        if (fixture is null)
        {
            var entity = Fixtures.Rows("jobs.json")[0];
            return new ReadResult { Payload = entity, Shape = shape, Rows = [entity] };
        }

        if (shape == PayloadShape.Array)
        {
            var array = JsonNode.Parse(Fixtures.Text(fixture))!.AsArray();
            return new ReadResult { Payload = array, Shape = shape, Rows = array.ToArray() };
        }

        var payload = Fixtures.Object(fixture);
        var rows = payload["value"]!.AsArray().ToList();
        if (request.Options.Filter is { } filter && filter.StartsWith("Key eq", StringComparison.Ordinal))
        {
            rows = rows.Where(r => filter.EndsWith(r!["Key"]!.GetValue<string>(), StringComparison.Ordinal)).ToList();
        }
        else if (request.Options.Filter is { } nameFilter && nameFilter.StartsWith("ReleaseName eq", StringComparison.Ordinal))
        {
            rows = rows.Where(r => nameFilter.Contains($"'{r!["ReleaseName"]!.GetValue<string>()}'", StringComparison.Ordinal)).ToList();
        }

        var selected = rows.Skip(request.Options.Skip ?? 0).Take(request.Options.Top ?? int.MaxValue).Select(r => r!.DeepClone()).ToArray();
        var result = new JsonObject { ["@odata.context"] = payload["@odata.context"]!.DeepClone(), ["@odata.count"] = rows.Count, ["value"] = new JsonArray(selected) };
        return new ReadResult { Payload = result, Shape = shape, Rows = result["value"]!.AsArray().ToArray(), TotalCount = rows.Count };
    }
}

internal static class CoreHost
{
    public static Mg3Profile Profile(int timeoutSeconds = 30) => new()
    {
        Orchestrator = new OrchestratorSettings { BaseUrl = "https://orch.test/", Folder = new FolderSettings { Path = "Shared" } },
        Auth = new AuthSettings { Mode = AuthMode.Pat, Token = "t" },
        Limits = new LimitsSettings { GlobalTimeoutSeconds = timeoutSeconds, MaxJoinRows = 1000 },
    };

    public static (IOrchestratorQueryService Service, FakeReader Reader, ServiceProvider Provider) Create(Mg3Profile? profile = null)
    {
        var reader = new FakeReader();
        var services = new ServiceCollection().AddMg3Orchestrator(profile ?? Profile());
        services.AddSingleton<IOrchestratorReader>(reader);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IOrchestratorQueryService>(), reader, provider);
    }

    public static CommandRequest Command(string id, string? command, string? query = null, Dictionary<string, string>? args = null) =>
        new(id, command, args ?? [], query);

    public static BatchRequest BatchOf(BatchOptions? options = null, params CommandRequest[] commands) =>
        new(commands, options ?? new BatchOptions(), IsBatch: true);

    public static BatchRequest Single(CommandRequest command, BatchOptions? options = null) =>
        new([command], options ?? new BatchOptions(), IsBatch: false);
}
