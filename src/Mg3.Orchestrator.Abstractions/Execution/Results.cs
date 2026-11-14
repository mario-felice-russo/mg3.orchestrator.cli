using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Errors;

namespace Mg3.Orchestrator.Abstractions.Execution;

public enum CommandStatus
{
    Ok,
    Error,
    Timeout,
    Skipped,
}

public sealed record ErrorInfo(int ExitCode, string Code, string Message, string? CommandId);

public sealed record CommandResult
{
    public required string Id { get; init; }
    public required string Command { get; init; }
    public CommandStatus Status { get; init; } = CommandStatus.Ok;
    public long DurationMs { get; init; }
    public int Pages { get; init; }
    public bool Truncated { get; init; }
    public string? Pushdown { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public ErrorInfo? Error { get; init; }
    public JsonNode? Payload { get; init; }
    public PayloadShape Shape { get; init; } = PayloadShape.ODataCollection;
    public IReadOnlyList<JsonNode?> Rows { get; init; } = [];

    /// <summary>Numero totale di righe disponibili, se noto; serve a segnalare il troncamento.</summary>
    public long? TotalCount { get; init; }

    public bool IsSuccess => Status == CommandStatus.Ok;

    public static CommandResult Failed(string id, string command, Mg3Exception error, long durationMs) => new()
    {
        Id = id,
        Command = command,
        Status = error.ExitCode == ExitCodes.Timeout ? CommandStatus.Timeout : CommandStatus.Error,
        DurationMs = durationMs,
        Error = new ErrorInfo(error.ExitCode, error.Code, error.Message, id),
    };
}

public sealed record BatchResult(IReadOnlyList<CommandResult> Results, bool IsBatch, int ExitCode);
