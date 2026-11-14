using Mg3.Orchestrator.Abstractions.Query;
using Mg3.Orchestrator.Query.Evaluation;
using Mg3.Orchestrator.Query.Execution;
using Mg3.Orchestrator.Query.Functions;
using Mg3.Orchestrator.Query.Parsing;
using Mg3.Orchestrator.Query.Planning;
using Microsoft.Extensions.DependencyInjection;

namespace Mg3.Orchestrator.Query;

public static class QueryServiceCollectionExtensions
{
    /// <summary>Registra parser, funzioni, esecutori e planner. Nuovi stadi o funzioni: una registrazione in più.</summary>
    public static IServiceCollection AddMg3Query(this IServiceCollection services)
    {
        services.AddSingleton<IStageParser, WhereStageParser>();
        services.AddSingleton<IStageParser, SelectStageParser>();
        services.AddSingleton<IStageParser, OrderByStageParser>();
        services.AddSingleton<IStageParser, TakeStageParser>();
        services.AddSingleton<IStageParser, SkipStageParser>();
        services.AddSingleton<IStageParser, DistinctStageParser>();
        services.AddSingleton<IStageParser, GroupByStageParser>();
        services.AddSingleton<IStageParser, AggregateStageParser>();
        services.AddSingleton<IStageParser, JoinStageParser>();
        services.AddSingleton<IStageParser, LeftJoinStageParser>();
        services.AddSingleton<IStageParser, FlattenStageParser>();
        services.AddSingleton<IStageParser, CountStageParser>();

        services.AddSingleton<IQueryFunction, NowFunction>();
        services.AddSingleton<IQueryFunction, TodayFunction>();
        services.AddSingleton<IQueryFunction, DateFunction>();
        services.AddSingleton<IQueryFunction, LowerFunction>();
        services.AddSingleton<IQueryFunction, UpperFunction>();
        services.AddSingleton<IQueryFunction, LenFunction>();
        services.AddSingleton<IQueryFunction, CoalesceFunction>();
        services.AddSingleton<IQueryFunction, YearFunction>();
        services.AddSingleton<IQueryFunction, MonthFunction>();
        services.AddSingleton<IQueryFunction, DayFunction>();
        services.AddSingleton<IQueryFunction, HourFunction>();

        services.AddSingleton<IStageExecutor, WhereExecutor>();
        services.AddSingleton<IStageExecutor, SelectExecutor>();
        services.AddSingleton<IStageExecutor, OrderByExecutor>();
        services.AddSingleton<IStageExecutor, TakeExecutor>();
        services.AddSingleton<IStageExecutor, SkipExecutor>();
        services.AddSingleton<IStageExecutor, DistinctExecutor>();
        services.AddSingleton<IStageExecutor, GroupAggregateExecutor>();
        services.AddSingleton<IStageExecutor, JoinExecutor>();
        services.AddSingleton<IStageExecutor, FlattenExecutor>();
        services.AddSingleton<IStageExecutor, CountExecutor>();

        services.AddSingleton<ExpressionEvaluator>();
        services.AddSingleton<ODataFilterTranslator>();
        services.AddSingleton<IQueryParser, QueryParser>();
        services.AddSingleton<IQueryPrinter, QueryPrinter>();
        services.AddSingleton<IQueryPlanner, QueryPlanner>();
        services.AddSingleton<IQueryExecutor, QueryExecutor>();
        return services;
    }
}
