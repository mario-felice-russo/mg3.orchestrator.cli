using Mg3.Orchestrator.Abstractions.Configuration;

namespace Mg3.Orchestrator.Infrastructure.Http;

/// <summary>Chiavi delle opzioni associate alle singole richieste HTTP.</summary>
public static class Mg3RequestOptions
{
    /// <summary>Marca la richiesta del token: l'unico POST ammesso.</summary>
    public static readonly HttpRequestOptionsKey<bool> TokenRequest = new("mg3.token-request");

    /// <summary>Cartella da comunicare a Orchestrator per questa richiesta.</summary>
    public static readonly HttpRequestOptionsKey<FolderSettings> Folder = new("mg3.folder");

    public const string OrchestratorClient = "mg3-orchestrator";
    public const string IdentityClient = "mg3-identity";
}
