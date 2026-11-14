using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Abstractions.Output;
using Mg3.Orchestrator.Output.Json;
using Mg3.Orchestrator.Output.Text;
using Mg3.Orchestrator.Tests.Shared;

namespace Mg3.Orchestrator.Output.UnitTests;

internal static class OutputHost
{
    public static readonly TextOutputFormatter Text = new([new TableRenderer(), new ListRenderer(), new MarkdownRenderer(), new CsvRenderer()]);

    public static readonly JsonOutputFormatter Json = new();

    public static CommandResult Jobs(int take = 3, string id = "jobs", long? total = null, bool truncated = false)
    {
        var payload = Fixtures.Object("jobs.json");
        var rows = payload["value"]!.AsArray().Take(take).Select(r => r!.DeepClone()).ToArray();
        var data = new JsonObject { ["@odata.context"] = "ctx", ["value"] = new JsonArray(rows) };
        return new CommandResult
        {
            Id = id,
            Command = "jobs list",
            Payload = data,
            Rows = data["value"]!.AsArray().ToArray(),
            Shape = PayloadShape.ODataCollection,
            TotalCount = total,
            Truncated = truncated,
            Pages = 1,
            DurationMs = 42,
            Pushdown = "$top=3",
            Warnings = ["un avviso"],
        };
    }

    public static string Write(IOutputFormatter formatter, BatchResult result, OutputSettings settings)
    {
        using var writer = new StringWriter { NewLine = "\n" };
        formatter.Write(writer, result, settings);
        return writer.ToString();
    }

    public static BatchResult Batch(params CommandResult[] results) => new(results, IsBatch: true, 0);

    public static BatchResult One(CommandResult result) => new([result], IsBatch: false, 0);

    public static OutputSettings Settings(TextStyle style = TextStyle.Table, int maxRows = 200, int maxCellWidth = 40, string culture = "it-IT", string timeZone = "Europe/Rome") =>
        new() { TextStyle = style, MaxRows = maxRows, MaxCellWidth = maxCellWidth, Culture = culture, TimeZone = timeZone };
}
