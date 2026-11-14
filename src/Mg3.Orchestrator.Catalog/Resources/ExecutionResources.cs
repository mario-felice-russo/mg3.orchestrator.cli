using Mg3.Orchestrator.Abstractions.Catalog;

namespace Mg3.Orchestrator.Catalog.Resources;

public sealed class JobsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "jobs",
        Description = "Esecuzioni dei processi (job).",
        Path = "odata/Jobs",
        Scope = "OR.Jobs.Read",
        TimeField = "CreationTime",
        NameField = "ReleaseName",
        Fields =
        [
            F.Long("Id"), F.Guid("Key"), F.Enum("State"), F.Time("StartTime"), F.Time("EndTime"), F.Time("CreationTime"),
            F.Time("LastModificationTime"), F.Text("ReleaseName"), F.Long("ReleaseVersionId"), F.Text("HostMachineName"),
            F.Text("Source"), F.Enum("SourceType"), F.Enum("JobPriority"), F.Enum("Type"), F.Text("Info", false),
            F.Guid("BatchExecutionKey"), F.Enum("RuntimeType"), F.Long("OrganizationUnitId"),
            F.Text("OrganizationUnitFullyQualifiedName", false), F.Obj("Robot"), F.Obj("Release"),
        ],
    };
}

public sealed class ReleasesResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "releases",
        Aliases = ["processes"],
        Description = "Processi pubblicati nelle cartelle (release).",
        Path = "odata/Releases",
        Scope = "OR.Execution.Read",
        Fields =
        [
            F.Long("Id"), F.Guid("Key"), F.Text("Name"), F.Text("ProcessKey"), F.Text("ProcessVersion"),
            F.Bool("IsLatestVersion"), F.Bool("IsProcessDeleted"), F.Text("Description", false), F.Text("EnvironmentName"),
            F.Text("EntryPointPath", false), F.Long("OrganizationUnitId"), F.Time("CreationTime"),
        ],
    };
}

public sealed class PackagesResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "packages",
        Description = "Pacchetti presenti nel feed dei processi.",
        Path = "odata/Processes",
        Scope = "OR.Execution.Read",
        KeyKind = KeyKind.Textual,
        NameField = "Title",
        FolderScoped = false,
        Fields =
        [
            F.Text("Id"), F.Text("Key"), F.Text("Title"), F.Text("Version"), F.Text("Description", false),
            F.Time("Published"), F.Bool("IsLatestVersion"), F.Text("Authors", false),
        ],
        ExtraActions =
        [
            new ResourceAction(
                "versions",
                "Versioni di un pacchetto.",
                "odata/Processes/UiPath.Server.Configuration.OData.GetProcessVersions(processId='{key}')",
                RequiresKey: true,
                PayloadShape.ODataCollection),
        ],
    };
}

public sealed class TriggersResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "triggers",
        Aliases = ["schedules"],
        Description = "Trigger temporali dei processi (process schedules).",
        Path = "odata/ProcessSchedules",
        Scope = "OR.Jobs.Read",
        Fields =
        [
            F.Long("Id"), F.Guid("Key"), F.Text("Name"), F.Bool("Enabled"), F.Long("ReleaseId"), F.Text("ReleaseName"),
            F.Text("StartProcessCron", false), F.Text("StartProcessCronSummary", false), F.Text("TimeZoneId"),
            F.Time("StartProcessNextOccurrence"),
        ],
    };
}

public sealed class JobStatsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "stats jobs",
        Aliases = ["jobstats"],
        Description = "Conteggio dei job per stato.",
        Path = "api/Stats/GetJobsStats",
        Scope = "OR.Monitoring.Read",
        IsEntitySet = false,
        Shape = PayloadShape.Array,
        SupportsODataQuery = false,
        SupportsPaging = false,
        Fields = [F.Text("title", false), F.Long("count", false)],
    };
}

public sealed class SessionStatsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "stats sessions",
        Aliases = ["sessionstats"],
        Description = "Conteggio delle sessioni dei robot per stato.",
        Path = "api/Stats/GetSessionsStats",
        Scope = "OR.Monitoring.Read",
        IsEntitySet = false,
        Shape = PayloadShape.Array,
        SupportsODataQuery = false,
        SupportsPaging = false,
        Fields = [F.Text("title", false), F.Long("count", false)],
    };
}
