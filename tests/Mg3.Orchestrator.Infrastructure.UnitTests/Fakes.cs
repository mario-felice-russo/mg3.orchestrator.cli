using System.Net;
using System.Text;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Http;

namespace Mg3.Orchestrator.Infrastructure.UnitTests;

/// <summary>Handler finto: registra le richieste e risponde con una funzione.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> Uris { get; } = [];

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        Uris.Add(request.RequestUri!.AbsoluteUri);
        return Task.FromResult(respond(request));
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler, string baseUrl = "https://orch.test/org/tenant/orchestrator_/") : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri(baseUrl) };
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class CountingAuthProvider(AuthMode mode, TimeSpan lifetime, TimeProvider time) : IAuthProvider
{
    public int Calls { get; private set; }

    public AuthMode Mode => mode;

    public Task<AccessToken> AcquireAsync(Mg3Profile profile, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new AccessToken($"token-{Calls}", time.GetUtcNow().Add(lifetime)));
    }
}

internal static class Profiles
{
    public static Mg3Profile Valid() => new()
    {
        Orchestrator = new OrchestratorSettings { BaseUrl = "https://orch.test/org/tenant/orchestrator_" },
        Auth = new AuthSettings
        {
            Mode = AuthMode.ClientCredentials,
            IdentityUrl = "https://orch.test/identity_/connect/token",
            ClientId = "id",
            ClientSecret = "secret",
            Scopes = "OR.Jobs.Read OR.Folders.Read",
        },
    };
}
