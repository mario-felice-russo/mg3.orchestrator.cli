using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Execution;

namespace Mg3.Orchestrator.Output.UnitTests;

/// <summary>Snapshot (Verify) di ogni formato e stile: l'output deve restare identico byte per byte.</summary>
public sealed class SnapshotTests
{
    [Theory]
    [InlineData(TextStyle.Table)]
    [InlineData(TextStyle.List)]
    [InlineData(TextStyle.Markdown)]
    [InlineData(TextStyle.Csv)]
    public Task TextStyles(TextStyle style) =>
        Verify(OutputHost.Write(OutputHost.Text, OutputHost.One(OutputHost.Jobs()), OutputHost.Settings(style))).UseParameters(style);

    [Fact]
    public Task TextTruncationAndErrors()
    {
        var failed = new CommandResult
        {
            Id = "rotto",
            Command = "jobs get 9",
            Status = CommandStatus.Error,
            Error = new ErrorInfo(4, "not_found", "Risorsa non trovata (404)", "rotto"),
        };
        var batch = OutputHost.Batch(OutputHost.Jobs(5, "primo", total: 40), OutputHost.Jobs(2, "limitato", truncated: true), failed);
        return Verify(OutputHost.Write(OutputHost.Text, batch, OutputHost.Settings(maxRows: 2, maxCellWidth: 12)));
    }

    [Fact]
    public Task TextScalarRowsAndEmpty()
    {
        var scalars = new CommandResult
        {
            Id = "stats",
            Command = "stats jobs",
            Payload = new JsonArray(1, "due", null),
            Rows = [JsonValue.Create(1), JsonValue.Create("due"), null],
            Shape = PayloadShape.Array,
        };
        var empty = new CommandResult { Id = "vuoto", Command = "jobs list", Payload = new JsonObject { ["value"] = new JsonArray() }, Rows = [] };
        return Verify(OutputHost.Write(OutputHost.Text, OutputHost.Batch(scalars, empty), OutputHost.Settings(culture: "en-US", timeZone: "UTC")));
    }

    [Fact]
    public Task JsonEnvelope() =>
        Verify(OutputHost.Write(OutputHost.Json, OutputHost.Batch(OutputHost.Jobs(1)), new OutputSettings { Format = OutputFormat.Json, Envelope = true }));

    [Fact]
    public Task JsonBatchWithError()
    {
        var failed = new CommandResult
        {
            Id = "b",
            Command = "jobs get 9",
            Status = CommandStatus.Error,
            Error = new ErrorInfo(4, "not_found", "Risorsa non trovata", "b"),
        };
        return Verify(OutputHost.Write(OutputHost.Json, OutputHost.Batch(OutputHost.Jobs(1, "a"), failed), new OutputSettings { Format = OutputFormat.Json }));
    }
}
