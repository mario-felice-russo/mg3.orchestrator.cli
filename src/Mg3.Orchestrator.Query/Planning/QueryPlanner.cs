using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Http;
using Mg3.Orchestrator.Abstractions.Query;

namespace Mg3.Orchestrator.Query.Planning;

/// <summary>
/// Sposta sul server il prefisso più lungo di stadi traducibili, rispettando l'ordine di applicazione OData
/// ($filter → $orderby → $skip → $top → $select), così che il risultato coincida con l'esecuzione in memoria.
/// </summary>
public sealed class QueryPlanner(ODataFilterTranslator translator) : IQueryPlanner
{
    private enum Phase
    {
        None,
        Ordered,
        Paged,
    }

    public QueryPlan Plan(IReadOnlyList<StageNode> stages, PlanningContext context)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(context);
        var server = context.BaseOptions;
        if (!context.PushdownEnabled || context.Resource is not { SupportsODataQuery: true } resource)
        {
            return new QueryPlan(server, [], stages);
        }

        var filters = new List<string>();
        if (server.Filter is not null)
        {
            filters.Add(server.Filter);
        }

        var phase = server.Top is not null || server.Skip is not null ? Phase.Paged : server.OrderBy is not null ? Phase.Ordered : Phase.None;
        var selected = server.Select is not null;
        var (orderBy, select, skip, top) = (server.OrderBy, server.Select, server.Skip, server.Top);
        var pushed = new List<StageNode>();
        foreach (var stage in stages)
        {
            var accepted = stage switch
            {
                WhereStage w when phase != Phase.Paged && !selected && translator.TryTranslate(w.Condition, resource, context.Now, out var filter)
                    => Accept(() => filters.Add(filter)),
                OrderByStage o when phase == Phase.None && !selected && TryOrderBy(o, resource, out var order)
                    => Accept(() => (orderBy, phase) = (order, Phase.Ordered)),
                SkipStage s when top is null
                    => Accept(() => (skip, phase) = ((skip ?? 0) + s.Count, Phase.Paged)),
                TakeStage t
                    => Accept(() => (top, phase) = (Math.Min(top ?? int.MaxValue, t.Count), Phase.Paged)),
                SelectStage s when !selected && TrySelect(s, resource, out var fields)
                    => Accept(() => (select, selected) = (fields, true)),
                _ => false,
            };

            if (!accepted)
            {
                break;
            }

            pushed.Add(stage);
        }

        var options = server with
        {
            Filter = filters.Count switch
            {
                0 => null,
                1 => filters[0],
                _ => string.Join(" and ", filters.Select(f => $"({f})")),
            },
            OrderBy = orderBy,
            Select = select,
            Skip = skip,
            Top = top,
        };
        return new QueryPlan(options, pushed, stages.Skip(pushed.Count).ToArray());
    }

    private static bool Accept(Action apply)
    {
        apply();
        return true;
    }

    private static bool TryOrderBy(OrderByStage stage, ResourceDescriptor resource, out string orderBy)
    {
        var parts = new List<string>();
        foreach (var key in stage.Keys)
        {
            if (key.Expression is not FieldExpr field || resource.FindField(field.DottedName) is not { Filterable: true, Type: not FieldType.Complex } descriptor)
            {
                orderBy = string.Empty;
                return false;
            }

            parts.Add(ODataFilterTranslator.Path(descriptor) + (key.Descending ? " desc" : " asc"));
        }

        orderBy = string.Join(",", parts);
        return true;
    }

    private static bool TrySelect(SelectStage stage, ResourceDescriptor resource, out string select)
    {
        var fields = new List<string>();
        foreach (var projection in stage.Projections)
        {
            if (projection.Expression is not FieldExpr { Path.Count: 1 } field
                || (projection.Alias is not null && !string.Equals(projection.Alias, field.Path[0], StringComparison.Ordinal))
                || resource.FindField(field.Path[0]) is not { } descriptor
                || !string.Equals(descriptor.Name, field.Path[0], StringComparison.Ordinal))
            {
                select = string.Empty;
                return false;
            }

            fields.Add(field.Path[0]);
        }

        select = string.Join(",", fields);
        return true;
    }
}
