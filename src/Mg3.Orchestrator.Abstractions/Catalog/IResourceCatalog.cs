using System.Diagnostics.CodeAnalysis;

namespace Mg3.Orchestrator.Abstractions.Catalog;

/// <summary>Contributo di una singola risorsa al catalogo (una classe per risorsa).</summary>
public interface IResourceDefinition
{
    ResourceDescriptor Descriptor { get; }
}

public interface IResourceCatalog
{
    IReadOnlyList<ResourceDescriptor> All { get; }

    bool TryResolve(string name, [NotNullWhen(true)] out ResourceDescriptor? descriptor);
}
