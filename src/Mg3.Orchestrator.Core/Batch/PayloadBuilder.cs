using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Catalog;

namespace Mg3.Orchestrator.Core.Batch;

/// <summary>Ricostruisce il payload con la stessa struttura dell'originale, contenente le righe trasformate.</summary>
public static class PayloadBuilder
{
    public static JsonNode? Build(JsonNode? original, PayloadShape shape, IReadOnlyList<JsonNode?> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var items = rows.Select(r => r?.DeepClone()).ToArray();
        switch (shape)
        {
            case PayloadShape.ODataCollection:
                var obj = new JsonObject();
                if (original is JsonObject source)
                {
                    if (source.TryGetPropertyValue("@odata.context", out var context))
                    {
                        obj["@odata.context"] = context?.DeepClone();
                    }

                    if (source.ContainsKey("@odata.count"))
                    {
                        obj["@odata.count"] = items.Length;
                    }
                }

                obj["value"] = new JsonArray(items);
                return obj;
            case PayloadShape.Array:
                return new JsonArray(items);
            default:
                return items.Length == 1 ? items[0] : new JsonArray(items);
        }
    }
}
