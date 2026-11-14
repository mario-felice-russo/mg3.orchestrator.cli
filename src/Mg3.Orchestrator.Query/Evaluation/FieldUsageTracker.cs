namespace Mg3.Orchestrator.Query.Evaluation;

/// <summary>Registra i campi referenziati per segnalare quelli assenti da tutte le righe.</summary>
public sealed class FieldUsageTracker
{
    private readonly Dictionary<string, bool> _seen = new(StringComparer.OrdinalIgnoreCase);

    public void Record(string field, bool found)
    {
        _seen[field] = (_seen.TryGetValue(field, out var previous) && previous) || found;
    }

    public IEnumerable<string> MissingFields => _seen.Where(kv => !kv.Value).Select(kv => kv.Key).Order(StringComparer.Ordinal);
}
