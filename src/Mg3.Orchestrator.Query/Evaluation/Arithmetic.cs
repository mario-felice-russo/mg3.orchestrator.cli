using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Query;

namespace Mg3.Orchestrator.Query.Evaluation;

/// <summary>Aritmetica su numeri, date e durate.</summary>
public static class Arithmetic
{
    public static object? Apply(BinaryOperator op, object? left, object? right)
    {
        if (left is null || right is null)
        {
            return null;
        }

        if (left is TimeSpan || right is TimeSpan || left is DateTimeOffset || right is DateTimeOffset)
        {
            return ApplyTemporal(op, left, right);
        }

        if (Values.TryNumber(left, out var a) && Values.TryNumber(right, out var b) && (left is decimal || right is decimal))
        {
            return op switch
            {
                BinaryOperator.Add => a + b,
                BinaryOperator.Subtract => a - b,
                BinaryOperator.Multiply => a * b,
                BinaryOperator.Divide => b == 0 ? null : a / b,
                _ => throw Unsupported(op, left, right),
            };
        }

        if (op == BinaryOperator.Add && left is string ls && right is string rs)
        {
            return ls + rs;
        }

        if (Values.TryParseDate(left, out _) && (right is TimeSpan || Values.TryParseDate(right, out _)))
        {
            return ApplyTemporal(op, left, right);
        }

        throw Unsupported(op, left, right);
    }

    private static object? ApplyTemporal(BinaryOperator op, object left, object right)
    {
        var leftIsDate = Values.TryParseDate(left, out var leftDate);
        var rightIsDate = Values.TryParseDate(right, out var rightDate);
        return (op, left, right) switch
        {
            (BinaryOperator.Add, _, TimeSpan t) when leftIsDate => leftDate + t,
            (BinaryOperator.Add, TimeSpan t, _) when rightIsDate => rightDate + t,
            (BinaryOperator.Subtract, _, TimeSpan t) when leftIsDate => leftDate - t,
            (BinaryOperator.Subtract, _, _) when leftIsDate && rightIsDate => leftDate - rightDate,
            (BinaryOperator.Add, TimeSpan a, TimeSpan b) => a + b,
            (BinaryOperator.Subtract, TimeSpan a, TimeSpan b) => a - b,
            _ => throw Unsupported(op, left, right),
        };
    }

    private static QueryException Unsupported(BinaryOperator op, object left, object right) =>
        new($"Operazione '{op}' non applicabile a '{Values.Describe(left)}' e '{Values.Describe(right)}'");
}
