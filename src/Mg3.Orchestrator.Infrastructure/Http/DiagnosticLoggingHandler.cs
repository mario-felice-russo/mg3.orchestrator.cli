using Microsoft.Extensions.Logging;

namespace Mg3.Orchestrator.Infrastructure.Http;

/// <summary>Registra metodo, URL (con query string) ed esito. Non registra mai gli header.</summary>
public sealed partial class DiagnosticLoggingHandler(ILogger<DiagnosticLoggingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogRequest(logger, request.Method, request.RequestUri);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        LogResponse(logger, (int)response.StatusCode, request.RequestUri);
        return response;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "HTTP {Method} {Uri}")]
    private static partial void LogRequest(ILogger logger, HttpMethod method, Uri? uri);

    [LoggerMessage(Level = LogLevel.Debug, Message = "HTTP {StatusCode} da {Uri}")]
    private static partial void LogResponse(ILogger logger, int statusCode, Uri? uri);
}
