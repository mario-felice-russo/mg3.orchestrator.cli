using System.Net;
using System.Text.Json.Nodes;
using System.Web;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Http;
using Mg3.Orchestrator.Infrastructure.Http;
using Mg3.Orchestrator.Tests.Shared;

namespace Mg3.Orchestrator.Infrastructure.UnitTests;

public sealed class ReaderTests
{
    /// <summary>Server OData minimale: applica $top/$skip alle fixture.</summary>
    private static StubHandler PagingServer(bool includeCount = true) => new(request => PagingResponse(request, includeCount));

    private static HttpResponseMessage PagingResponse(HttpRequestMessage request, bool includeCount = true)
    {
        var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
        var rows = Fixtures.Rows("jobs.json");
        var skip = int.Parse(query["$skip"] ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        var top = int.Parse(query["$top"] ?? "1000", System.Globalization.CultureInfo.InvariantCulture);
        var page = new JsonObject { ["@odata.context"] = "ctx" };
        if (includeCount && query["$count"] == "true")
        {
            page["@odata.count"] = rows.Count;
        }

        page["value"] = new JsonArray(rows.Skip(skip).Take(top).Select(r => r!.DeepClone()).ToArray());
        return StubHandler.Json(page.ToJsonString());
    }

    private static OrchestratorReader Reader(StubHandler handler, int pageSize = 15, int maxItems = 1000, int maxPages = 50) =>
        new(new StubHttpClientFactory(handler), Profiles.Valid() with
        {
            Http = new HttpSettings { PageSize = pageSize },
            Limits = new LimitsSettings { MaxItems = maxItems, MaxPages = maxPages },
        });

    private static ReadRequest List(ODataQueryOptions? options = null) => new("odata/Jobs", options ?? new ODataQueryOptions { Count = true }, null, Paginate: true);

    [Fact]
    public async Task FollowsPaginationAndMergesPages()
    {
        var handler = PagingServer();
        var result = await Reader(handler).ReadAsync(List(), CancellationToken.None);
        result.Rows.Count.ShouldBe(40);
        result.Pages.ShouldBe(3);
        result.Truncated.ShouldBeFalse();
        result.TotalCount.ShouldBe(40);
        result.Shape.ShouldBe(PayloadShape.ODataCollection);
        result.Payload!["value"]!.AsArray().Count.ShouldBe(40);
        result.Payload["@odata.count"]!.GetValue<int>().ShouldBe(40);
        handler.Uris.ShouldBe(
        [
            "https://orch.test/org/tenant/orchestrator_/odata/Jobs?$top=15&$count=true",
            "https://orch.test/org/tenant/orchestrator_/odata/Jobs?$top=15&$skip=15&$count=true",
            "https://orch.test/org/tenant/orchestrator_/odata/Jobs?$top=15&$skip=30&$count=true",
        ]);
    }

    [Fact]
    public async Task StopsWhenPageIsShortWithoutCount()
    {
        var handler = PagingServer(includeCount: false);
        var result = await Reader(handler, pageSize: 25).ReadAsync(List(new ODataQueryOptions()), CancellationToken.None);
        result.Rows.Count.ShouldBe(40);
        result.Pages.ShouldBe(2);
        result.Truncated.ShouldBeFalse();
    }

    [Fact]
    public async Task MaxItemsTruncates()
    {
        var result = await Reader(PagingServer(), maxItems: 20).ReadAsync(List(), CancellationToken.None);
        result.Rows.Count.ShouldBe(20);
        result.Truncated.ShouldBeTrue();
    }

    [Fact]
    public async Task MaxPagesTruncates()
    {
        var result = await Reader(PagingServer(), pageSize: 5, maxPages: 2).ReadAsync(List(), CancellationToken.None);
        result.Rows.Count.ShouldBe(10);
        result.Pages.ShouldBe(2);
        result.Truncated.ShouldBeTrue();
    }

    [Fact]
    public async Task UserTopIsNotTruncation()
    {
        var handler = PagingServer();
        var result = await Reader(handler).ReadAsync(List(new ODataQueryOptions { Top = 20, Skip = 5, Filter = "State eq 'x'", OrderBy = "Id desc", Select = "Id", Expand = "Robot" }), CancellationToken.None);
        result.Rows.Count.ShouldBe(20);
        result.Truncated.ShouldBeFalse();
        handler.Uris[0].ShouldBe("https://orch.test/org/tenant/orchestrator_/odata/Jobs?$filter=State%20eq%20%27x%27&$select=Id&$orderby=Id%20desc&$expand=Robot&$top=15&$skip=5&$count=true");
    }

    [Fact]
    public async Task NonPaginatedReadsDetectShape()
    {
        var array = await Reader(new StubHandler(_ => StubHandler.Json(Fixtures.Text("jobstats.json")))).ReadAsync(new ReadRequest("api/Stats/GetJobsStats", ODataQueryOptions.Empty, null, false), CancellationToken.None);
        array.Shape.ShouldBe(PayloadShape.Array);
        array.Rows.Count.ShouldBe(3);

        var entity = await Reader(new StubHandler(_ => StubHandler.Json("{\"Id\":1}"))).ReadAsync(new ReadRequest("odata/Jobs(1)", ODataQueryOptions.Empty, null, false), CancellationToken.None);
        entity.Shape.ShouldBe(PayloadShape.Entity);
        entity.Rows.Single()!["Id"]!.GetValue<int>().ShouldBe(1);

        var collection = await Reader(PagingServer()).ReadAsync(new ReadRequest("odata/Jobs", new ODataQueryOptions { Top = 2, Count = true }, null, false), CancellationToken.None);
        collection.Shape.ShouldBe(PayloadShape.ODataCollection);
        collection.TotalCount.ShouldBe(40);
    }

    [Fact]
    public async Task EntityResponseDuringPaginationIsReturnedAsIs()
    {
        var result = await Reader(new StubHandler(_ => StubHandler.Json("{\"Name\":\"x\"}"))).ReadAsync(List(), CancellationToken.None);
        result.Shape.ShouldBe(PayloadShape.Entity);
    }

    [Fact]
    public async Task EmptyBodyIsNull()
    {
        var result = await Reader(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) })).ReadAsync(new ReadRequest("odata/X", ODataQueryOptions.Empty, null, false), CancellationToken.None);
        result.Payload.ShouldBeNull();
        result.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task InvalidJsonFails()
    {
        var error = await Should.ThrowAsync<Mg3Exception>(() => Reader(new StubHandler(_ => StubHandler.Json("<html>"))).ReadAsync(new ReadRequest("odata/X", ODataQueryOptions.Empty, null, false), CancellationToken.None));
        error.Code.ShouldBe("invalid_json");
    }

    [Fact]
    public async Task MapsHttpErrors()
    {
        var error = await Should.ThrowAsync<AccessDeniedException>(() => Reader(new StubHandler(_ => StubHandler.Json("{}", HttpStatusCode.NotFound))).ReadAsync(List(), CancellationToken.None));
        error.Code.ShouldBe("not_found");
        await Should.ThrowAsync<OrchestratorUnavailableException>(() => Reader(new StubHandler(_ => throw new HttpRequestException("rete"))).ReadAsync(List(), CancellationToken.None));
    }

    [Fact]
    public async Task SetsFolderOnRequest()
    {
        var handler = PagingServer();
        await Reader(handler).ReadAsync(new ReadRequest("odata/Jobs", ODataQueryOptions.Empty, new FolderSettings { Path = "Shared" }, false), CancellationToken.None);
        handler.Requests[0].Options.TryGetValue(Mg3RequestOptions.Folder, out var folder).ShouldBeTrue();
        folder!.Path.ShouldBe("Shared");
    }

    [Fact]
    public async Task ReturnsPartialResultsWhenCancelledAfterFirstPage()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var handler = new StubHandler(request =>
        {
            if (++calls == 2)
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }

            return PagingResponse(request);
        });
        var result = await Reader(handler, pageSize: 10).ReadAsync(List(), cts.Token);
        result.Interrupted.ShouldBeTrue();
        result.Truncated.ShouldBeTrue();
        result.Rows.Count.ShouldBe(10);
    }
}
