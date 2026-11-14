using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Execution;

namespace Mg3.Orchestrator.Abstractions.Output;

public interface IOutputFormatter
{
    OutputFormat Format { get; }

    void Write(TextWriter writer, BatchResult result, OutputSettings settings);

    void WriteError(TextWriter writer, ErrorInfo errorInfo, OutputSettings settings);
}

/// <summary>Renderer tabellare per uno stile di testo.</summary>
public interface ITextRenderer
{
    TextStyle Style { get; }

    void Render(TextWriter writer, TextTable table);
}

/// <summary>Tabella già formattata come stringhe, pronta per il rendering.</summary>
public sealed record TextTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);
