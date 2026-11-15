using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Infrastructure.Configuration;

namespace Mg3.Orchestrator.Infrastructure.UnitTests;

public sealed class ConfigurationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("mg3orch-config-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string content, string name = "config.json")
    {
        File.WriteAllText(Path.Combine(_directory, name), content);
        return name;
    }

    private Mg3Profile Load(string content, bool validate = true) => new ConfigFileLoader().Load(Write(content), _directory, validate);

    private string Error(string content) => Should.Throw<ConfigurationException>(() => Load(content)).Message;

    [Fact]
    public void MinimalPatFileUsesDefaults()
    {
        var profile = Load("""{ "url": "https://cloud.uipath.com/org/tenant/orchestrator_/", "folderId": 298109, "token": "pat" }""");
        profile.Auth.Mode.ShouldBe(AuthMode.Pat);
        profile.Auth.Token.ShouldBe("pat");
        profile.Orchestrator.Folder.Id.ShouldBe(298109);
        profile.Output.ShouldBe(new OutputSettings());
        profile.Http.ShouldBe(new HttpSettings());
        profile.Limits.ShouldBe(new LimitsSettings());
        profile.Verbosity.ShouldBe(LogVerbosity.Normal);
    }

    [Fact]
    public void ClientCredentialsDeriveIdentityUrl()
    {
        var cloud = Load("""{ "url": "https://cloud.uipath.com/org/tenant/orchestrator_/", "clientId": "a", "clientSecret": "b", "scopes": "OR.Jobs.Read" }""");
        cloud.Auth.Mode.ShouldBe(AuthMode.ClientCredentials);
        cloud.Auth.IdentityUrl.ShouldBe("https://cloud.uipath.com/identity_/connect/token");

        var onPrem = Load("""{ "url": "https://orch.local/", "clientId": "a", "clientSecret": "b", "scopes": "OR.Jobs.Read" }""");
        onPrem.Auth.IdentityUrl.ShouldBe("https://orch.local/identity/connect/token");

        var explicitUrl = Load("""{ "url": "https://orch.local/", "clientId": "a", "clientSecret": "b", "scopes": "OR.Jobs.Read", "identityUrl": "https://id.local/token" }""");
        explicitUrl.Auth.IdentityUrl.ShouldBe("https://id.local/token");
        ConfigFileMapper.DefaultIdentityUrl("non url").ShouldBeNull();
    }

    [Fact]
    public void LegacyCredentialsAndTenant()
    {
        var profile = Load("""{ "url": "https://orch.local/", "tenantName": "T1", "username": "u", "password": "p", "folderPath": "Shared" }""");
        profile.Auth.Mode.ShouldBe(AuthMode.Legacy);
        profile.Orchestrator.TenantName.ShouldBe("T1");
        profile.Orchestrator.Folder.Path.ShouldBe("Shared");
    }

    [Fact]
    public void FormatStyleAndAdvancedSettingsAreApplied()
    {
        var profile = Load("""
            {
              // i commenti sono ammessi
              "url": "https://x.test/", "token": "t", "format": "json", "style": "Markdown",
              "advanced": {
                "envelope": true, "maxRows": 5, "maxCellWidth": 10, "timeZone": "Europe/Rome", "culture": "en-US",
                "timeoutSeconds": 7, "maxRetries": 0, "retryBaseDelayMs": 1, "pageSize": 50, "maxItems": 60, "maxPages": 2,
                "globalTimeoutSeconds": 9, "maxJoinRows": 11, "tokenCache": "disk", "tokenCachePath": "/tmp/c.json", "verbosity": "diagnostic"
              },
            }
            """);
        profile.Output.ShouldBe(new OutputSettings { Format = OutputFormat.Json, TextStyle = TextStyle.Markdown, Envelope = true, MaxRows = 5, MaxCellWidth = 10, TimeZone = "Europe/Rome", Culture = "en-US" });
        profile.Http.ShouldBe(new HttpSettings { TimeoutSeconds = 7, MaxRetries = 0, RetryBaseDelayMs = 1, PageSize = 50 });
        profile.Limits.ShouldBe(new LimitsSettings { MaxItems = 60, MaxPages = 2, GlobalTimeoutSeconds = 9, MaxJoinRows = 11 });
        profile.Auth.TokenCache.ShouldBe(TokenCacheMode.Disk);
        profile.Auth.TokenCachePath.ShouldBe("/tmp/c.json");
        profile.Verbosity.ShouldBe(LogVerbosity.Diagnostic);
    }

    [Fact]
    public void MaskedFileRoundTripsWithoutSecrets()
    {
        var profile = Load("""{ "url": "https://x.test/", "clientId": "id", "clientSecret": "secret", "scopes": "OR.Jobs.Read" }""");
        var masked = ConfigFileMapper.ToMaskedFile(profile);
        masked.ClientSecret.ShouldBe("***");
        masked.ClientId.ShouldBe("id");
        masked.Token.ShouldBeNull();
        masked.Advanced!.MaxRows.ShouldBe(200);
        ConfigFileMapper.ToProfile(masked with { ClientSecret = "secret" }).ShouldBe(profile);
    }

    [Theory]
    [InlineData("""{ "url": "https://x.test/" }""", "Mancano le credenziali")]
    [InlineData("""{ "url": "https://x.test/", "token": "t", "clientId": "c" }""", "un solo tipo di credenziali")]
    [InlineData("""{ "url": "https://x.test/", "token": "t", "folderId": 1, "folderPath": "x" }""", "in un solo modo")]
    [InlineData("""{ "url": "https://x.test/", "tokn": "t" }""", "chiave sconosciuta 'tokn'. Chiavi ammesse: url, folderId")]
    [InlineData("""{ "url": "https://x.test/", "token": "t", "advanced": { "maxRow": 1 } }""", "chiave sconosciuta 'maxRow' in 'advanced'. Chiavi ammesse: envelope")]
    [InlineData("""{ "url": "https://x.test/", "token": "t", "advanced": { "maxRows": "tanti" } }""", "valore non valido per 'advanced.maxRows'")]
    [InlineData("""{ "url": "https://x.test/", "token": "t", "format": "xml" }""", "valore non valido per 'format'")]
    [InlineData("""[1, 2]""", "non è un oggetto JSON valido")]
    [InlineData("""{ non json""", "non è un oggetto JSON valido")]
    [InlineData("""null""", "è vuoto")]
    [InlineData("""{ "token": "t" }""", "[url] obbligatorio")]
    [InlineData("""{ "url": "https://x.test/", "clientId": "a", "clientSecret": "b" }""", "[scopes] obbligatorio")]
    [InlineData("""{ "url": "https://x.test/", "clientId": "a", "clientSecret": "b", "scopes": "OR.Jobs.Write" }""", "non è di sola lettura")]
    public void InvalidFilesExplainTheProblem(string content, string fragment)
    {
        Error(content).ShouldContain(fragment);
    }

    [Fact]
    public void PathIsRequiredAndMustExist()
    {
        Should.Throw<ConfigurationException>(() => new ConfigFileLoader().Load(null, _directory)).Message.ShouldContain("--config <percorso>");
        var missing = Should.Throw<ConfigurationException>(() => new ConfigFileLoader().Load("assente.json", _directory));
        missing.Message.ShouldContain("mg3orch init --config assente.json");
        missing.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
    }

    [Fact]
    public void AbsolutePathsAreSupported()
    {
        var name = Write("""{ "url": "https://x.test/", "token": "t" }""", "abs.json");
        new ConfigFileLoader().Load(Path.Combine(_directory, name), "/").Auth.Token.ShouldBe("t");
    }

    [Fact]
    public void WithoutValidationIncompleteFilesLoad()
    {
        Load("""{ "token": "t", "format": "json" }""", validate: false).Output.Format.ShouldBe(OutputFormat.Json);
    }

    [Fact]
    public void ExitCodesHaveDescriptions()
    {
        foreach (var code in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 130 })
        {
            ExitCodes.Describe(code).ShouldNotBe("codice sconosciuto");
        }

        ExitCodes.Describe(99).ShouldBe("codice sconosciuto");
    }
}
