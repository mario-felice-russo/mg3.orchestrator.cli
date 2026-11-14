using System.Net.Http.Headers;
using Mg3.Orchestrator.Abstractions.Http;

namespace Mg3.Orchestrator.Infrastructure.Http;

/// <summary>Aggiunge il bearer token a ogni richiesta verso Orchestrator.</summary>
public sealed class AuthenticationHandler(ITokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
