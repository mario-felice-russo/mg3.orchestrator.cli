using Mg3.Orchestrator.Abstractions.Catalog;

namespace Mg3.Orchestrator.Catalog.Resources;

public sealed class RobotLogsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "robotlogs",
        Aliases = ["logs"],
        Description = "Log scritti dai robot durante l'esecuzione.",
        Path = "odata/RobotLogs",
        Scope = "OR.Monitoring.Read",
        NameField = "ProcessName",
        GuidField = null,
        TimeField = "TimeStamp",
        Fields =
        [
            F.Long("Id"), F.Enum("Level"), F.Text("ProcessName"), F.Time("TimeStamp"), F.Text("Message", false),
            F.Guid("JobKey"), F.Text("RobotName"), F.Text("HostMachineName"), F.Long("MachineId"), F.Text("WindowsIdentity"),
        ],
    };
}

public sealed class AuditLogsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "auditlogs",
        Description = "Registro di audit del tenant.",
        Path = "odata/AuditLogs",
        Scope = "OR.Audit.Read",
        NameField = "DisplayName",
        GuidField = null,
        FolderScoped = false,
        TimeField = "ExecutionTime",
        Fields =
        [
            F.Long("Id"), F.Text("ServiceName"), F.Text("MethodName"), F.Time("ExecutionTime"), F.Enum("Action"),
            F.Enum("Component"), F.Text("DisplayName"), F.Long("EntityId"), F.Text("OperationText", false),
            F.Text("UserName"), F.Enum("UserType"), F.Text("Parameters", false),
        ],
    };
}

public sealed class BucketsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "buckets",
        Description = "Bucket di archiviazione.",
        Path = "odata/Buckets",
        Scope = "OR.Administration.Read",
        Fields =
        [
            F.Long("Id"), F.Guid("Key"), F.Text("Name"), F.Text("Description", false), F.Text("StorageProvider"),
            F.Text("Identifier"), F.Bool("ReadOnly"),
        ],
    };
}

public sealed class LibrariesResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "libraries",
        Description = "Librerie presenti nel feed.",
        Path = "odata/Libraries",
        Scope = "OR.Execution.Read",
        KeyKind = KeyKind.Textual,
        NameField = "Title",
        FolderScoped = false,
        Fields =
        [
            F.Text("Id"), F.Text("Key"), F.Text("Title"), F.Text("Version"), F.Text("Description", false),
            F.Time("Published"), F.Bool("IsLatestVersion"),
        ],
    };
}

public sealed class TasksResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "tasks",
        Description = "Attività di Action Center.",
        Path = "odata/Tasks",
        Scope = "OR.Tasks.Read",
        NameField = "Title",
        TimeField = "CreationTime",
        Fields =
        [
            F.Long("Id"), F.Guid("Key"), F.Text("Title"), F.Enum("Type"), F.Enum("Priority"), F.Enum("Status"),
            F.Long("AssignedToUserId"), F.Time("CreationTime"), F.Time("LastModificationTime"), F.Bool("IsDeleted"),
            F.Text("TaskCatalogName"),
        ],
    };
}

public sealed class TestSetsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "testsets",
        Description = "Set di test.",
        Path = "odata/TestSets",
        Scope = "OR.TestSets.Read",
        TimeField = "CreationTime",
        Fields = [F.Long("Id"), F.Guid("Key"), F.Text("Name"), F.Text("Description", false), F.Time("CreationTime")],
    };
}

public sealed class TestSetExecutionsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "testsetexecutions",
        Description = "Esecuzioni dei set di test.",
        Path = "odata/TestSetExecutions",
        Scope = "OR.TestSetExecutions.Read",
        GuidField = null,
        TimeField = "StartTime",
        Fields =
        [
            F.Long("Id"), F.Text("Name"), F.Enum("Status"), F.Long("TestSetId"), F.Time("StartTime"), F.Time("EndTime"),
            F.Enum("TriggerType"),
        ],
    };
}

public sealed class TestCaseExecutionsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "testcaseexecutions",
        Description = "Esecuzioni dei singoli casi di test.",
        Path = "odata/TestCaseExecutions",
        Scope = "OR.TestSetExecutions.Read",
        NameField = "EntryPointPath",
        GuidField = null,
        TimeField = "StartTime",
        Fields =
        [
            F.Long("Id"), F.Enum("Status"), F.Long("TestCaseId"), F.Long("TestSetExecutionId"), F.Time("StartTime"),
            F.Time("EndTime"), F.Text("EntryPointPath"), F.Long("JobId"),
        ],
    };
}
