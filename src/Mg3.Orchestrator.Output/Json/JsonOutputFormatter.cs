using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Abstractions.Output;

namespace Mg3.Orchestrator.Output.Json;

/// <summary>Restituisce il JSON originale di Orchestrator, compatto e con escaping minimo.</summary>
public sealed class JsonOutputFormatter : IOutputFormatter
{
    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public OutputFormat Format => OutputFormat.Json;

    public void Write(TextWriter writer, BatchResult result, OutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(settings);
        JsonNode? output;
        if (result.IsBatch)
        {
            var obj = new JsonObject();
            foreach (var command in result.Results)
            {
                obj[command.Id] = Render(command, settings);
            }

            output = obj;
        }
        else
        {
            output = result.Results.Count == 1 ? Render(result.Results[0], settings) : new JsonArray(result.Results.Select(r => Render(r, settings)).ToArray());
        }

        writer.WriteLine(output?.ToJsonString(WriteOptions) ?? "null");
    }

    public void WriteError(TextWriter writer, ErrorInfo errorInfo, OutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(errorInfo);
        writer.WriteLine(ErrorObject(errorInfo).ToJsonString(WriteOptions));
    }

    public static JsonObject ErrorObject(ErrorInfo errorInfo)
    {
        ArgumentNullException.ThrowIfNull(errorInfo);
        return new JsonObject { ["error"] = ErrorBody(errorInfo) };
    }

    private static JsonObject ErrorBody(ErrorInfo errorInfo) => new()
    {
        ["code"] = errorInfo.Code,
        ["exitCode"] = errorInfo.ExitCode,
        ["message"] = errorInfo.Message,
        ["commandId"] = errorInfo.CommandId,
    };

    private static JsonNode? Render(CommandResult result, OutputSettings settings)
    {
        if (settings.Envelope)
        {
            return new JsonObject
            {
                ["command"] = result.Command,
                ["status"] = result.Status.ToString().ToLowerInvariant(),
                ["durationMs"] = result.DurationMs,
                ["pages"] = result.Pages,
                ["truncated"] = result.Truncated,
                ["pushdown"] = result.Pushdown,
                ["warnings"] = new JsonArray(result.Warnings.Select(w => (JsonNode?)JsonValue.Create(w)).ToArray()),
                ["error"] = result.Error is null ? null : ErrorBody(result.Error),
                ["data"] = result.Payload?.DeepClone(),
            };
        }

        if (result.Payload is null && result.Error is not null)
        {
            return ErrorObject(result.Error);
        }

        return result.Payload?.DeepClone();
    }
}
