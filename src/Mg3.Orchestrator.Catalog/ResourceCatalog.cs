using System.Diagnostics.CodeAnalysis;
using Mg3.Orchestrator.Abstractions.Catalog;

namespace Mg3.Orchestrator.Catalog;

/// <summary>Catalogo costruito dalle <see cref="IResourceDefinition"/> registrate.</summary>
public sealed class ResourceCatalog : IResourceCatalog
{
    private readonly Dictionary<string, ResourceDescriptor> _byName = new(StringComparer.OrdinalIgnoreCase);

    public ResourceCatalog(IEnumerable<IResourceDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        All = definitions.Select(d => d.Descriptor).OrderBy(d => d.Name, StringComparer.Ordinal).ToArray();
        foreach (var descriptor in All)
        {
            foreach (var name in descriptor.Aliases.Prepend(descriptor.Name))
            {
                if (!_byName.TryAdd(name, descriptor))
                {
                    throw new InvalidOperationException($"Nome di risorsa duplicato nel catalogo: '{name}'.");
                }
            }
        }
    }

    public IReadOnlyList<ResourceDescriptor> All { get; }

    public bool TryResolve(string name, [NotNullWhen(true)] out ResourceDescriptor? descriptor) =>
        _byName.TryGetValue(name, out descriptor);
}
