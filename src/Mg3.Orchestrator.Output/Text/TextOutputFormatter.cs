using System.Globalization;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Abstractions.Output;

namespace Mg3.Orchestrator.Output.Text;

/// <summary>Output testuale deterministico: intestazione per sezione, tabella, avviso di troncamento.</summary>
public sealed class TextOutputFormatter : IOutputFormatter
{
    private readonly Dictionary<TextStyle, ITextRenderer> _renderers;

    public TextOutputFormatter(IEnumerable<ITextRenderer> renderers)
    {
        ArgumentNullException.ThrowIfNull(renderers);
        _renderers = renderers.ToDictionary(r => r.Style);
    }

    public OutputFormat Format => OutputFormat.Text;

    public void Write(TextWriter writer, BatchResult result, OutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(settings);
        var renderer = _renderers[settings.TextStyle];
        var formatter = new CellFormatter(settings);
        for (var i = 0; i < result.Results.Count; i++)
        {
            if (i > 0)
            {
                writer.WriteLine();
            }

            WriteSection(writer, result.Results[i], result.IsBatch, settings, renderer, formatter);
        }
    }

    public void WriteError(TextWriter writer, ErrorInfo errorInfo, OutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(errorInfo);
        writer.WriteLine(ErrorLine(errorInfo));
    }

    public static string ErrorLine(ErrorInfo errorInfo)
    {
        ArgumentNullException.ThrowIfNull(errorInfo);
        return $"ERRORE [{errorInfo.ExitCode.ToString(CultureInfo.InvariantCulture)}]: {errorInfo.Message}";
    }

    private static void WriteSection(TextWriter writer, CommandResult result, bool isBatch, OutputSettings settings, ITextRenderer renderer, CellFormatter formatter)
    {
        var label = isBatch ? result.Id : result.Command;
        var csv = settings.TextStyle == TextStyle.Csv;
        if (result.Payload is null && result.Error is not null)
        {
            if (!csv)
            {
                writer.WriteLine($"## {label}");
            }

            writer.WriteLine(ErrorLine(result.Error));
            return;
        }

        var rows = result.Rows;
        var shown = Math.Min(rows.Count, settings.MaxRows);
        if (!csv)
        {
            writer.WriteLine($"## {label} — {rows.Count.ToString(CultureInfo.InvariantCulture)} {(rows.Count == 1 ? "riga" : "righe")}");
        }

        if (rows.Count > 0)
        {
            renderer.Render(writer, TextTableBuilder.Build(rows.Take(shown), formatter));
        }

        if (csv)
        {
            return;
        }

        var total = result.TotalCount is { } count && count > rows.Count ? count : rows.Count;
        if (shown < total)
        {
            writer.WriteLine($"[troncato: mostrate {shown.ToString(CultureInfo.InvariantCulture)} di {total.ToString(CultureInfo.InvariantCulture)} — usa take/skip o filtri più stretti]");
        }
        else if (result.Truncated)
        {
            writer.WriteLine($"[troncato: mostrate {shown.ToString(CultureInfo.InvariantCulture)} righe; altri dati oltre i limiti (limits.maxItems/maxPages o timeout) — usa filtri più stretti]");
        }

        if (result.Error is not null)
        {
            writer.WriteLine(ErrorLine(result.Error));
        }
    }
}
