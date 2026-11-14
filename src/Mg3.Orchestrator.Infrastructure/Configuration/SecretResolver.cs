using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;

namespace Mg3.Orchestrator.Infrastructure.Configuration;

/// <summary>Risolve <c>env:NOME</c> dalle variabili d'ambiente e <c>file:percorso</c> dal contenuto del file.</summary>
public sealed class SecretResolver(IReadOnlyDictionary<string, string> environment) : ISecretResolver
{
    public string? Resolve(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.StartsWith("env:", StringComparison.Ordinal))
        {
            var name = value[4..];
            return environment.TryGetValue(name, out var resolved) && !string.IsNullOrEmpty(resolved)
                ? resolved
                : throw new ConfigurationException($"La variabile d'ambiente '{name}' non è impostata.");
        }

        if (value.StartsWith("file:", StringComparison.Ordinal))
        {
            var path = value[5..];
            return File.Exists(path)
                ? File.ReadAllText(path).Trim()
                : throw new ConfigurationException($"Il file del segreto '{path}' non esiste.");
        }

        return value;
    }
}
