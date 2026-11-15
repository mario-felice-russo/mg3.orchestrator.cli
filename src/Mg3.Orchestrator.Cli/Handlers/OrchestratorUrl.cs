using System.Globalization;
using System.Web;
using Mg3.Orchestrator.Abstractions.Errors;

namespace Mg3.Orchestrator.Cli.Handlers;

/// <summary>Ricava URL base e cartella da un indirizzo copiato dal browser.</summary>
/// <example>
/// <c>https://cloud.uipath.com/org/tenant/orchestrator_/jobs?tid=1&amp;fid=298109</c> →
/// <c>https://cloud.uipath.com/org/tenant/orchestrator_/</c>, cartella 298109.
/// </example>
public sealed record OrchestratorUrl(string BaseUrl, long? FolderId)
{
    public static OrchestratorUrl Parse(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ConfigurationException($"'{value}' non è un URL http(s) valido. Esempio: https://cloud.uipath.com/<organizzazione>/<tenant>/orchestrator_/");
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.FindIndex(segments, s => string.Equals(s, "orchestrator_", StringComparison.OrdinalIgnoreCase));
        var basePath = index >= 0 ? string.Join('/', segments[..(index + 1)]) + "/" : string.Empty;
        var fid = HttpUtility.ParseQueryString(uri.Query)["fid"];
        long? folderId = long.TryParse(fid, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
        return new OrchestratorUrl($"{uri.Scheme}://{uri.Authority}/{basePath}", folderId);
    }

    public bool IsCloud => BaseUrl.EndsWith("/orchestrator_/", StringComparison.OrdinalIgnoreCase);
}
