using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Http;

namespace Mg3.Orchestrator.Core.Commands;

public enum CommandKind
{
    List,
    Count,
    GetByPath,
    GetByFilter,
    Function,
    Raw,
}

/// <summary>Comando risolto: cosa leggere, con quali opzioni e in quale cartella.</summary>
public sealed record ResolvedCommand(
    CommandKind Kind,
    ResourceDescriptor? Resource,
    string Path,
    ODataQueryOptions Options,
    FolderSettings? Folder,
    bool Paginate)
{
    public bool SupportsPushdown => Kind == CommandKind.List && Resource is { SupportsODataQuery: true };
}
