using System.Globalization;
using System.Text;
using Mg3.Orchestrator.Abstractions.Query;

namespace Mg3.Orchestrator.Query.Parsing;

/// <summary>Stampa canonica dell'AST: il risultato, rianalizzato, produce lo stesso AST.</summary>
public sealed class QueryPrinter : IQueryPrinter
{
    public string Print(QueryAst ast)
    {
        ArgumentNullException.ThrowIfNull(ast);
        var parts = new List<string>();
        if (ast.Source is not null)
        {
            parts.Add("from " + PrintSource(ast.Source));
        }

        parts.AddRange(ast.Stages.Select(PrintStage));
        return string.Join(" | ", parts);
    }

    public static string PrintStage(StageNode stage) => stage switch
    {
        WhereStage w => "where " + PrintExpr(w.Condition),
        SelectStage s => "select " + string.Join(", ", s.Projections.Select(p => p.Alias is null ? PrintExpr(p.Expression) : $"{p.Alias} = {PrintExpr(p.Expression)}")),
        OrderByStage o => "orderby " + string.Join(", ", o.Keys.Select(k => PrintExpr(k.Expression) + (k.Descending ? " desc" : " asc"))),
        TakeStage t => "take " + t.Count.ToString(CultureInfo.InvariantCulture),
        SkipStage s => "skip " + s.Count.ToString(CultureInfo.InvariantCulture),
        DistinctStage d => d.Fields.Count == 0 ? "distinct" : "distinct " + string.Join(", ", d.Fields.Select(f => f.DottedName)),
        GroupByStage g => "groupby " + string.Join(", ", g.Fields.Select(f => f.DottedName)),
        AggregateStage a => "aggregate " + string.Join(", ", a.Aggregates.Select(x => $"{x.Name} = {x.Function}({(x.Argument is null ? string.Empty : PrintExpr(x.Argument))})")),
        JoinStage j => $"{(j.Left ? "left join" : "join")} {PrintSource(j.Right)} on {j.LeftKey.DottedName} == {j.RightKey.DottedName}",
        FlattenStage f => "flatten " + f.Field.DottedName,
        CountStage => "count",
        _ => throw new NotSupportedException($"Stadio non stampabile: {stage.GetType().Name}"),
    };

    public static string PrintExpr(Expr expr) => expr switch
    {
        LiteralExpr l => PrintLiteral(l.Value),
        FieldExpr f => f.DottedName,
        BinaryExpr b => $"({PrintExpr(b.Left)} {PrintOperator(b.Operator)} {PrintExpr(b.Right)})",
        UnaryExpr { Operator: UnaryOperator.Not } u => $"(not {PrintExpr(u.Operand)})",
        UnaryExpr u => $"(-{PrintExpr(u.Operand)})",
        IsNullExpr n => $"({PrintExpr(n.Operand)} is {(n.Negated ? "not " : string.Empty)}null)",
        CallExpr c => $"{c.Function}({string.Join(", ", c.Arguments.Select(PrintExpr))})",
        ListExpr l => $"[{string.Join(", ", l.Items.Select(PrintExpr))}]",
        _ => throw new NotSupportedException($"Espressione non stampabile: {expr.GetType().Name}"),
    };

    private static string PrintSource(SourceNode source) =>
        (source.IsReference ? "$" + source.Name : source.Name) + (source.Alias is null ? string.Empty : " as " + source.Alias);

    private static string PrintOperator(BinaryOperator op) => op switch
    {
        BinaryOperator.Or => "or",
        BinaryOperator.And => "and",
        BinaryOperator.Equal => "==",
        BinaryOperator.NotEqual => "!=",
        BinaryOperator.Less => "<",
        BinaryOperator.LessOrEqual => "<=",
        BinaryOperator.Greater => ">",
        BinaryOperator.GreaterOrEqual => ">=",
        BinaryOperator.In => "in",
        BinaryOperator.Contains => "contains",
        BinaryOperator.StartsWith => "startswith",
        BinaryOperator.EndsWith => "endswith",
        BinaryOperator.Matches => "matches",
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        _ => throw new NotSupportedException(op.ToString()),
    };

    private static string PrintLiteral(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        string s => "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'",
        TimeSpan t => PrintDuration(t),
        _ => throw new NotSupportedException($"Letterale non stampabile: {value.GetType().Name}"),
    };

    private static string PrintDuration(TimeSpan t)
    {
        var sb = new StringBuilder();
        var ms = (decimal)t.TotalMilliseconds;
        (decimal Size, string Unit)[] units = [(86_400_000m, "d"), (3_600_000m, "h"), (60_000m, "m"), (1000m, "s")];
        foreach (var (size, unit) in units)
        {
            if (ms % size == 0)
            {
                return sb.Append((ms / size).ToString(CultureInfo.InvariantCulture)).Append(unit).ToString();
            }
        }

        return ms.ToString(CultureInfo.InvariantCulture) + "ms";
    }
}
