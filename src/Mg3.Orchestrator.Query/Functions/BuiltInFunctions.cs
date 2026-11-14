using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Query.Evaluation;

namespace Mg3.Orchestrator.Query.Functions;

public sealed class NowFunction : IQueryFunction
{
    public string Name => "now";

    public (int Min, int Max) Arity => (0, 0);

    public object? Invoke(IReadOnlyList<object?> arguments, FunctionContext context) => context.Now;
}

public sealed class TodayFunction : IQueryFunction
{
    public string Name => "today";

    public (int Min, int Max) Arity => (0, 0);

    public object? Invoke(IReadOnlyList<object?> arguments, FunctionContext context)
    {
        var utc = context.Now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }
}

public sealed class DateFunction : IQueryFunction
{
    public string Name => "date";

    public (int Min, int Max) Arity => (1, 1);

    public object? Invoke(IReadOnlyList<object?> arguments, FunctionContext context)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments[0] is null)
        {
            return null;
        }

        return Values.TryParseDate(arguments[0], out var date)
            ? date
            : throw new QueryException($"date(): valore non riconosciuto come data ISO 8601: '{Values.Describe(arguments[0])}'");
    }
}

public sealed class LowerFunction : IQueryFunction
{
    public string Name => "lower";

    public (int Min, int Max) Arity => (1, 1);

    public object? Invoke(IReadOnlyList<object?> arguments, FunctionContext context) =>
        arguments[0] is string s ? s.ToLowerInvariant() : arguments[0];
}

public sealed class UpperFunction : IQueryFunction
{
    public string Name => "upper";

    public (int Min, int Max) Arity => (1, 1);

    public object? Invoke(IReadOnlyList<object?> arguments, FunctionContext context) =>
        arguments[0] is string s ? s.ToUpperInvariant() : arguments[0];
}

public sealed class LenFunction : IQueryFunction
{
    public string Name => "len";

    public (int Min, int Max) Arity => (1, 1);

    public object? Invoke(IReadOnlyList<object?> arguments, FunctionContext context) => arguments[0] switch
    {
        string s => (decimal)s.Length,
        JsonArray a => (decimal)a.Count,
        IReadOnlyList<object?> l => (decimal)l.Count,
        _ => null,
    };
}

public sealed class CoalesceFunction : IQueryFunction
{
    public string Name => "coalesce";

    public (int Min, int Max) Arity => (1, int.MaxValue);

    public object? Invoke(IReadOnlyList<object?> arguments, FunctionContext context) => arguments.FirstOrDefault(a => a is not null);
}

/// <summary>Estrae una parte di una data (anno, mese, giorno, ora) in UTC.</summary>
public abstract class DatePartFunction(string name, Func<DateTimeOffset, int> part) : IQueryFunction
{
    public string Name => name;

    public (int Min, int Max) Arity => (1, 1);

    public object? Invoke(IReadOnlyList<object?> arguments, FunctionContext context) =>
        Values.TryParseDate(arguments[0], out var date) ? (decimal)part(date.ToUniversalTime()) : null;
}

public sealed class YearFunction() : DatePartFunction("year", d => d.Year);

public sealed class MonthFunction() : DatePartFunction("month", d => d.Month);

public sealed class DayFunction() : DatePartFunction("day", d => d.Day);

public sealed class HourFunction() : DatePartFunction("hour", d => d.Hour);
