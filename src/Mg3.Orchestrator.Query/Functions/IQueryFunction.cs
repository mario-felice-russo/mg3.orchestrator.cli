namespace Mg3.Orchestrator.Query.Functions;

public sealed record FunctionContext(DateTimeOffset Now);

/// <summary>Funzione della DSL (es. <c>now()</c>, <c>lower(x)</c>).</summary>
public interface IQueryFunction
{
    string Name { get; }

    /// <summary>Numero di argomenti ammessi.</summary>
    (int Min, int Max) Arity { get; }

    object? Invoke(IReadOnlyList<object?> arguments, FunctionContext context);
}
