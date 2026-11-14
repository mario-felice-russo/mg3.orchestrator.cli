namespace Mg3.Orchestrator.Abstractions.Http;

/// <summary>Opzioni di query OData inviate al server.</summary>
public sealed record ODataQueryOptions
{
    public string? Filter { get; init; }
    public string? Select { get; init; }
    public string? OrderBy { get; init; }
    public int? Top { get; init; }
    public int? Skip { get; init; }
    public string? Expand { get; init; }
    public bool Count { get; init; }

    public static ODataQueryOptions Empty { get; } = new();

    public bool IsEmpty => Filter is null && Select is null && OrderBy is null && Top is null && Skip is null && Expand is null;
}
