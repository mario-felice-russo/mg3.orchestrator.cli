using System.Net;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Http;
using Mg3.Orchestrator.Infrastructure.Auth;
using Mg3.Orchestrator.Infrastructure.Http;

namespace Mg3.Orchestrator.Infrastructure.UnitTests;

public sealed class AuthTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClientCredentialsPostsFormAndReadsToken()
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            request.Options.TryGetValue(Mg3RequestOptions.TokenRequest, out var isToken).ShouldBeTrue();
            isToken.ShouldBeTrue();
            return StubHandler.Json("{\"access_token\":\"abc\",\"expires_in\":120}");
        });
        var token = await new ClientCredentialsAuthProvider(new StubHttpClientFactory(handler), new FixedTimeProvider(Now)).AcquireAsync(Profiles.Valid(), CancellationToken.None);
        token.ShouldBe(new AccessToken("abc", Now.AddSeconds(120)));
        body.ShouldBe("grant_type=client_credentials&client_id=id&client_secret=secret&scope=OR.Jobs.Read+OR.Folders.Read");
        handler.Uris.Single().ShouldBe("https://orch.test/identity_/connect/token");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, typeof(AuthenticationException))]
    [InlineData(HttpStatusCode.Unauthorized, typeof(AuthenticationException))]
    [InlineData(HttpStatusCode.ServiceUnavailable, typeof(OrchestratorUnavailableException))]
    public async Task TokenErrorsAreMapped(HttpStatusCode status, Type expected)
    {
        var handler = new StubHandler(_ => StubHandler.Json("{\"error\":\"invalid_client\"}", status));
        var error = await Should.ThrowAsync<Mg3Exception>(() => new ClientCredentialsAuthProvider(new StubHttpClientFactory(handler), TimeProvider.System).AcquireAsync(Profiles.Valid(), CancellationToken.None));
        error.ShouldBeOfType(expected);
    }

    [Fact]
    public async Task MissingAccessTokenOrInvalidJsonFails()
    {
        var missing = new StubHandler(_ => StubHandler.Json("{}"));
        await Should.ThrowAsync<AuthenticationException>(() => new ClientCredentialsAuthProvider(new StubHttpClientFactory(missing), TimeProvider.System).AcquireAsync(Profiles.Valid(), CancellationToken.None));
        var invalid = new StubHandler(_ => StubHandler.Json("non json"));
        await Should.ThrowAsync<AuthenticationException>(() => new ClientCredentialsAuthProvider(new StubHttpClientFactory(invalid), TimeProvider.System).AcquireAsync(Profiles.Valid(), CancellationToken.None));
        var network = new StubHandler(_ => throw new HttpRequestException("dns"));
        await Should.ThrowAsync<OrchestratorUnavailableException>(() => new ClientCredentialsAuthProvider(new StubHttpClientFactory(network), TimeProvider.System).AcquireAsync(Profiles.Valid(), CancellationToken.None));
    }

    [Fact]
    public async Task LegacyAuthenticatesAgainstAccountEndpoint()
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return StubHandler.Json("{\"result\":\"legacy-token\"}");
        });
        var profile = Profiles.Valid() with
        {
            Orchestrator = new OrchestratorSettings { BaseUrl = "https://orch.local", TenantName = "T1" },
            Auth = new AuthSettings { Mode = AuthMode.Legacy, Username = "u", Password = "p" },
        };
        var token = await new LegacyAuthProvider(new StubHttpClientFactory(handler), new FixedTimeProvider(Now)).AcquireAsync(profile, CancellationToken.None);
        token.Value.ShouldBe("legacy-token");
        token.ExpiresAt.ShouldBe(Now.Add(LegacyAuthProvider.AssumedLifetime));
        handler.Uris.Single().ShouldBe("https://orch.local/api/Account/Authenticate");
        body.ShouldBe("{\"tenancyName\":\"T1\",\"usernameOrEmailAddress\":\"u\",\"password\":\"p\"}");
    }

    [Fact]
    public async Task PatReturnsConfiguredToken()
    {
        var provider = new PatAuthProvider(new FixedTimeProvider(Now));
        (await provider.AcquireAsync(Profiles.Valid() with { Auth = new AuthSettings { Mode = AuthMode.Pat, Token = "pat" } }, CancellationToken.None)).Value.ShouldBe("pat");
        await Should.ThrowAsync<ConfigurationException>(() => provider.AcquireAsync(Profiles.Valid() with { Auth = new AuthSettings { Mode = AuthMode.Pat } }, CancellationToken.None));
    }

    [Fact]
    public async Task TokenProviderCachesUntilExpiry()
    {
        var time = new FixedTimeProvider(Now);
        var auth = new CountingAuthProvider(AuthMode.ClientCredentials, TimeSpan.FromMinutes(10), time);
        using var provider = new TokenProvider([auth], new InMemoryTokenCache(), Profiles.Valid(), time);
        (await provider.GetTokenAsync(CancellationToken.None)).ShouldBe("token-1");
        (await provider.GetTokenAsync(CancellationToken.None)).ShouldBe("token-1");
        time.Now = Now.AddMinutes(9.5);
        (await provider.GetTokenAsync(CancellationToken.None)).ShouldBe("token-2");
        auth.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task TokenProviderRejectsUnsupportedMode()
    {
        using var provider = new TokenProvider([], new InMemoryTokenCache(), Profiles.Valid(), TimeProvider.System);
        await Should.ThrowAsync<ConfigurationException>(() => provider.GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public void FileTokenCachePersistsWithPrivatePermissions()
    {
        var directory = Directory.CreateTempSubdirectory("mg3orch-token-").FullName;
        try
        {
            var path = Path.Combine(directory, "sub", "cache.json");
            var token = new AccessToken("persisted", Now.AddHours(1));
            new FileTokenCache(path).StoreToken("chiave", token);
            new FileTokenCache(path).TryGetToken("chiave").ShouldBe(token);
            new FileTokenCache(path).TryGetToken("altra").ShouldBeNull();
            File.ReadAllText(path).ShouldNotContain("chiave");
            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.WriteAllText(path, "corrotto");
            new FileTokenCache(path).TryGetToken("chiave").ShouldBeNull();
            new FileTokenCache(Path.Combine(directory, "assente.json")).TryGetToken("x").ShouldBeNull();
            FileTokenCache.DefaultPath.ShouldEndWith("token-cache.json");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AccessTokenValidityHasSkew()
    {
        var token = new AccessToken("x", Now.AddSeconds(90));
        token.IsValidAt(Now).ShouldBeTrue();
        token.IsValidAt(Now.AddSeconds(31)).ShouldBeFalse();
    }
}
