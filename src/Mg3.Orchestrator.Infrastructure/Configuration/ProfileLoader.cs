using System.Text.Json;
using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Microsoft.Extensions.Options;

namespace Mg3.Orchestrator.Infrastructure.Configuration;

/// <summary>Carica, fonde, risolve i segreti e valida il profilo effettivo.</summary>
public sealed class ProfileLoader(IValidateOptions<Mg3Profile> validator)
{
    public const string ConfigEnvironmentVariable = "MG3ORCH_CONFIG";
    public const string ProfileEnvironmentVariable = "MG3ORCH_PROFILE";
    public const string EnvironmentPrefix = "MG3ORCH__";
    public const string DefaultFileName = "mg3orch.json";
    public const string DefaultProfile = "default";

    public ProfileLoader()
        : this(new Mg3ProfileValidator())
    {
    }

    public Mg3Profile Load(ConfigurationSources sources, bool validate = true)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var requestOverrides = sources.RequestOverrides?.DeepClone() as JsonObject;
        var requestProfile = TakeProfile(requestOverrides);
        var profileName = requestProfile ?? sources.Profile ?? sources.Environment.GetValueOrDefault(ProfileEnvironmentVariable) ?? DefaultProfile;

        var merged = LoadFileLayer(sources, profileName);
        JsonMerge.Merge(merged, EnvironmentLayer(sources.Environment));
        JsonMerge.Merge(merged, sources.CommandLineOverrides);
        JsonMerge.Merge(merged, requestOverrides);

        var profile = Deserialize(merged);
        if (!validate)
        {
            return profile;
        }

        profile = ResolveSecrets(profile, new SecretResolver(sources.Environment));
        var result = validator.Validate(null, profile);
        if (result.Failed)
        {
            throw new ConfigurationException("Configurazione non valida: " + string.Join(" ", result.Failures));
        }

        return profile;
    }

    private static string? TakeProfile(JsonObject? overrides)
    {
        var key = overrides?.FirstOrDefault(kv => string.Equals(kv.Key, "profile", StringComparison.OrdinalIgnoreCase)).Key;
        if (key is null)
        {
            return null;
        }

        var value = overrides![key]?.GetValue<string>();
        overrides.Remove(key);
        return value;
    }

    private static JsonObject LoadFileLayer(ConfigurationSources sources, string profileName)
    {
        var explicitPath = sources.ConfigPath ?? sources.Environment.GetValueOrDefault(ConfigEnvironmentVariable);
        var path = explicitPath ?? Path.Combine(sources.WorkingDirectory, DefaultFileName);
        if (!File.Exists(path))
        {
            if (explicitPath is not null)
            {
                throw new ConfigurationException($"Il file di configurazione '{explicitPath}' non esiste.");
            }

            return [];
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"Il file di configurazione '{path}' non è JSON valido: {ex.Message}");
        }

        if (root is not JsonObject obj)
        {
            throw new ConfigurationException($"Il file di configurazione '{path}' deve contenere un oggetto JSON.");
        }

        if (obj["profiles"] is not JsonObject profiles)
        {
            return obj;
        }

        var match = profiles.FirstOrDefault(kv => string.Equals(kv.Key, profileName, StringComparison.OrdinalIgnoreCase));
        if (match.Value is not JsonObject selected)
        {
            throw new ConfigurationException($"Il profilo '{profileName}' non esiste. Profili disponibili: {string.Join(", ", profiles.Select(p => p.Key))}.");
        }

        return (JsonObject)selected.DeepClone();
    }

    private static JsonObject EnvironmentLayer(IReadOnlyDictionary<string, string> environment)
    {
        var layer = new JsonObject();
        foreach (var (name, value) in environment.Where(kv => kv.Key.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var path = name[EnvironmentPrefix.Length..].Split("__", StringSplitOptions.RemoveEmptyEntries);
            if (path.Length > 0)
            {
                JsonMerge.SetPath(layer, path, JsonValue.Create(value));
            }
        }

        return layer;
    }

    private static Mg3Profile Deserialize(JsonObject merged)
    {
        try
        {
            return merged.Deserialize<Mg3Profile>(ConfigurationJson.Options) ?? new Mg3Profile();
        }
        catch (JsonException ex)
        {
            var key = string.IsNullOrEmpty(ex.Path) ? "(radice)" : ex.Path.TrimStart('$', '.');
            throw new ConfigurationException($"Configurazione non valida alla chiave '{key}': {ex.Message.Split(". Path:")[0]}");
        }
    }

    private static Mg3Profile ResolveSecrets(Mg3Profile profile, SecretResolver resolver) => profile with
    {
        Auth = profile.Auth with
        {
            ClientId = resolver.Resolve(profile.Auth.ClientId),
            ClientSecret = resolver.Resolve(profile.Auth.ClientSecret),
            Token = resolver.Resolve(profile.Auth.Token),
            Username = resolver.Resolve(profile.Auth.Username),
            Password = resolver.Resolve(profile.Auth.Password),
        },
    };
}
