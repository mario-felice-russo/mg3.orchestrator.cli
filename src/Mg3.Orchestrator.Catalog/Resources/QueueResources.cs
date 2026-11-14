using Mg3.Orchestrator.Abstractions.Catalog;

namespace Mg3.Orchestrator.Catalog.Resources;

public sealed class QueuesResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "queues",
        Description = "Definizioni delle code.",
        Path = "odata/QueueDefinitions",
        Scope = "OR.Queues.Read",
        TimeField = "CreationTime",
        Fields =
        [
            F.Long("Id"), F.Guid("Key"), F.Text("Name"), F.Text("Description", false), F.Long("MaxNumberOfRetries"),
            F.Bool("AcceptAutomaticallyRetry"), F.Bool("EnforceUniqueReference"), F.Time("CreationTime"),
            F.Long("OrganizationUnitId"),
        ],
    };
}

public sealed class QueueItemsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "queueitems",
        Description = "Elementi delle code.",
        Path = "odata/QueueItems",
        Scope = "OR.Queues.Read",
        NameField = "Reference",
        TimeField = "CreationTime",
        Fields =
        [
            F.Long("Id"), F.Guid("Key"), F.Long("QueueDefinitionId"), F.Enum("Status"), F.Enum("ReviewStatus"),
            F.Text("Reference"), F.Enum("ProcessingExceptionType"), F.Enum("Priority"), F.Time("CreationTime"),
            F.Time("StartProcessing"), F.Time("EndProcessing"), F.Time("DeferDate"), F.Time("DueDate"),
            F.Long("RetryNumber"), F.Obj("SpecificContent"), F.Obj("Output"), F.Obj("ProcessingException"),
        ],
    };
}

public sealed class QueueItemEventsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "queueitemevents",
        Description = "Storico degli eventi degli elementi di coda.",
        Path = "odata/QueueItemEvents",
        Scope = "OR.Queues.Read",
        NameField = null,
        GuidField = null,
        TimeField = "Timestamp",
        Fields =
        [
            F.Long("Id"), F.Long("QueueItemId"), F.Time("Timestamp"), F.Enum("Action"), F.Enum("Status"),
            F.Enum("ReviewStatus"), F.Long("UserId"), F.Text("UserName"), F.Text("Data", false),
        ],
    };
}

public sealed class QueueStatsResource : IResourceDefinition
{
    public ResourceDescriptor Descriptor { get; } = new()
    {
        Name = "queuestats",
        Description = "Stato di elaborazione aggregato delle code.",
        Path = "odata/QueueProcessingRecords/UiPathODataSvc.RetrieveQueuesProcessingStatus",
        Scope = "OR.Queues.Read",
        IsEntitySet = false,
        Shape = PayloadShape.ODataCollection,
        SupportsODataQuery = false,
        SupportsPaging = false,
        Fields =
        [
            F.Long("QueueDefinitionId", false), F.Text("QueueDefinitionName", false), F.Long("ItemsToProcess", false),
            F.Long("ItemsInProgress", false), F.Long("SuccessfulTransactionsNo", false), F.Long("ApplicationExceptionsNo", false),
            F.Long("BusinessExceptionsNo", false),
        ],
    };
}
