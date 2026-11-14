using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Catalog.Resources;
using Microsoft.Extensions.DependencyInjection;

namespace Mg3.Orchestrator.Catalog;

public static class CatalogServiceCollectionExtensions
{
    /// <summary>Registra tutte le risorse del catalogo. Per aggiungere una risorsa basta una nuova riga.</summary>
    public static IServiceCollection AddMg3Catalog(this IServiceCollection services)
    {
        services.AddSingleton<IResourceDefinition, JobsResource>();
        services.AddSingleton<IResourceDefinition, ReleasesResource>();
        services.AddSingleton<IResourceDefinition, PackagesResource>();
        services.AddSingleton<IResourceDefinition, RobotsResource>();
        services.AddSingleton<IResourceDefinition, MachinesResource>();
        services.AddSingleton<IResourceDefinition, SessionsResource>();
        services.AddSingleton<IResourceDefinition, QueuesResource>();
        services.AddSingleton<IResourceDefinition, QueueItemsResource>();
        services.AddSingleton<IResourceDefinition, QueueItemEventsResource>();
        services.AddSingleton<IResourceDefinition, QueueStatsResource>();
        services.AddSingleton<IResourceDefinition, AssetsResource>();
        services.AddSingleton<IResourceDefinition, FoldersResource>();
        services.AddSingleton<IResourceDefinition, PersonalWorkspacesResource>();
        services.AddSingleton<IResourceDefinition, UsersResource>();
        services.AddSingleton<IResourceDefinition, RolesResource>();
        services.AddSingleton<IResourceDefinition, EnvironmentsResource>();
        services.AddSingleton<IResourceDefinition, TriggersResource>();
        services.AddSingleton<IResourceDefinition, RobotLogsResource>();
        services.AddSingleton<IResourceDefinition, AuditLogsResource>();
        services.AddSingleton<IResourceDefinition, BucketsResource>();
        services.AddSingleton<IResourceDefinition, LibrariesResource>();
        services.AddSingleton<IResourceDefinition, WebhooksResource>();
        services.AddSingleton<IResourceDefinition, CalendarsResource>();
        services.AddSingleton<IResourceDefinition, AlertsResource>();
        services.AddSingleton<IResourceDefinition, TasksResource>();
        services.AddSingleton<IResourceDefinition, CredentialStoresResource>();
        services.AddSingleton<IResourceDefinition, TestSetsResource>();
        services.AddSingleton<IResourceDefinition, TestSetExecutionsResource>();
        services.AddSingleton<IResourceDefinition, TestCaseExecutionsResource>();
        services.AddSingleton<IResourceDefinition, SettingsResource>();
        services.AddSingleton<IResourceDefinition, LicenseResource>();
        services.AddSingleton<IResourceDefinition, JobStatsResource>();
        services.AddSingleton<IResourceDefinition, SessionStatsResource>();
        services.AddSingleton<IResourceCatalog, ResourceCatalog>();
        return services;
    }
}
