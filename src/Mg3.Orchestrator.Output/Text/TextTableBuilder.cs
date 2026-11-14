using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Output;

namespace Mg3.Orchestrator.Output.Text;

/// <summary>Costruisce una tabella: colonne nell'ordine di prima apparizione; righe scalari nella colonna 'value'.</summary>
public static class TextTableBuilder
{
    public static TextTable Build(IEnumerable<JsonNode?> rows, CellFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(formatter);
        var list = rows.ToList();
        var columns = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in list)
        {
            var names = row is JsonObject obj ? obj.Select(kv => kv.Key) : ["value"];
            foreach (var name in names.Where(known.Add))
            {
                columns.Add(name);
            }
        }

        var cells = list.Select(row => (IReadOnlyList<string>)columns
            .Select(column => formatter.Format(row is JsonObject obj ? (obj.TryGetPropertyValue(column, out var value) ? value : null) : row))
            .ToArray()).ToArray();
        return new TextTable(columns, cells);
    }
}
