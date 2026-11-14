using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Query;
using Mg3.Orchestrator.Query.Functions;

namespace Mg3.Orchestrator.Query.Evaluation;

/// <summary>Valuta le espressioni della DSL su una riga JSON. Non esegue codice dinamico.</summary>
public sealed class ExpressionEvaluator
{
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Dictionary<string, IQueryFunction> _functions;
    private readonly ConcurrentDictionary<string, Regex> _regexCache = new(StringComparer.Ordinal);

    public ExpressionEvaluator(IEnumerable<IQueryFunction> functions)
    {
        ArgumentNullException.ThrowIfNull(functions);
        _functions = functions.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasFunction(string name) => _functions.ContainsKey(name);

    public object? Evaluate(Expr expression, EvaluationScope scope)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(scope);
        return expression switch
        {
            LiteralExpr l => l.Value,
            FieldExpr f => ResolveField(f, scope),
            ListExpr l => l.Items.Select(i => Evaluate(i, scope)).ToArray(),
            UnaryExpr u => EvaluateUnary(u, scope),
            IsNullExpr n => (Evaluate(n.Operand, scope) is null) != n.Negated,
            CallExpr c => EvaluateCall(c, scope),
            BinaryExpr b => EvaluateBinary(b, scope),
            _ => throw new QueryException($"Espressione non supportata: {expression.GetType().Name}"),
        };
    }

    /// <summary>Nome di output di un campo: rimuove l'alias della sorgente.</summary>
    public static string OutputName(FieldExpr field, string? sourceAlias)
    {
        ArgumentNullException.ThrowIfNull(field);
        var path = field.Path;
        if (sourceAlias is not null && path.Count > 1 && string.Equals(path[0], sourceAlias, StringComparison.OrdinalIgnoreCase))
        {
            path = path.Skip(1).ToArray();
        }

        return string.Join('.', path);
    }

    public static JsonNode? FindProperty(JsonObject obj, string name)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj.TryGetPropertyValue(name, out var exact))
        {
            return exact;
        }

        foreach (var (key, value) in obj)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    public static bool HasProperty(JsonObject obj, string name) =>
        obj.ContainsKey(name) || obj.Any(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Risolve il nodo JSON di un campo; <paramref name="found"/> indica se la proprietà esiste.</summary>
    public static JsonNode? ResolveNode(FieldExpr field, JsonNode? row, string? sourceAlias, out bool found)
    {
        ArgumentNullException.ThrowIfNull(field);
        var path = field.Path;
        var start = 0;
        if (sourceAlias is not null && path.Count > 1 && string.Equals(path[0], sourceAlias, StringComparison.OrdinalIgnoreCase)
            && row is JsonObject root && !HasProperty(root, path[0]))
        {
            start = 1;
        }

        if (path.Count - start > 1 && row is JsonObject flat)
        {
            var dotted = string.Join('.', path.Skip(start));
            if (HasProperty(flat, dotted))
            {
                found = true;
                return FindProperty(flat, dotted);
            }
        }

        var current = row;
        for (var i = start; i < path.Count; i++)
        {
            if (current is not JsonObject obj || !HasProperty(obj, path[i]))
            {
                found = false;
                return null;
            }

            current = FindProperty(obj, path[i]);
        }

        found = true;
        return current;
    }

    private static object? ResolveField(FieldExpr field, EvaluationScope scope)
    {
        var node = ResolveNode(field, scope.Row, scope.SourceAlias, out var found);
        scope.Tracker?.Record(OutputName(field, scope.SourceAlias), found);
        return Values.FromJson(node);
    }

    private object? EvaluateUnary(UnaryExpr unary, EvaluationScope scope)
    {
        var value = Evaluate(unary.Operand, scope);
        return unary.Operator switch
        {
            UnaryOperator.Not => value is null ? null : !Values.IsTruthy(value),
            UnaryOperator.Negate => value switch
            {
                null => null,
                TimeSpan t => -t,
                _ when Values.TryNumber(value, out var n) => -n,
                _ => throw new QueryException($"Impossibile negare il valore '{Values.Describe(value)}'"),
            },
            _ => throw new QueryException($"Operatore unario non supportato: {unary.Operator}"),
        };
    }

    private object? EvaluateCall(CallExpr call, EvaluationScope scope)
    {
        if (!_functions.TryGetValue(call.Function, out var function))
        {
            throw new QueryException($"Funzione sconosciuta '{call.Function}': disponibili {string.Join(", ", _functions.Keys.Order(StringComparer.Ordinal))}");
        }

        var (min, max) = function.Arity;
        if (call.Arguments.Count < min || call.Arguments.Count > max)
        {
            throw new QueryException($"La funzione '{call.Function}' accetta da {min} a {(max == int.MaxValue ? "N" : max)} argomenti, ricevuti {call.Arguments.Count}");
        }

        var arguments = call.Arguments.Select(a => Evaluate(a, scope)).ToArray();
        return function.Invoke(arguments, new FunctionContext(scope.Now));
    }

    private object? EvaluateBinary(BinaryExpr binary, EvaluationScope scope)
    {
        if (binary.Operator is BinaryOperator.And or BinaryOperator.Or)
        {
            return EvaluateLogical(binary, scope);
        }

        var left = Evaluate(binary.Left, scope);
        var right = Evaluate(binary.Right, scope);
        return binary.Operator switch
        {
            BinaryOperator.Equal => Values.Equals(left, right),
            BinaryOperator.NotEqual => !Values.Equals(left, right),
            BinaryOperator.Less => left is not null && right is not null && Values.Compare(left, right) < 0,
            BinaryOperator.LessOrEqual => left is not null && right is not null && Values.Compare(left, right) <= 0,
            BinaryOperator.Greater => left is not null && right is not null && Values.Compare(left, right) > 0,
            BinaryOperator.GreaterOrEqual => left is not null && right is not null && Values.Compare(left, right) >= 0,
            BinaryOperator.In => EvaluateIn(left, right),
            BinaryOperator.Contains => EvaluateContains(left, right),
            BinaryOperator.StartsWith => left is string ls && right is string rs && ls.StartsWith(rs, StringComparison.OrdinalIgnoreCase),
            BinaryOperator.EndsWith => left is string le && right is string re && le.EndsWith(re, StringComparison.OrdinalIgnoreCase),
            BinaryOperator.Matches => EvaluateMatches(left, right),
            _ => Arithmetic.Apply(binary.Operator, left, right),
        };
    }

    private object? EvaluateLogical(BinaryExpr binary, EvaluationScope scope)
    {
        var left = Values.IsTruthy(Evaluate(binary.Left, scope));
        if (binary.Operator == BinaryOperator.And)
        {
            return left && Values.IsTruthy(Evaluate(binary.Right, scope));
        }

        return left || Values.IsTruthy(Evaluate(binary.Right, scope));
    }

    private static bool EvaluateIn(object? left, object? right) => right switch
    {
        IReadOnlyList<object?> list => list.Any(item => Values.Equals(left, item)),
        JsonArray array => array.Any(item => Values.Equals(left, Values.FromJson(item))),
        _ => throw new QueryException("L'operatore 'in' richiede una lista a destra, es. State in ('Faulted', 'Stopped')"),
    };

    private static bool EvaluateContains(object? left, object? right) => left switch
    {
        string s when right is not null => s.Contains(Values.Describe(right), StringComparison.OrdinalIgnoreCase),
        JsonArray array => array.Any(item => Values.Equals(Values.FromJson(item), right)),
        IReadOnlyList<object?> list => list.Any(item => Values.Equals(item, right)),
        _ => false,
    };

    private bool EvaluateMatches(object? left, object? right)
    {
        if (left is not string input)
        {
            return false;
        }

        if (right is not string pattern)
        {
            throw new QueryException("L'operatore 'matches' richiede un'espressione regolare di tipo stringa");
        }

        var regex = _regexCache.GetOrAdd(pattern, static p => CreateRegex(p));
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new QueryException($"L'espressione regolare '{pattern}' ha superato il timeout di {RegexTimeout.TotalMilliseconds} ms");
        }
    }

    private static Regex CreateRegex(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new QueryException($"Espressione regolare non valida '{pattern}': {ex.Message}");
        }
    }
}
