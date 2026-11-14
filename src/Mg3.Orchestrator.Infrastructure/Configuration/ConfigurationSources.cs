using System.Text.Json.Nodes;

namespace Mg3.Orchestrator.Infrastructure.Configuration;

/// <summary>Strati della configurazione, dal meno al più prioritario: file, ambiente, riga di comando, richiesta batch.</summary>
/// <param name="ConfigPath">Percorso esplicito (<c>--config</c>); se null si usano <c>MG3ORCH_CONFIG</c> e <c>./mg3orch.json</c>.</param>
public sealed record ConfigurationSources(
    string? ConfigPath,
    string? Profile,
    IReadOnlyDictionary<string, string> Environment,
    string WorkingDirectory,
    JsonObject? CommandLineOverrides = null,
    JsonObject? RequestOverrides = null);
