using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Query;
using Mg3.Orchestrator.Query.Evaluation;
using Mg3.Orchestrator.Query.Parsing;

namespace Mg3.Orchestrator.Query.Execution;

public sealed class WhereExecutor : StageExecutor<WhereStage>
{
    protected override IReadOnlyList<JsonNode?> Execute(WhereStage stage, IReadOnlyList<JsonNode?> rows, StageExecutionContext context) =>
        rows.Where(row => Values.IsTruthy(context.Evaluator.Evaluate(stage.Condition, context.Scope(row)))).ToList();
}

public sealed class SelectExecutor : StageExecutor<SelectStage>
{
    protected override IReadOnlyList<JsonNode?> Execute(SelectStage stage, IReadOnlyList<JsonNode?> rows, StageExecutionContext context)
    {
        var names = stage.Projections.Select(p => ProjectionName(p, context.SourceAlias)).ToArray();
        var result = new List<JsonNode?>(rows.Count);
        foreach (var row in rows)
        {
            var obj = new JsonObject();
            for (var i = 0; i < stage.Projections.Count; i++)
            {
                obj[names[i]] = Project(stage.Projections[i].Expression, row, context);
            }

            result.Add(obj);
        }

        context.SourceAlias = null;
        return result;
    }

    internal static string ProjectionName(Projection projection, string? sourceAlias) =>
        projection.Alias ?? (projection.Expression is FieldExpr f ? ExpressionEvaluator.OutputName(f, sourceAlias) : QueryPrinter.PrintExpr(projection.Expression));

    private static JsonNode? Project(Expr expression, JsonNode? row, StageExecutionContext context)
    {
        if (expression is FieldExpr field)
        {
            var node = ExpressionEvaluator.ResolveNode(field, row, context.SourceAlias, out var found);
            context.Tracker.Record(ExpressionEvaluator.OutputName(field, context.SourceAlias), found);
            return node?.DeepClone();
        }

        return Values.ToJson(context.Evaluator.Evaluate(expression, context.Scope(row)));
    }
}

public sealed class OrderByExecutor : StageExecutor<OrderByStage>
{
    protected override IReadOnlyList<JsonNode?> Execute(OrderByStage stage, IReadOnlyList<JsonNode?> rows, StageExecutionContext context)
    {
        var keyed = rows.Select(row => (Row: row, Keys: stage.Keys.Select(k => context.Evaluator.Evaluate(k.Expression, context.Scope(row))).ToArray())).ToList();
        IOrderedEnumerable<(JsonNode? Row, object?[] Keys)>? ordered = null;
        for (var i = 0; i < stage.Keys.Count; i++)
        {
            var index = i;
            var comparer = Comparer<object?>.Create(Values.Compare);
            ordered = (ordered, stage.Keys[i].Descending) switch
            {
                (null, false) => keyed.OrderBy(x => x.Keys[index], comparer),
                (null, true) => keyed.OrderByDescending(x => x.Keys[index], comparer),
                (_, false) => ordered.ThenBy(x => x.Keys[index], comparer),
                (_, true) => ordered.ThenByDescending(x => x.Keys[index], comparer),
            };
        }

        return ordered is null ? rows : ordered.Select(x => x.Row).ToList();
    }
}

public sealed class TakeExecutor : StageExecutor<TakeStage>
{
    protected override IReadOnlyList<JsonNode?> Execute(TakeStage stage, IReadOnlyList<JsonNode?> rows, StageExecutionContext context) =>
        rows.Take(stage.Count).ToList();
}

public sealed class SkipExecutor : StageExecutor<SkipStage>
{
    protected override IReadOnlyList<JsonNode?> Execute(SkipStage stage, IReadOnlyList<JsonNode?> rows, StageExecutionContext context) =>
        rows.Skip(stage.Count).ToList();
}

public sealed class DistinctExecutor : StageExecutor<DistinctStage>
{
    protected override IReadOnlyList<JsonNode?> Execute(DistinctStage stage, IReadOnlyList<JsonNode?> rows, StageExecutionContext context)
    {
        IReadOnlyList<JsonNode?> source = rows;
        if (stage.Fields.Count > 0)
        {
            var projection = new SelectStage(stage.Fields.Select(f => new Projection(f, null)).ToArray(), stage.Position);
            source = new SelectExecutor().Execute(projection, rows, context);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return source.Where(row => seen.Add(Values.CanonicalKey(Values.FromJson(row) ?? row))).ToList();
    }
}

public sealed class GroupAggregateExecutor : StageExecutor<GroupAggregateStage>
{
    protected override IReadOnlyList<JsonNode?> Execute(GroupAggregateStage stage, IReadOnlyList<JsonNode?> rows, StageExecutionContext context)
    {
        var groups = new Dictionary<string, (object?[] Keys, List<JsonNode?> Rows)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var row in rows)
        {
            var keys = stage.Keys.Select(k => context.Evaluator.Evaluate(k, context.Scope(row))).ToArray();
            var canonical = string.Join("|", keys.Select(Values.CanonicalKey));
            if (!groups.TryGetValue(canonical, out var group))
            {
                group = (keys, []);
                groups[canonical] = group;
                order.Add(canonical);
            }

            group.Rows.Add(row);
        }

        if (stage.Keys.Count == 0 && groups.Count == 0)
        {
            groups[string.Empty] = ([], []);
            order.Add(string.Empty);
        }

        var result = new List<JsonNode?>(order.Count);
        foreach (var canonical in order)
        {
            var (keys, groupRows) = groups[canonical];
            var obj = new JsonObject();
            for (var i = 0; i < stage.Keys.Count; i++)
            {
                obj[ExpressionEvaluator.OutputName(stage.Keys[i], context.SourceAlias)] = Values.ToJson(keys[i]);
            }

            foreach (var aggregate in stage.Aggregates)
            {
                obj[aggregate.Name] = Values.ToJson(Aggregate(aggregate, groupRows, context));
            }

            result.Add(obj);
        }

        context.SourceAlias = null;
        return result;
    }

    private static object? Aggregate(AggregateDefinition aggregate, List<JsonNode?> rows, StageExecutionContext context)
    {
        if (aggregate.Argument is null)
        {
            return (decimal)rows.Count;
        }

        var values = rows.Select(r => context.Evaluator.Evaluate(aggregate.Argument, context.Scope(r))).ToList();
        var present = values.Where(v => v is not null).ToList();
        var numbers = present.Select(v => Values.TryNumber(v, out var n) ? n : (decimal?)null).Where(n => n.HasValue).Select(n => n!.Value).ToList();
        return aggregate.Function switch
        {
            "count" => (decimal)present.Count,
            "countif" => (decimal)values.Count(Values.IsTruthy),
            "sum" => numbers.Sum(),
            "avg" => numbers.Count == 0 ? null : Math.Round(numbers.Average(), 6),
            "min" => present.Count == 0 ? null : present.Min(Comparer<object?>.Create(Values.Compare)),
            "max" => present.Count == 0 ? null : present.Max(Comparer<object?>.Create(Values.Compare)),
            _ => throw new InvalidOperationException($"Aggregato non gestito: {aggregate.Function}"),
        };
    }
}

public sealed class JoinExecutor : StageExecutor<JoinStage>
{
    protected override IReadOnlyList<JsonNode?> Execute(JoinStage stage, IReadOnlyList<JsonNode?> rows, StageExecutionContext context)
    {
        if (!context.NamedSources.TryGetValue(stage.Right.SourceKey, out var rightRows))
        {
            throw new Abstractions.Errors.QueryException($"Sorgente del join non disponibile: '{stage.Right.SourceKey}'", stage.Position);
        }

        var index = new Dictionary<string, List<JsonNode?>>(StringComparer.Ordinal);
        foreach (var right in rightRows)
        {
            var key = Values.FromJson(ExpressionEvaluator.ResolveNode(stage.RightKey, right, stage.Alias, out _));
            if (key is null)
            {
                continue;
            }

            var canonical = Values.CanonicalKey(key);
            if (!index.TryGetValue(canonical, out var bucket))
            {
                bucket = [];
                index[canonical] = bucket;
            }

            bucket.Add(right);
        }

        var result = new List<JsonNode?>();
        foreach (var left in rows)
        {
            if (left is not JsonObject leftObject)
            {
                continue;
            }

            var key = context.Evaluator.Evaluate(stage.LeftKey, context.Scope(left));
            var matches = key is null ? null : index.GetValueOrDefault(Values.CanonicalKey(key));
            if (matches is null || matches.Count == 0)
            {
                if (stage.Left && !Add(result, Combine(leftObject, stage.Alias, null), context))
                {
                    break;
                }

                continue;
            }

            if (matches.Any(match => !Add(result, Combine(leftObject, stage.Alias, match), context)))
            {
                break;
            }
        }

        return result;
    }

    private static JsonObject Combine(JsonObject left, string alias, JsonNode? right)
    {
        var combined = (JsonObject)left.DeepClone();
        combined[alias] = right?.DeepClone();
        return combined;
    }

    private static bool Add(List<JsonNode?> result, JsonObject row, StageExecutionContext context)
    {
        if (result.Count >= context.MaxJoinRows)
        {
            context.Truncated = true;
            context.Warnings.Add($"Join troncato a {context.MaxJoinRows} righe (limits.maxJoinRows).");
            return false;
        }

        result.Add(row);
        return true;
    }
}

public sealed class FlattenExecutor : StageExecutor<FlattenStage>
{
    protected override IReadOnlyList<JsonNode?> Execute(FlattenStage stage, IReadOnlyList<JsonNode?> rows, StageExecutionContext context)
    {
        var result = new List<JsonNode?>();
        var name = ExpressionEvaluator.OutputName(stage.Field, context.SourceAlias);
        foreach (var row in rows)
        {
            var node = ExpressionEvaluator.ResolveNode(stage.Field, row, context.SourceAlias, out var found);
            context.Tracker.Record(name, found);
            if (row is not JsonObject obj || node is not JsonArray array)
            {
                result.Add(row);
                continue;
            }

            foreach (var item in array)
            {
                var copy = (JsonObject)obj.DeepClone();
                SetPath(copy, stage.Field.Path.Count > 1 && name != stage.Field.DottedName ? stage.Field.Path.Skip(1).ToArray() : stage.Field.Path, item?.DeepClone());
                result.Add(copy);
            }
        }

        return result;
    }

    private static void SetPath(JsonObject root, IReadOnlyList<string> path, JsonNode? value)
    {
        var current = root;
        for (var i = 0; i < path.Count - 1; i++)
        {
            if (ExpressionEvaluator.FindProperty(current, path[i]) is not JsonObject next)
            {
                return;
            }

            current = next;
        }

        var last = current.FirstOrDefault(kv => string.Equals(kv.Key, path[^1], StringComparison.OrdinalIgnoreCase)).Key ?? path[^1];
        current[last] = value;
    }
}

public sealed class CountExecutor : StageExecutor<CountStage>
{
    protected override IReadOnlyList<JsonNode?> Execute(CountStage stage, IReadOnlyList<JsonNode?> rows, StageExecutionContext context) =>
        [new JsonObject { ["count"] = rows.Count }];
}
