using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Execution;

namespace Mg3.Orchestrator.Cli.Handlers;

/// <summary>Legge e valida la richiesta batch JSON.</summary>
public static class BatchRequestReader
{
    private static readonly HashSet<string> RootKeys = new(StringComparer.OrdinalIgnoreCase) { "config", "commands", "options" };
    private static readonly HashSet<string> CommandKeys = new(StringComparer.OrdinalIgnoreCase) { "id", "command", "args", "query" };
    private static readonly HashSet<string> OptionKeys = new(StringComparer.OrdinalIgnoreCase) { "stopOnError", "parallelism" };

    public static BatchRequest Parse(string json, BatchOptions defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"La richiesta non è JSON valido: {ex.Message}");
        }

        if (root is not JsonObject obj)
        {
            throw new ConfigurationException("La richiesta deve essere un oggetto JSON con 'commands'.");
        }

        RejectUnknown(obj, RootKeys, "richiesta");
        var config = Get(obj, "config") switch
        {
            null => null,
            JsonObject c => (JsonObject)c.DeepClone(),
            _ => throw new ConfigurationException("'config' deve essere un oggetto."),
        };
        if (Get(obj, "commands") is not JsonArray commands)
        {
            throw new ConfigurationException("'commands' è obbligatorio e deve essere un array.");
        }

        var options = defaults;
        if (Get(obj, "options") is JsonObject optionsNode)
        {
            RejectUnknown(optionsNode, OptionKeys, "options");
            options = options with
            {
                StopOnError = Get(optionsNode, "stopOnError") is { } stop ? Bool(stop, "options.stopOnError") : options.StopOnError,
                Parallelism = Get(optionsNode, "parallelism") is { } p ? Int(p, "options.parallelism") : options.Parallelism,
            };
        }

        var list = commands.Select((node, index) => ParseCommand(node, index)).ToArray();
        return new BatchRequest(list, options, IsBatch: true, config);
    }

    private static CommandRequest ParseCommand(JsonNode? node, int index)
    {
        if (node is not JsonObject obj)
        {
            throw new ConfigurationException($"commands[{index}] deve essere un oggetto.");
        }

        RejectUnknown(obj, CommandKeys, $"commands[{index}]");
        var id = String(Get(obj, "id"), $"commands[{index}].id") ?? throw new ConfigurationException($"commands[{index}].id è obbligatorio.");
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Get(obj, "args") is { } argsNode)
        {
            if (argsNode is not JsonObject argsObject)
            {
                throw new ConfigurationException($"commands[{index}].args deve essere un oggetto.");
            }

            foreach (var (key, value) in argsObject)
            {
                args[key] = value switch
                {
                    JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
                    JsonValue v when v.GetValueKind() is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToJsonString(),
                    _ => throw new ConfigurationException($"commands[{index}].args.{key} deve essere una stringa, un numero o un booleano."),
                };
            }
        }

        return new CommandRequest(id, String(Get(obj, "command"), $"commands[{index}].command"), args, String(Get(obj, "query"), $"commands[{index}].query"));
    }

    private static JsonNode? Get(JsonObject obj, string key) =>
        obj.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private static void RejectUnknown(JsonObject obj, HashSet<string> allowed, string where)
    {
        var unknown = obj.Select(kv => kv.Key).Where(k => !allowed.Contains(k)).ToArray();
        if (unknown.Length > 0)
        {
            throw new ConfigurationException($"Chiavi sconosciute in {where}: {string.Join(", ", unknown)}. Ammesse: {string.Join(", ", allowed)}.");
        }
    }

    private static string? String(JsonNode? node, string where) => node switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => throw new ConfigurationException($"{where} deve essere una stringa."),
    };

    private static bool Bool(JsonNode node, string where) =>
        node is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? v.GetValue<bool>()
            : throw new ConfigurationException($"{where} deve essere un booleano.");

    private static int Int(JsonNode node, string where) =>
        node is JsonValue v && v.TryGetValue<int>(out var n)
            ? n
            : throw new ConfigurationException($"{where} deve essere un intero, trovato {node.ToJsonString().ToString(CultureInfo.InvariantCulture)}.");
}
