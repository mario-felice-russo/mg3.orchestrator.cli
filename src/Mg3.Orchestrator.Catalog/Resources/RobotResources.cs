using Mg3.Orchestrator.Abstractions.Catalog;

namespace Mg3.Orchestrator.Catalog.Resources;

public sealed class RobotsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "robots",
        Description = "Robot definiti nel tenant.",
        Path = "odata/Robots",
        Scope = "OR.Robots.Read",
        GuidField = null,
        Fields =
        [
            F.Long("Id"), F.Text("Name"), F.Text("MachineName"), F.Long("MachineId"), F.Enum("Type"), F.Text("Username"),
            F.Text("Description", false), F.Text("RobotEnvironments", false), F.Bool("IsExternalLicensed"),
        ],
    };
}

public sealed class MachinesResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "machines",
        Description = "Macchine e template di macchina.",
        Path = "odata/Machines",
        Scope = "OR.Machines.Read",
        FolderScoped = false,
        Fields =
        [
            F.Long("Id"), F.Guid("Key"), F.Text("Name"), F.Enum("Type"), F.Text("Description", false),
            F.Long("UnattendedSlots"), F.Long("NonProductionSlots"), F.Long("TestAutomationSlots"),
        ],
    };
}

public sealed class SessionsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "sessions",
        Description = "Sessioni dei robot connessi.",
        Path = "odata/Sessions",
        Scope = "OR.Robots.Read",
        NameField = "HostMachineName",
        GuidField = null,
        TimeField = "ReportingTime",
        Fields =
        [
            F.Long("Id"), F.Enum("State"), F.Text("HostMachineName"), F.Long("MachineId"), F.Text("MachineName"),
            F.Bool("IsUnresponsive"), F.Time("ReportingTime"), F.Obj("Robot"),
        ],
    };
}

public sealed class EnvironmentsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "environments",
        Description = "Ambienti dei robot (solo cartelle classiche).",
        Path = "odata/Environments",
        Scope = "OR.Robots.Read",
        GuidField = null,
        Fields = [F.Long("Id"), F.Text("Name"), F.Text("Description", false), F.Enum("Type")],
    };
}

public sealed class CredentialStoresResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "credentialstores",
        Description = "Archivi delle credenziali configurati.",
        Path = "odata/CredentialStores",
        Scope = "OR.Settings.Read",
        FolderScoped = false,
        Fields = [F.Long("Id"), F.Guid("Key"), F.Text("Name"), F.Text("Type"), F.Long("ProxyId")],
    };
}
