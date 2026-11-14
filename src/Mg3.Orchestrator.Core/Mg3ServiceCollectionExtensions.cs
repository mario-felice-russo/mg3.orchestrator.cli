using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Catalog;
using Mg3.Orchestrator.Core.Batch;
using Mg3.Orchestrator.Core.Commands;
using Mg3.Orchestrator.Infrastructure;
using Mg3.Orchestrator.Output;
using Mg3.Orchestrator.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Mg3.Orchestrator.Core;

public static class Mg3ServiceCollectionExtensions
{
    /// <summary>
    /// Registra tutti i servizi di mg3orch per l'uso come libreria.
    /// Il profilo è fisso per il container; il blocco <c>config</c> di <see cref="BatchRequest"/> è usato solo dalla CLI.
    /// </summary>
    public static IServiceCollection AddMg3Orchestrator(this IServiceCollection services, Mg3Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        services.AddMg3Catalog();
        services.AddMg3Query();
        services.AddMg3Output();
        services.AddMg3Infrastructure(profile);
        services.AddSingleton<ICommandResolver, CommandResolver>();
        services.AddSingleton<ICommandExecutor, CommandExecutor>();
        services.AddSingleton<IOrchestratorQueryService, OrchestratorQueryService>();
        return services;
    }
}
