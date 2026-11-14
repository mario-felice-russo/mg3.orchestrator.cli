using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Http;
using Mg3.Orchestrator.Infrastructure.Http;

namespace Mg3.Orchestrator.Infrastructure.Auth;

/// <summary>Base comune: invio della richiesta del token (unico POST ammesso) e gestione degli errori.</summary>
public abstract class TokenEndpointProvider(IHttpClientFactory httpClientFactory, TimeProvider time) : IAuthProvider
{
    public abstract AuthMode Mode { get; }

    protected TimeProvider Time => time;

    public abstract Task<AccessToken> AcquireAsync(Mg3Profile profile, CancellationToken cancellationToken);

    protected async Task<JsonObject> PostAsync(Uri uri, HttpContent content, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(Mg3RequestOptions.IdentityClient);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
        request.Options.Set(Mg3RequestOptions.TokenRequest, true);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new OrchestratorUnavailableException($"Servizio di identità non raggiungibile: {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await HttpErrorMapper.ToExceptionAsync(response, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 400 and < 500)
                {
                    throw new AuthenticationException($"Autenticazione fallita ({(int)response.StatusCode}): {error.Message}");
                }

                throw error;
            }

            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return JsonNode.Parse(body) as JsonObject ?? throw new AuthenticationException("Risposta del token non valida.");
            }
            catch (JsonException ex)
            {
                throw new AuthenticationException("Risposta del token non in formato JSON.", ex);
            }
        }
    }
}

/// <summary>OAuth client credentials (applicazione esterna su Automation Cloud o Identity Server on‑prem).</summary>
public sealed class ClientCredentialsAuthProvider(IHttpClientFactory httpClientFactory, TimeProvider time)
    : TokenEndpointProvider(httpClientFactory, time)
{
    public override AuthMode Mode => AuthMode.ClientCredentials;

    public override async Task<AccessToken> AcquireAsync(Mg3Profile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var auth = profile.Auth;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = auth.ClientId ?? string.Empty,
            ["client_secret"] = auth.ClientSecret ?? string.Empty,
        };
        if (!string.IsNullOrWhiteSpace(auth.Scopes))
        {
            form["scope"] = auth.Scopes;
        }

        using var content = new FormUrlEncodedContent(form);
        var json = await PostAsync(new Uri(auth.IdentityUrl!), content, cancellationToken).ConfigureAwait(false);
        var token = json["access_token"]?.GetValue<string>() ?? throw new AuthenticationException("Il servizio di identità non ha restituito 'access_token'.");
        var expiresIn = json["expires_in"] is JsonValue v && v.TryGetValue<int>(out var seconds) ? seconds : 3600;
        return new AccessToken(token, Time.GetUtcNow().AddSeconds(expiresIn));
    }
}

/// <summary>Personal Access Token, inviato come bearer.</summary>
public sealed class PatAuthProvider(TimeProvider time) : IAuthProvider
{
    public AuthMode Mode => AuthMode.Pat;

    public Task<AccessToken> AcquireAsync(Mg3Profile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var token = profile.Auth.Token ?? throw new ConfigurationException("auth.token è obbligatorio con auth.mode = Pat.");
        return Task.FromResult(new AccessToken(token, time.GetUtcNow().AddYears(1)));
    }
}

/// <summary>Autenticazione legacy on‑prem: <c>POST api/Account/Authenticate</c>.</summary>
public sealed class LegacyAuthProvider(IHttpClientFactory httpClientFactory, TimeProvider time)
    : TokenEndpointProvider(httpClientFactory, time)
{
    public static readonly TimeSpan AssumedLifetime = TimeSpan.FromMinutes(30);

    public override AuthMode Mode => AuthMode.Legacy;

    public override async Task<AccessToken> AcquireAsync(Mg3Profile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var body = new JsonObject
        {
            ["tenancyName"] = profile.Orchestrator.TenantName ?? "Default",
            ["usernameOrEmailAddress"] = profile.Auth.Username,
            ["password"] = profile.Auth.Password,
        };
        using var content = JsonContent.Create(body);
        var uri = new Uri(new Uri(EnsureTrailingSlash(profile.Orchestrator.BaseUrl!)), "api/Account/Authenticate");
        var json = await PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
        var token = json["result"]?.GetValue<string>() ?? throw new AuthenticationException("Orchestrator non ha restituito il token ('result').");
        return new AccessToken(token, Time.GetUtcNow().Add(AssumedLifetime));
    }

    internal static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
