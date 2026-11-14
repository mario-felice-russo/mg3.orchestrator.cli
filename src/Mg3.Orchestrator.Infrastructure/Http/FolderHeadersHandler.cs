using System.Globalization;
using Mg3.Orchestrator.Abstractions.Configuration;

namespace Mg3.Orchestrator.Infrastructure.Http;

/// <summary>Comunica cartella e tenant tramite header, in ordine di priorità: id, key, path.</summary>
public sealed class FolderHeadersHandler(Mg3Profile profile) : DelegatingHandler
{
    public const string OrganizationUnitIdHeader = "X-UIPATH-OrganizationUnitId";
    public const string FolderKeyHeader = "X-UIPATH-FolderKey";
    public const string FolderPathHeader = "X-UIPATH-FolderPath";
    public const string TenantNameHeader = "X-UIPATH-TenantName";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Options.TryGetValue(Mg3RequestOptions.Folder, out var folder) && folder is not null)
        {
            if (folder.Id is { } id)
            {
                request.Headers.TryAddWithoutValidation(OrganizationUnitIdHeader, id.ToString(CultureInfo.InvariantCulture));
            }
            else if (!string.IsNullOrWhiteSpace(folder.Key))
            {
                request.Headers.TryAddWithoutValidation(FolderKeyHeader, folder.Key);
            }
            else if (!string.IsNullOrWhiteSpace(folder.Path))
            {
                request.Headers.TryAddWithoutValidation(FolderPathHeader, folder.Path);
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.Orchestrator.TenantName))
        {
            request.Headers.TryAddWithoutValidation(TenantNameHeader, profile.Orchestrator.TenantName);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
