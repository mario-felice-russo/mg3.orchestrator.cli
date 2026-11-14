using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Http;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Mg3.Orchestrator.Infrastructure.Http;

/// <summary>Esegue GET verso Orchestrator, segue la paginazione e unisce le pagine nel payload originale.</summary>
public sealed class OrchestratorReader(IHttpClientFactory httpClientFactory, Mg3Profile profile) : IOrchestratorReader
{
    public async Task<ReadResult> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = httpClientFactory.CreateClient(Mg3RequestOptions.OrchestratorClient);
        if (!request.Paginate)
        {
            var node = await GetAsync(client, request, request.Options, cancellationToken).ConfigureAwait(false);
            return Single(node);
        }

        return await ReadPagesAsync(client, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ReadResult> ReadPagesAsync(HttpClient client, ReadRequest request, CancellationToken cancellationToken)
    {
        var limits = profile.Limits;
        var cap = Math.Min(request.Options.Top ?? int.MaxValue, limits.MaxItems);
        var userCapped = request.Options.Top is { } top && top <= limits.MaxItems;
        var baseSkip = request.Options.Skip ?? 0;
        var items = new List<JsonNode?>();
        JsonObject? first = null;
        long? total = null;
        var pages = 0;
        var exhausted = false;
        try
        {
            while (items.Count < cap && pages < limits.MaxPages)
            {
                var pageSize = Math.Min(profile.Http.PageSize, cap - items.Count);
                var options = request.Options with { Top = pageSize, Skip = baseSkip + items.Count, Count = pages == 0 || request.Options.Count };
                var node = await GetAsync(client, request, options, cancellationToken).ConfigureAwait(false);
                pages++;
                if (node is not JsonObject page || page["value"] is not JsonArray values)
                {
                    return pages == 1 ? Single(node) : throw new Mg3Exception(ExitCodes.Generic, "unexpected_payload", $"La pagina {pages} di {request.Path} non contiene 'value'.");
                }

                first ??= page;
                total ??= ReadCount(page);
                var received = values.ToList();
                values.Clear();
                items.AddRange(received);
                if (received.Count < pageSize || (total is { } t && baseSkip + items.Count >= t))
                {
                    exhausted = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && items.Count > 0)
        {
            return Merge(first!, items, pages, total, truncated: true, interrupted: true);
        }

        var truncated = !exhausted && !(userCapped && items.Count >= cap);
        if (total is { } count && baseSkip + items.Count >= count)
        {
            truncated = false;
        }

        return first is null ? Single(new JsonObject { ["value"] = new JsonArray() }) : Merge(first, items, pages, total, truncated, interrupted: false);
    }

    private static ReadResult Merge(JsonObject first, List<JsonNode?> items, int pages, long? total, bool truncated, bool interrupted)
    {
        first["value"] = new JsonArray(items.ToArray());
        return new ReadResult
        {
            Payload = first,
            Shape = PayloadShape.ODataCollection,
            Rows = items,
            Pages = pages,
            TotalCount = total,
            Truncated = truncated,
            Interrupted = interrupted,
        };
    }

    private static ReadResult Single(JsonNode? node) => node switch
    {
        JsonObject obj when obj["value"] is JsonArray values => new ReadResult
        {
            Payload = obj,
            Shape = PayloadShape.ODataCollection,
            Rows = values.ToArray(),
            TotalCount = ReadCount(obj),
        },
        JsonArray array => new ReadResult { Payload = array, Shape = PayloadShape.Array, Rows = array.ToArray() },
        _ => new ReadResult { Payload = node, Shape = PayloadShape.Entity, Rows = node is null ? [] : [node] },
    };

    private static long? ReadCount(JsonObject page) =>
        page["@odata.count"] is JsonValue v && v.TryGetValue<long>(out var count) ? count : null;

    private static async Task<JsonNode?> GetAsync(HttpClient client, ReadRequest request, ODataQueryOptions options, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, BuildUri(request.Path, options));
        if (request.Folder is { IsEmpty: false } folder)
        {
            message.Options.Set(Mg3RequestOptions.Folder, folder);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new OrchestratorUnavailableException($"Orchestrator non raggiungibile: {ex.Message}", ex);
        }
        catch (BrokenCircuitException ex)
        {
            throw new OrchestratorUnavailableException("Orchestrator non disponibile: troppi errori consecutivi (circuit breaker aperto).", ex);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new OrchestratorUnavailableException("Orchestrator non ha risposto entro http.timeoutSeconds.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await HttpErrorMapper.ToExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                if (response.Content.Headers.ContentLength == 0)
                {
                    return null;
                }

                try
                {
                    return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (JsonException ex)
                {
                    throw new Mg3Exception(ExitCodes.Generic, "invalid_json", $"Risposta non JSON da {request.Path}: {ex.Message}", ex);
                }
            }
        }
    }

    internal static string BuildUri(string path, ODataQueryOptions options)
    {
        var query = new List<string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                query.Add($"{name}={Uri.EscapeDataString(value)}");
            }
        }

        Add("$filter", options.Filter);
        Add("$select", options.Select);
        Add("$orderby", options.OrderBy);
        Add("$expand", options.Expand);
        Add("$top", options.Top?.ToString(CultureInfo.InvariantCulture));
        Add("$skip", options.Skip is > 0 ? options.Skip.Value.ToString(CultureInfo.InvariantCulture) : null);
        if (options.Count)
        {
            query.Add("$count=true");
        }

        var sb = new StringBuilder(path.TrimStart('/'));
        if (query.Count > 0)
        {
            sb.Append(path.Contains('?', StringComparison.Ordinal) ? '&' : '?').AppendJoin('&', query);
        }

        return sb.ToString();
    }
}
