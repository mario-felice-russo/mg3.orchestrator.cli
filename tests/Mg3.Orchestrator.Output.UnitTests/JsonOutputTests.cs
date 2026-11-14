using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Output.Text;
using Mg3.Orchestrator.Tests.Shared;

namespace Mg3.Orchestrator.Output.UnitTests;

public sealed class JsonOutputTests
{
    [Fact]
    public void RawPayloadIsByteIdenticalToFixture()
    {
        var text = Fixtures.Text("jobs.json");
        var payload = JsonNode.Parse(text)!;
        var result = new CommandResult { Id = "cmd", Command = "jobs list", Payload = payload, Rows = payload["value"]!.AsArray().ToArray() };
        var output = OutputHost.Write(OutputHost.Json, OutputHost.One(result), new OutputSettings { Format = OutputFormat.Json });
        output.ShouldBe(text + "\n");
    }

    [Fact]
    public void SingleErrorIsWrittenAsErrorObject()
    {
        var result = new CommandResult { Id = "cmd", Command = "x", Status = CommandStatus.Error, Error = new ErrorInfo(5, "query_error", "msg", "cmd") };
        OutputHost.Write(OutputHost.Json, OutputHost.One(result), new OutputSettings()).ShouldBe("{\"error\":{\"code\":\"query_error\",\"exitCode\":5,\"message\":\"msg\",\"commandId\":\"cmd\"}}\n");
    }

    [Fact]
    public void WriteErrorProducesValidOutputInBothFormats()
    {
        var info = new ErrorInfo(2, "invalid_usage", "Configurazione non valida", null);
        using var json = new StringWriter();
        OutputHost.Json.WriteError(json, info, new OutputSettings());
        JsonNode.Parse(json.ToString())!["error"]!["exitCode"]!.GetValue<int>().ShouldBe(2);
        using var text = new StringWriter();
        OutputHost.Text.WriteError(text, info, new OutputSettings());
        text.ToString().TrimEnd().ShouldBe("ERRORE [2]: Configurazione non valida");
    }

    [Fact]
    public void MultipleNonBatchResultsAreWrittenAsArray()
    {
        var output = OutputHost.Write(OutputHost.Json, new BatchResult([OutputHost.Jobs(1), OutputHost.Jobs(1)], IsBatch: false, 0), new OutputSettings());
        JsonNode.Parse(output).ShouldBeOfType<JsonArray>().Count.ShouldBe(2);
    }

    [Fact]
    public void CellFormatterConvertsDatesNumbersAndTruncates()
    {
        var formatter = new CellFormatter(OutputHost.Settings(maxCellWidth: 6, culture: "it-IT", timeZone: "Europe/Rome"));
        formatter.Format(JsonValue.Create(1234.5m)).ShouldBe("1234,5");
        formatter.Format(JsonValue.Create("abcdefghi")).ShouldBe("abcde…");
        formatter.Format(JsonValue.Create(true)).ShouldBe("true");
        formatter.Format(null).ShouldBe(string.Empty);
        formatter.Format(JsonValue.Create("a\nb")).ShouldBe("a b");
        new CellFormatter(OutputHost.Settings(maxCellWidth: 40)).Format(JsonValue.Create("2026-11-01T08:37:00.000Z")).ShouldBe("2026-11-01 09:37:00");
        var csv = new CellFormatter(OutputHost.Settings(TextStyle.Csv, maxCellWidth: 4));
        csv.Format(JsonValue.Create(1234.5m)).ShouldBe("1234.5");
        csv.Format(JsonValue.Create("lunghissimo")).ShouldBe("lunghissimo");
    }

    [Fact]
    public void CsvQuotesSpecialCharacters()
    {
        var result = new CommandResult
        {
            Id = "c",
            Command = "c",
            Payload = new JsonArray(),
            Shape = PayloadShape.Array,
            Rows = [new JsonObject { ["a"] = "x,y", ["b"] = "di \"lui\"" }],
        };
        OutputHost.Write(OutputHost.Text, OutputHost.One(result), OutputHost.Settings(TextStyle.Csv)).ShouldBe("a,b\n\"x,y\",\"di \"\"lui\"\"\"\n");
    }
}
