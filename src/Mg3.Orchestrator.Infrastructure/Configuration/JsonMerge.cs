using System.Text.Json.Nodes;

namespace Mg3.Orchestrator.Infrastructure.Configuration;

/// <summary>Fusione profonda di oggetti JSON con chiavi case-insensitive.</summary>
public static class JsonMerge
{
    public static void Merge(JsonObject target, JsonObject? source)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (source is null)
        {
            return;
        }

        foreach (var (key, value) in source.ToArray())
        {
            var existingKey = target.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Key;
            if (value is JsonObject sourceObject && existingKey is not null && target[existingKey] is JsonObject targetObject)
            {
                Merge(targetObject, sourceObject);
                continue;
            }

            if (existingKey is not null)
            {
                target.Remove(existingKey);
            }

            target[key] = value?.DeepClone();
        }
    }

    /// <summary>Imposta un valore a partire da un percorso (es. <c>Output</c>, <c>Format</c>).</summary>
    public static void SetPath(JsonObject root, IReadOnlyList<string> path, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);
        var current = root;
        for (var i = 0; i < path.Count - 1; i++)
        {
            var key = current.FirstOrDefault(kv => string.Equals(kv.Key, path[i], StringComparison.OrdinalIgnoreCase)).Key;
            if (key is null || current[key] is not JsonObject next)
            {
                next = new JsonObject();
                if (key is not null)
                {
                    current.Remove(key);
                }

                current[path[i]] = next;
            }

            current = next;
        }

        var last = current.FirstOrDefault(kv => string.Equals(kv.Key, path[^1], StringComparison.OrdinalIgnoreCase)).Key;
        if (last is not null)
        {
            current.Remove(last);
        }

        current[path[^1]] = value;
    }
}
