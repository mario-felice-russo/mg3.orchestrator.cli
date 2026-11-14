using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Http;

namespace Mg3.Orchestrator.Infrastructure.Auth;

/// <summary>Seleziona il provider in base a <c>auth.mode</c> e conserva il token in cache.</summary>
public sealed class TokenProvider : ITokenProvider, IDisposable
{
    private readonly Dictionary<AuthMode, IAuthProvider> _providers;
    private readonly ITokenCache _cache;
    private readonly Mg3Profile _profile;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public TokenProvider(IEnumerable<IAuthProvider> providers, ITokenCache cache, Mg3Profile profile, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToDictionary(p => p.Mode);
        _cache = cache;
        _profile = profile;
        _time = time;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var key = CacheKey(_profile);
        if (_cache.TryGetToken(key) is { } cached && cached.IsValidAt(_time.GetUtcNow()))
        {
            return cached.Value;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetToken(key) is { } again && again.IsValidAt(_time.GetUtcNow()))
            {
                return again.Value;
            }

            if (!_providers.TryGetValue(_profile.Auth.Mode, out var provider))
            {
                throw new ConfigurationException($"Modalità di autenticazione non supportata: {_profile.Auth.Mode}");
            }

            var token = await provider.AcquireAsync(_profile, cancellationToken).ConfigureAwait(false);
            _cache.StoreToken(key, token);
            return token.Value;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    internal static string CacheKey(Mg3Profile profile) => string.Join(
        '|',
        profile.Auth.Mode,
        profile.Orchestrator.BaseUrl,
        profile.Auth.IdentityUrl,
        profile.Auth.ClientId,
        profile.Auth.Username,
        profile.Auth.Scopes);
}
