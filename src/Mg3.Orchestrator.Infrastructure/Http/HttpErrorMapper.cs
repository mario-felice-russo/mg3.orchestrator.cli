using System.Net;
using System.Text.Json;
using Mg3.Orchestrator.Abstractions.Errors;

namespace Mg3.Orchestrator.Infrastructure.Http;

/// <summary>Converte risposte HTTP non riuscite in eccezioni con exit code.</summary>
public static class HttpErrorMapper
{
    public static async Task<Mg3Exception> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        var detail = await ReadMessageAsync(response, cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        var path = response.RequestMessage?.RequestUri?.AbsolutePath;
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail}";
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new AuthenticationException($"Orchestrator ha rifiutato le credenziali (401) su {path}{suffix}"),
            HttpStatusCode.Forbidden => new AccessDeniedException("access_denied", $"Accesso negato (403) su {path}{suffix}"),
            HttpStatusCode.NotFound => new AccessDeniedException("not_found", $"Risorsa non trovata (404) su {path}{suffix}"),
            HttpStatusCode.BadRequest => new Mg3Exception(ExitCodes.InvalidUsage, "bad_request", $"Richiesta non valida (400) su {path}{suffix}"),
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => new OrchestratorUnavailableException($"Orchestrator non disponibile ({status}) su {path} dopo i tentativi previsti{suffix}"),
            _ when status >= 500 => new OrchestratorUnavailableException($"Orchestrator ha risposto {status} su {path} dopo i tentativi previsti{suffix}"),
            _ => new Mg3Exception(ExitCodes.Generic, "http_error", $"Risposta inattesa {status} su {path}{suffix}"),
        };
    }

    private static async Task<string?> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var name in new[] { "message", "error_description", "error" })
            {
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Corpo non JSON: si restituisce un estratto.
        }

        return body.Length > 200 ? body[..200] + "…" : body;
    }
}
