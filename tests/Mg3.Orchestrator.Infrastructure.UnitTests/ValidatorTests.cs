using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Infrastructure.Configuration;

namespace Mg3.Orchestrator.Infrastructure.UnitTests;

public sealed class ValidatorTests
{
    private static IEnumerable<string> Errors(Mg3Profile profile) => new Mg3ProfileValidator().Validate(null, profile).Failures ?? [];

    [Fact]
    public void ValidProfilePasses() => Errors(Profiles.Valid()).ShouldBeEmpty();

    [Fact]
    public void WriteScopesAreRejected()
    {
        var profile = Profiles.Valid() with { Auth = Profiles.Valid().Auth with { Scopes = "OR.Jobs.Read OR.Jobs.Write OR.Execution" } };
        Errors(profile).ShouldBe(
        [
            "[scopes] lo scope 'OR.Jobs.Write' non è di sola lettura: sono ammessi solo scope '*.Read'.",
            "[scopes] lo scope 'OR.Execution' non è di sola lettura: sono ammessi solo scope '*.Read'.",
        ]);
    }

    [Theory]
    [InlineData(AuthMode.ClientCredentials, "[identityUrl]")]
    [InlineData(AuthMode.Pat, "[token]")]
    [InlineData(AuthMode.Legacy, "[username]")]
    public void RequiresCredentialsForMode(AuthMode mode, string key)
    {
        var profile = Profiles.Valid() with { Auth = new AuthSettings { Mode = mode } };
        Errors(profile).ShouldContain(e => e.StartsWith(key, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatesUrlsRangesFolderAndOutput()
    {
        var profile = new Mg3Profile
        {
            Orchestrator = new OrchestratorSettings { BaseUrl = "ftp://x", Folder = new FolderSettings { Path = "Cartella è", Key = "non-guid" } },
            Auth = new AuthSettings { Mode = AuthMode.ClientCredentials, IdentityUrl = "relativo", ClientId = "a", ClientSecret = "b" },
            Http = new HttpSettings { TimeoutSeconds = 0, PageSize = 5000, MaxRetries = 11, RetryBaseDelayMs = -1 },
            Limits = new LimitsSettings { MaxItems = 0, MaxPages = 0, GlobalTimeoutSeconds = 0, MaxJoinRows = 0 },
            Output = new OutputSettings { TimeZone = "Marte/Olympus", Culture = "xx-YY-invalid", MaxRows = 0, MaxCellWidth = 1 },
        };
        var errors = Errors(profile).ToList();
        foreach (var key in new[]
        {
            "url", "folderPath", "folderKey", "identityUrl", "advanced.timeoutSeconds", "advanced.pageSize",
            "advanced.maxRetries", "advanced.retryBaseDelayMs", "advanced.maxItems", "advanced.maxPages", "advanced.globalTimeoutSeconds", "advanced.maxJoinRows",
            "advanced.timeZone", "advanced.culture", "advanced.maxRows", "advanced.maxCellWidth",
        })
        {
            errors.ShouldContain(e => e.StartsWith($"[{key}]", StringComparison.Ordinal), key);
        }
    }

    [Fact]
    public void FolderParseRecognizesIdKeyAndPath()
    {
        FolderSettings.Parse("42").Id.ShouldBe(42);
        FolderSettings.Parse("8d3c0c1a-0000-4000-8000-000000000001").Key.ShouldNotBeNull();
        FolderSettings.Parse("Shared/Finance").Path.ShouldBe("Shared/Finance");
        new FolderSettings().IsEmpty.ShouldBeTrue();
    }
}
