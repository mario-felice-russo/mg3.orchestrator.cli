using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Configuration;

namespace Mg3.Orchestrator.Abstractions.Http;

/// <param name="Path">Percorso relativo a <c>baseUrl</c> (es. <c>odata/Jobs</c>).</param>
/// <param name="Folder">Cartella; null per le risorse non legate a una cartella.</param>
/// <param name="Paginate">True per seguire la paginazione <c>$top</c>/<c>$skip</c>.</param>
/// <remarks>La forma del payload viene rilevata dalla risposta.</remarks>
public sealed record ReadRequest(string Path, ODataQueryOptions Options, FolderSettings? Folder, bool Paginate);

/// <summary>Risultato di una lettura: payload originale (pagine unite) e righe estratte.</summary>
public sealed record ReadResult
{
    public required JsonNode? Payload { get; init; }
    public required PayloadShape Shape { get; init; }
    public required IReadOnlyList<JsonNode?> Rows { get; init; }
    public int Pages { get; init; } = 1;

    /// <summary>Valore di <c>@odata.count</c>, se presente.</summary>
    public long? TotalCount { get; init; }
    public bool Truncated { get; init; }

    /// <summary>True se la lettura è stata interrotta da una cancellazione dopo aver ricevuto dati.</summary>
    public bool Interrupted { get; init; }
}

/// <summary>Lettore di sola lettura verso Orchestrator.</summary>
public interface IOrchestratorReader
{
    Task<ReadResult> ReadAsync(ReadRequest request, CancellationToken cancellationToken);
}
