using System.Net;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Http;
using Mg3.Orchestrator.Infrastructure.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mg3.Orchestrator.Infrastructure.UnitTests;

public sealed class HandlerTests
{
    private static async Task<HttpResponseMessage> SendAsync(DelegatingHandler handler, HttpRequestMessage request, StubHandler? inner = null)
    {
        handler.InnerHandler = inner ?? new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = new HttpMessageInvoker(handler);
        return await invoker.SendAsync(request, CancellationToken.None);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    public async Task ReadOnlyGuardBlocksEveryMethodButGet(string method)
    {
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://orch.test/odata/Jobs");
        using var guard = new ReadOnlyGuardHandler();
        var error = await Should.ThrowAsync<ReadOnlyViolationException>(() => SendAsync(guard, request, inner));
        error.Message.ShouldContain(method);
        inner.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadOnlyGuardAllowsGetAndMarkedTokenPost()
    {
        using var guard = new ReadOnlyGuardHandler();
        using var get = new HttpRequestMessage(HttpMethod.Get, "https://orch.test/odata/Jobs");
        (await SendAsync(guard, get)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var guard2 = new ReadOnlyGuardHandler();
        using var post = new HttpRequestMessage(HttpMethod.Post, "https://orch.test/identity_/connect/token");
        post.Options.Set(Mg3RequestOptions.TokenRequest, true);
        (await SendAsync(guard2, post)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(7L, null, null, "X-UIPATH-OrganizationUnitId", "7")]
    [InlineData(null, "8d3c0c1a-0000-4000-8000-000000000001", null, "X-UIPATH-FolderKey", "8d3c0c1a-0000-4000-8000-000000000001")]
    [InlineData(null, null, "Shared/Finance", "X-UIPATH-FolderPath", "Shared/Finance")]
    [InlineData(7L, null, "Shared", "X-UIPATH-OrganizationUnitId", "7")]
    public async Task FolderHeadersFollowPriority(long? id, string? key, string? path, string header, string value)
    {
        var profile = Profiles.Valid() with { Orchestrator = Profiles.Valid().Orchestrator with { TenantName = "Default" } };
        using var handler = new FolderHeadersHandler(profile);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://orch.test/odata/Jobs");
        request.Options.Set(Mg3RequestOptions.Folder, new FolderSettings { Id = id, Key = key, Path = path });
        await SendAsync(handler, request);
        request.Headers.GetValues(header).Single().ShouldBe(value);
        request.Headers.GetValues(FolderHeadersHandler.TenantNameHeader).Single().ShouldBe("Default");
        request.Headers.Count().ShouldBe(2);
    }

    [Fact]
    public async Task FolderHeadersAreOmittedWithoutFolder()
    {
        using var handler = new FolderHeadersHandler(Profiles.Valid());
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://orch.test/odata/Jobs");
        await SendAsync(handler, request);
        request.Headers.ShouldBeEmpty();
    }

    [Fact]
    public async Task AuthenticationHandlerAddsBearer()
    {
        using var handler = new AuthenticationHandler(new StaticTokenProvider("abc"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://orch.test/odata/Jobs");
        await SendAsync(handler, request);
        request.Headers.Authorization!.ToString().ShouldBe("Bearer abc");
    }

    [Fact]
    public async Task DiagnosticHandlerPassesThrough()
    {
        using var handler = new DiagnosticLoggingHandler(NullLogger<DiagnosticLoggingHandler>.Instance);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://orch.test/odata/Jobs?$top=1");
        (await SendAsync(handler, request)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ExitCodes.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, ExitCodes.AccessDenied)]
    [InlineData(HttpStatusCode.NotFound, ExitCodes.AccessDenied)]
    [InlineData(HttpStatusCode.BadRequest, ExitCodes.InvalidUsage)]
    [InlineData(HttpStatusCode.TooManyRequests, ExitCodes.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ExitCodes.Unavailable)]
    [InlineData((HttpStatusCode)418, ExitCodes.Generic)]
    public async Task MapsHttpStatusToExitCode(HttpStatusCode status, int exitCode)
    {
        using var response = StubHandler.Json("{\"message\":\"dettaglio\",\"errorCode\":1}", status);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://orch.test/odata/Jobs");
        var error = await HttpErrorMapper.ToExceptionAsync(response, CancellationToken.None);
        error.ExitCode.ShouldBe(exitCode);
        error.Message.ShouldContain("dettaglio");
    }

    [Fact]
    public async Task ErrorMapperHandlesNonJsonBodies()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent(new string('x', 300)) };
        var error = await HttpErrorMapper.ToExceptionAsync(response, CancellationToken.None);
        error.Message.ShouldEndWith("…");
        using var empty = new HttpResponseMessage(HttpStatusCode.BadGateway);
        (await HttpErrorMapper.ToExceptionAsync(empty, CancellationToken.None)).Message.ShouldNotContain(":");
    }

    private sealed class StaticTokenProvider(string token) : ITokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);
    }
}
