using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Infrastructure.Configuration;

namespace Mg3.Orchestrator.Infrastructure.UnitTests;

public sealed class ConfigurationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("mg3orch-config-").FullName;

    private const string File = """
        {
          // commento ammesso
          "profiles": {
            "default": {
              "orchestrator": { "baseUrl": "https://cloud.test/org/tenant/orchestrator_/", "folder": { "path": "Shared" } },
              "auth": { "mode": "ClientCredentials", "identityUrl": "https://cloud.test/identity_/connect/token", "clientId": "app", "clientSecret": "env:SECRET", "scopes": "OR.Jobs.Read" },
              "output": { "format": "text", "maxRows": 10 }
            },
            "onprem": {
              "orchestrator": { "baseUrl": "https://orch.local/", "tenantName": "Default" },
              "auth": { "mode": "Legacy", "username": "admin", "password": "file:PASSWORD_FILE" }
            }
          }
        }
        """;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string content, string name = "mg3orch.json")
    {
        var path = Path.Combine(_directory, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    private ConfigurationSources Sources(Dictionary<string, string>? env = null, string? profile = null, JsonObject? cli = null, JsonObject? request = null, string? path = null) =>
        new(path, profile, env ?? new Dictionary<string, string> { ["SECRET"] = "s3cr3t" }, _directory, cli, request);

    [Fact]
    public void LoadsDefaultFileAndResolvesSecrets()
    {
        Write(File);
        var profile = new ProfileLoader().Load(Sources());
        profile.Orchestrator.Folder.Path.ShouldBe("Shared");
        profile.Auth.ClientSecret.ShouldBe("s3cr3t");
        profile.Output.MaxRows.ShouldBe(10);
        profile.Http.PageSize.ShouldBe(500);
    }

    [Fact]
    public void LayersOverrideInOrder()
    {
        Write(File);
        var env = new Dictionary<string, string>
        {
            ["SECRET"] = "x",
            ["MG3ORCH__Output__MaxRows"] = "20",
            ["MG3ORCH__Output__Envelope"] = "true",
            ["MG3ORCH__Http__TimeoutSeconds"] = "15",
        };
        var cli = new JsonObject { ["output"] = new JsonObject { ["maxRows"] = 30, ["format"] = "Json" } };
        var request = new JsonObject { ["output"] = new JsonObject { ["MAXROWS"] = 40 } };
        var profile = new ProfileLoader().Load(Sources(env, cli: cli, request: request));
        profile.Output.MaxRows.ShouldBe(40);
        profile.Output.Format.ShouldBe(OutputFormat.Json);
        profile.Output.Envelope.ShouldBeTrue();
        profile.Http.TimeoutSeconds.ShouldBe(15);
    }

    [Fact]
    public void ProfileSelectionPrefersRequestThenCliThenEnvironment()
    {
        Write(File);
        var passwordFile = Write("  pw-da-file \n", "pw.txt");
        var env = new Dictionary<string, string> { ["PASSWORD_FILE"] = "unused", ["MG3ORCH_PROFILE"] = "default", ["SECRET"] = "x" };
        var loader = new ProfileLoader();
        var envProfile = loader.Load(Sources(env));
        envProfile.Auth.Mode.ShouldBe(AuthMode.ClientCredentials);

        var file = File.Replace("file:PASSWORD_FILE", "file:" + passwordFile.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.Ordinal);
        Write(file);
        var cliProfile = loader.Load(Sources(env, profile: "onprem"));
        cliProfile.Auth.Password.ShouldBe("pw-da-file");
        cliProfile.Orchestrator.TenantName.ShouldBe("Default");

        var requestProfile = loader.Load(Sources(env, profile: "default", request: new JsonObject { ["profile"] = "onprem" }));
        requestProfile.Auth.Mode.ShouldBe(AuthMode.Legacy);
    }

    [Fact]
    public void FileWithoutProfilesIsASingleProfile()
    {
        var path = Write("""{ "orchestrator": { "baseUrl": "https://x.test/" }, "auth": { "mode": "Pat", "token": "t" } }""", "single.json");
        var profile = new ProfileLoader().Load(Sources(path: path));
        profile.Auth.Token.ShouldBe("t");
    }

    [Fact]
    public void ConfigPathCanComeFromEnvironment()
    {
        var path = Write("""{ "orchestrator": { "baseUrl": "https://x.test/" }, "auth": { "mode": "Pat", "token": "t" } }""", "env.json");
        var profile = new ProfileLoader().Load(Sources(new Dictionary<string, string> { ["MG3ORCH_CONFIG"] = path }));
        profile.Orchestrator.BaseUrl.ShouldBe("https://x.test/");
    }

    [Theory]
    [InlineData("""{ "orchestrator": { "baseUrl": "https://x.test/" }, "auth": { "mode": "Pat", "token": "t" }, "sconosciuta": 1 }""", "sconosciuta")]
    [InlineData("""{ "http": { "timeoutSeconds": "abc" } }""", "http.timeoutSeconds")]
    [InlineData("""{ "auth": { "mode": "Kerberos" } }""", "auth.mode")]
    [InlineData("""[1, 2]""", "oggetto JSON")]
    [InlineData("""{ non json""", "non è JSON valido")]
    public void InvalidFilesReportTheKey(string content, string fragment)
    {
        var path = Write(content, "bad.json");
        var error = Should.Throw<ConfigurationException>(() => new ProfileLoader().Load(Sources(path: path)));
        error.Message.ShouldContain(fragment);
        error.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
    }

    [Fact]
    public void MissingExplicitFileOrProfileFails()
    {
        Should.Throw<ConfigurationException>(() => new ProfileLoader().Load(Sources(path: Path.Combine(_directory, "nope.json")))).Message.ShouldContain("non esiste");
        Write(File);
        Should.Throw<ConfigurationException>(() => new ProfileLoader().Load(Sources(profile: "prod"))).Message.ShouldContain("Profili disponibili: default, onprem");
    }

    [Fact]
    public void MissingSecretVariableFails()
    {
        Write(File);
        Should.Throw<ConfigurationException>(() => new ProfileLoader().Load(Sources(new Dictionary<string, string>()))).Message.ShouldContain("'SECRET'");
    }

    [Fact]
    public void WithoutValidationNoSecretIsResolved()
    {
        Write(File);
        new ProfileLoader().Load(Sources(new Dictionary<string, string>()), validate: false).Auth.ClientSecret.ShouldBe("env:SECRET");
    }

    [Fact]
    public void NoFileGivesDefaultsThatFailValidation()
    {
        var error = Should.Throw<ConfigurationException>(() => new ProfileLoader().Load(Sources()));
        error.Message.ShouldContain("[orchestrator.baseUrl] obbligatorio");
    }

    [Fact]
    public void SecretResolverReadsFilesAndRejectsMissing()
    {
        var resolver = new SecretResolver(new Dictionary<string, string>());
        resolver.Resolve(null).ShouldBeNull();
        resolver.Resolve("plain").ShouldBe("plain");
        Should.Throw<ConfigurationException>(() => resolver.Resolve("file:/non/esiste"));
    }

    [Fact]
    public void JsonMergeReplacesCaseInsensitively()
    {
        var target = new JsonObject { ["Output"] = new JsonObject { ["Format"] = "Text", ["MaxRows"] = 1 }, ["x"] = 1 };
        JsonMerge.Merge(target, new JsonObject { ["output"] = new JsonObject { ["format"] = "Json" }, ["X"] = new JsonObject() });
        target.ToJsonString().ShouldBe("{\"Output\":{\"MaxRows\":1,\"format\":\"Json\"},\"X\":{}}");
        JsonMerge.SetPath(target, ["x", "y"], 2);
        JsonMerge.SetPath(target, ["output", "maxrows"], 3);
        target.ToJsonString().ShouldBe("{\"Output\":{\"format\":\"Json\",\"maxrows\":3},\"X\":{\"y\":2}}");
    }
}
