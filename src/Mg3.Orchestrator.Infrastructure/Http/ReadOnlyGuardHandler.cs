using Mg3.Orchestrator.Abstractions.Errors;

namespace Mg3.Orchestrator.Infrastructure.Http;

/// <summary>Garantisce la sola lettura: ammette solo GET, più il POST marcato come richiesta del token.</summary>
public sealed class ReadOnlyGuardHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Method == HttpMethod.Get)
        {
            return base.SendAsync(request, cancellationToken);
        }

        if (request.Method == HttpMethod.Post && request.Options.TryGetValue(Mg3RequestOptions.TokenRequest, out var isToken) && isToken)
        {
            return base.SendAsync(request, cancellationToken);
        }

        throw new ReadOnlyViolationException($"Richiesta bloccata: il metodo {request.Method} verso {request.RequestUri?.AbsolutePath} non è ammesso (sola lettura).");
    }
}
