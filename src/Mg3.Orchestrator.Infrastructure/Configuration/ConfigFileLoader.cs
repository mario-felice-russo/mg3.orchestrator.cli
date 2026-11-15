using System.Text.Json;
using System.Text.RegularExpressions;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Microsoft.Extensions.Options;

namespace Mg3.Orchestrator.Infrastructure.Configuration;

/// <summary>Carica e valida il file di configurazione indicato con <c>--config</c>. È l'unica fonte di configurazione.</summary>
public sealed partial class ConfigFileLoader(IValidateOptions<Mg3Profile> validator)
{
    public ConfigFileLoader()
        : this(new Mg3ProfileValidator())
    {
    }

    [GeneratedRegex("'(?<name>[^']+)' could not be mapped", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex UnmappedProperty();

    /// <param name="path">Percorso del file; relativo a <paramref name="workingDirectory"/>.</param>
    public Mg3Profile Load(string? path, string workingDirectory, bool validate = true)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ConfigurationException("Manca il file di configurazione: aggiungi --config <percorso>. Per crearlo: mg3orch init --config <percorso>.");
        }

        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(workingDirectory, path);
        if (!File.Exists(fullPath))
        {
            throw new ConfigurationException($"Il file di configurazione '{path}' non esiste. Per crearlo: mg3orch init --config {path}");
        }

        var profile = ConfigFileMapper.ToProfile(Parse(File.ReadAllText(fullPath), path));
        if (!validate)
        {
            return profile;
        }

        var result = validator.Validate(null, profile);
        if (result.Failed)
        {
            throw new ConfigurationException($"Configurazione '{path}' non valida: " + string.Join(" ", result.Failures));
        }

        return profile;
    }

    public static ConfigFile Parse(string json, string source)
    {
        try
        {
            return JsonSerializer.Deserialize<ConfigFile>(json, ConfigurationJson.ReadOptions)
                ?? throw new ConfigurationException($"Il file di configurazione '{source}' è vuoto.");
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"Configurazione '{source}' non valida: {Explain(ex)}");
        }
    }

    public static IEnumerable<string> KeysOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name));
    }

    private static string Explain(JsonException ex)
    {
        if (UnmappedProperty().Match(ex.Message) is { Success: true } match)
        {
            var inAdvanced = ex.Path?.Contains("advanced", StringComparison.OrdinalIgnoreCase) == true;
            var section = inAdvanced ? " in 'advanced'" : string.Empty;
            return $"chiave sconosciuta '{match.Groups["name"].Value}'{section}. Chiavi ammesse: {string.Join(", ", KeysOf(inAdvanced ? typeof(AdvancedSettings) : typeof(ConfigFile)))}.";
        }

        var key = string.IsNullOrEmpty(ex.Path) || ex.Path == "$" ? null : ex.Path.TrimStart('$', '.');
        return key is null
            ? $"il file non è un oggetto JSON valido ({ex.Message.Split(" Path:")[0]})."
            : $"valore non valido per '{key}'.";
    }
}
