using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Query;
using Mg3.Orchestrator.Query.Evaluation;

namespace Mg3.Orchestrator.Query.Planning;

/// <summary>
/// Traduce un'espressione DSL in <c>$filter</c> OData quando la semantica è identica a quella in memoria.
/// Regole conservative: solo campi dichiarati filtrabili; confronti d'ordine solo su numeri e date.
/// </summary>
public sealed class ODataFilterTranslator(ExpressionEvaluator evaluator)
{
    public bool TryTranslate(Expr expression, ResourceDescriptor resource, DateTimeOffset now, [NotNullWhen(true)] out string? filter)
    {
        ArgumentNullException.ThrowIfNull(resource);
        filter = Translate(expression, resource, now);
        return filter is not null;
    }

    private string? Translate(Expr expression, ResourceDescriptor resource, DateTimeOffset now)
    {
        switch (expression)
        {
            case BinaryExpr { Operator: BinaryOperator.And or BinaryOperator.Or } logical:
                var left = Translate(logical.Left, resource, now);
                var right = Translate(logical.Right, resource, now);
                return left is null || right is null ? null : $"({left}) {(logical.Operator == BinaryOperator.And ? "and" : "or")} ({right})";
            case UnaryExpr { Operator: UnaryOperator.Not } not:
                var inner = Translate(not.Operand, resource, now);
                return inner is null ? null : $"not ({inner})";
            case IsNullExpr { Operand: FieldExpr field } isNull:
                var nullable = Field(field, resource);
                return nullable is null ? null : $"{Path(nullable)} {(isNull.Negated ? "ne" : "eq")} null";
            case BinaryExpr binary:
                return TranslateComparison(binary, resource, now);
            default:
                return null;
        }
    }

    private string? TranslateComparison(BinaryExpr binary, ResourceDescriptor resource, DateTimeOffset now)
    {
        var (fieldExpr, constantExpr, op) = binary switch
        {
            { Left: FieldExpr f } => (f, binary.Right, binary.Operator),
            { Right: FieldExpr f } when Flip(binary.Operator) is { } flipped => (f, binary.Left, flipped),
            _ => (null, null, binary.Operator),
        };

        if (fieldExpr is null || constantExpr is null || Field(fieldExpr, resource) is not { } field || !TryConstant(constantExpr, now, out var constant))
        {
            return null;
        }

        var path = Path(field);
        switch (op)
        {
            case BinaryOperator.Equal or BinaryOperator.NotEqual:
                var literal = Literal(field, constant);
                return literal is null ? null : $"{path} {(op == BinaryOperator.Equal ? "eq" : "ne")} {literal}";
            case BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual:
                if (field.Type is not (FieldType.WholeNumber or FieldType.Number or FieldType.Timestamp) || constant is null)
                {
                    return null;
                }

                var ordered = Literal(field, constant);
                return ordered is null ? null : $"{path} {OrderOperator(op)} {ordered}";
            case BinaryOperator.Contains or BinaryOperator.StartsWith or BinaryOperator.EndsWith:
                if (field.Type != FieldType.Text || constant is not string text)
                {
                    return null;
                }

                var function = op switch
                {
                    BinaryOperator.Contains => "contains",
                    BinaryOperator.StartsWith => "startswith",
                    _ => "endswith",
                };
                return $"{function}({path},{Quote(text)})";
            case BinaryOperator.In:
                if (constant is not IReadOnlyList<object?> { Count: > 0 } items)
                {
                    return null;
                }

                var terms = items.Select(i => Literal(field, i)).ToArray();
                return terms.Any(t => t is null) ? null : "(" + string.Join(" or ", terms.Select(t => $"{path} eq {t}")) + ")";
            default:
                return null;
        }
    }

    private bool TryConstant(Expr expression, DateTimeOffset now, out object? value)
    {
        value = null;
        if (!IsConstant(expression))
        {
            return false;
        }

        try
        {
            value = evaluator.Evaluate(expression, new EvaluationScope(null, null, now));
            return true;
        }
        catch (Abstractions.Errors.QueryException)
        {
            return false;
        }
    }

    private static bool IsConstant(Expr expression) => expression switch
    {
        LiteralExpr => true,
        ListExpr l => l.Items.All(IsConstant),
        UnaryExpr u => IsConstant(u.Operand),
        BinaryExpr b => IsConstant(b.Left) && IsConstant(b.Right),
        CallExpr c => c.Arguments.All(IsConstant),
        _ => false,
    };

    private static FieldDescriptor? Field(FieldExpr field, ResourceDescriptor resource)
    {
        var descriptor = resource.FindField(field.DottedName);
        return descriptor is { Filterable: true } && descriptor.Type != FieldType.Complex ? descriptor : null;
    }

    /// <summary>Percorso OData con i nomi canonici del catalogo (i nomi OData sono case-sensitive).</summary>
    public static string Path(FieldDescriptor field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Name.Replace('.', '/');
    }

    private static string? Literal(FieldDescriptor field, object? value)
    {
        if (value is null)
        {
            return "null";
        }

        return field.Type switch
        {
            FieldType.Text or FieldType.Enumeration => value is string s ? Quote(s) : null,
            FieldType.Identifier => value is string g && Guid.TryParse(g, out var guid) ? guid.ToString("D") : null,
            FieldType.Boolean => value is bool b ? (b ? "true" : "false") : null,
            FieldType.WholeNumber or FieldType.Number => value is decimal d ? d.ToString(CultureInfo.InvariantCulture) : null,
            FieldType.Timestamp => Values.TryParseDate(value, out var date) ? Values.FormatDate(date) : null,
            _ => null,
        };
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static BinaryOperator? Flip(BinaryOperator op) => op switch
    {
        BinaryOperator.Equal => BinaryOperator.Equal,
        BinaryOperator.NotEqual => BinaryOperator.NotEqual,
        BinaryOperator.Less => BinaryOperator.Greater,
        BinaryOperator.LessOrEqual => BinaryOperator.GreaterOrEqual,
        BinaryOperator.Greater => BinaryOperator.Less,
        BinaryOperator.GreaterOrEqual => BinaryOperator.LessOrEqual,
        _ => null,
    };

    private static string OrderOperator(BinaryOperator op) => op switch
    {
        BinaryOperator.Less => "lt",
        BinaryOperator.LessOrEqual => "le",
        BinaryOperator.Greater => "gt",
        _ => "ge",
    };
}
