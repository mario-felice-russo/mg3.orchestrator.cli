using Mg3.Orchestrator.Abstractions.Errors;

namespace Mg3.Orchestrator.Core.Batch;

/// <summary>Valida id e riferimenti e restituisce un ordine topologico stabile (grafo aciclico).</summary>
public static class DependencyGraph
{
    public static IReadOnlyList<PreparedCommand> Order(IReadOnlyList<PreparedCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count == 0)
        {
            throw new ConfigurationException("La richiesta non contiene comandi.");
        }

        var byId = new Dictionary<string, PreparedCommand>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            if (string.IsNullOrWhiteSpace(command.Id))
            {
                throw new ConfigurationException("Ogni comando deve avere un 'id' non vuoto.");
            }

            if (!byId.TryAdd(command.Id, command))
            {
                throw new ConfigurationException($"Id di comando duplicato: '{command.Id}'.");
            }
        }

        foreach (var command in commands)
        {
            foreach (var dependency in command.Dependencies.Where(d => !byId.ContainsKey(d)))
            {
                throw new ConfigurationException($"Il comando '{command.Id}' fa riferimento a '${dependency}', che non esiste.");
            }
        }

        var ordered = new List<PreparedCommand>(commands.Count);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var pending = commands.ToList();
        while (pending.Count > 0)
        {
            var ready = pending.FirstOrDefault(c => c.Dependencies.All(done.Contains))
                ?? throw new ConfigurationException($"Dipendenze cicliche tra i comandi: {string.Join(", ", pending.Select(c => c.Id))}.");
            ordered.Add(ready);
            done.Add(ready.Id);
            pending.Remove(ready);
        }

        return ordered;
    }
}
