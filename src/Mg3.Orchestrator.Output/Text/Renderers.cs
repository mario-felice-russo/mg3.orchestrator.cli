using System.Text;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Output;

namespace Mg3.Orchestrator.Output.Text;

public sealed class TableRenderer : ITextRenderer
{
    public TextStyle Style => TextStyle.Table;

    public void Render(TextWriter writer, TextTable table)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(table);
        var widths = table.Columns.Select((c, i) => Math.Max(c.Length, table.Rows.Select(r => r[i].Length).DefaultIfEmpty(0).Max())).ToArray();
        WriteRow(writer, table.Columns, widths);
        writer.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in table.Rows)
        {
            WriteRow(writer, row, widths);
        }
    }

    private static void WriteRow(TextWriter writer, IReadOnlyList<string> cells, int[] widths) =>
        writer.WriteLine(string.Join("  ", cells.Select((c, i) => c.PadRight(widths[i]))).TrimEnd());
}

public sealed class ListRenderer : ITextRenderer
{
    public TextStyle Style => TextStyle.List;

    public void Render(TextWriter writer, TextTable table)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(table);
        var width = table.Columns.Select(c => c.Length).DefaultIfEmpty(0).Max();
        for (var r = 0; r < table.Rows.Count; r++)
        {
            if (r > 0)
            {
                writer.WriteLine();
            }

            for (var i = 0; i < table.Columns.Count; i++)
            {
                writer.WriteLine($"{(table.Columns[i] + ":").PadRight(width + 1)} {table.Rows[r][i]}".TrimEnd());
            }
        }
    }
}

public sealed class MarkdownRenderer : ITextRenderer
{
    public TextStyle Style => TextStyle.Markdown;

    public void Render(TextWriter writer, TextTable table)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(table);
        writer.WriteLine("| " + string.Join(" | ", table.Columns.Select(Escape)) + " |");
        writer.WriteLine("|" + string.Concat(table.Columns.Select(_ => " --- |")));
        foreach (var row in table.Rows)
        {
            writer.WriteLine("| " + string.Join(" | ", row.Select(Escape)) + " |");
        }
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}

public sealed class CsvRenderer : ITextRenderer
{
    public TextStyle Style => TextStyle.Csv;

    public void Render(TextWriter writer, TextTable table)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(table);
        writer.WriteLine(string.Join(',', table.Columns.Select(Quote)));
        foreach (var row in table.Rows)
        {
            writer.WriteLine(string.Join(',', row.Select(Quote)));
        }
    }

    private static string Quote(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return value;
        }

        return new StringBuilder("\"").Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"').ToString();
    }
}
