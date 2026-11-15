namespace Mg3.Orchestrator.Abstractions.Execution;

/// <summary>Singolo comando della richiesta.</summary>
/// <param name="Command">Es. <c>jobs list</c>; può mancare se <see cref="Query"/> inizia con <c>from</c>.</param>
/// <param name="Args">Argomenti nominati: folder, filter, select, orderby, top, skip, expand, since, key.</param>
public sealed record CommandRequest(string Id, string? Command, IReadOnlyDictionary<string, string> Args, string? Query);

public sealed record BatchOptions
{
    public bool StopOnError { get; init; }
    public int Parallelism { get; init; } = 4;
    public bool PushdownEnabled { get; init; } = true;
    public bool ExplainOnly { get; init; }
}

public sealed record BatchRequest(IReadOnlyList<CommandRequest> Commands, BatchOptions Options, bool IsBatch);
