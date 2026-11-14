using Mg3.Orchestrator.Abstractions.Configuration;

namespace Mg3.Orchestrator.Abstractions.Http;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt)
{
    public bool IsValidAt(DateTimeOffset now) => ExpiresAt - TimeSpan.FromSeconds(60) > now;
}

/// <summary>Fornisce un token valido, usando la cache.</summary>
public interface ITokenProvider
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}

/// <summary>Ottiene un token per una specifica modalità di autenticazione.</summary>
public interface IAuthProvider
{
    AuthMode Mode { get; }

    Task<AccessToken> AcquireAsync(Mg3Profile profile, CancellationToken cancellationToken);
}

public interface ITokenCache
{
    AccessToken? TryGetToken(string cacheKey);

    void StoreToken(string cacheKey, AccessToken token);
}
