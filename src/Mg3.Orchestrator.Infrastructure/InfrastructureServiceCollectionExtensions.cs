using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Http;
using Mg3.Orchestrator.Infrastructure.Auth;
using Mg3.Orchestrator.Infrastructure.Configuration;
using Mg3.Orchestrator.Infrastructure.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Mg3.Orchestrator.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registra HTTP (sola lettura, resilienza, header), autenticazione e lettore.</summary>
    public static IServiceCollection AddMg3Infrastructure(this IServiceCollection services, Mg3Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        services.AddSingleton(profile);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<Mg3Profile>, Mg3ProfileValidator>();

        services.AddSingleton<IAuthProvider, ClientCredentialsAuthProvider>();
        services.AddSingleton<IAuthProvider, PatAuthProvider>();
        services.AddSingleton<IAuthProvider, LegacyAuthProvider>();
        services.AddSingleton<ITokenCache>(_ => profile.Auth.TokenCache == TokenCacheMode.Disk
            ? new FileTokenCache(profile.Auth.TokenCachePath ?? FileTokenCache.DefaultPath)
            : new InMemoryTokenCache());
        services.AddSingleton<ITokenProvider, TokenProvider>();

        services.AddTransient<ReadOnlyGuardHandler>();
        services.AddTransient<AuthenticationHandler>();
        services.AddTransient<FolderHeadersHandler>();
        services.AddTransient<DiagnosticLoggingHandler>();
        services.ConfigureHttpClientDefaults(builder => builder.RemoveAllLoggers());

        var baseUrl = profile.Orchestrator.BaseUrl is { } url ? new Uri(LegacyAuthProvider.EnsureTrailingSlash(url)) : null;
        var orchestrator = services.AddHttpClient(Mg3RequestOptions.OrchestratorClient, client =>
            {
                client.BaseAddress = baseUrl;
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<ReadOnlyGuardHandler>();
        orchestrator.AddResilienceHandler("mg3-orchestrator", builder => ConfigureResilience(builder, profile.Http));
        orchestrator
            .AddHttpMessageHandler<AuthenticationHandler>()
            .AddHttpMessageHandler<FolderHeadersHandler>()
            .AddHttpMessageHandler<DiagnosticLoggingHandler>();

        var identity = services.AddHttpClient(Mg3RequestOptions.IdentityClient, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddHttpMessageHandler<ReadOnlyGuardHandler>();
        identity.AddResilienceHandler("mg3-identity", builder => ConfigureResilience(builder, profile.Http));
        identity.AddHttpMessageHandler<DiagnosticLoggingHandler>();

        services.AddSingleton<IOrchestratorReader, OrchestratorReader>();
        return services;
    }

    /// <summary>Retry con backoff esponenziale e jitter su 408, 429 (rispettando Retry-After), 5xx ed errori di rete.</summary>
    private static void ConfigureResilience(ResiliencePipelineBuilder<HttpResponseMessage> builder, HttpSettings http)
    {
        if (http.MaxRetries > 0)
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = http.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(http.RetryBaseDelayMs),
                ShouldRetryAfterHeader = true,
            });
        }

        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.9,
            MinimumThroughput = 20,
            SamplingDuration = TimeSpan.FromSeconds(60),
            BreakDuration = TimeSpan.FromSeconds(60),
        });
        builder.AddTimeout(TimeSpan.FromSeconds(http.TimeoutSeconds));
    }
}
