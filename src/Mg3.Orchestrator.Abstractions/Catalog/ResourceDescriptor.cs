namespace Mg3.Orchestrator.Abstractions.Catalog;

public enum FieldType
{
    Text,
    WholeNumber,
    Number,
    Boolean,
    Timestamp,
    Identifier,
    Enumeration,
    Complex,
}

public enum KeyKind
{
    Numeric,
    Textual,
}

/// <summary>Forma del payload restituito da Orchestrator.</summary>
public enum PayloadShape
{
    /// <summary>Oggetto OData con proprietà <c>value</c>.</summary>
    ODataCollection,

    /// <summary>Array JSON nudo.</summary>
    Array,

    /// <summary>Singolo oggetto.</summary>
    Entity,
}

public sealed record FieldDescriptor(string Name, FieldType Type, bool Filterable = false);

/// <summary>Azione aggiuntiva di una risorsa (es. <c>packages versions</c>).</summary>
/// <param name="PathTemplate">Percorso relativo; <c>{key}</c> viene sostituito dalla chiave (con apici singoli raddoppiati).</param>
public sealed record ResourceAction(string Name, string Description, string PathTemplate, bool RequiresKey, PayloadShape Shape);

/// <summary>Descrizione immutabile di una risorsa di lettura di Orchestrator.</summary>
public sealed record ResourceDescriptor
{
    public required string Name { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public required string Description { get; init; }
    public required string Path { get; init; }

    /// <summary>True se è un insieme di entità OData (abilita <c>list</c>, <c>get</c>, <c>count</c>).</summary>
    public bool IsEntitySet { get; init; } = true;

    /// <summary>Forma del payload per l'azione predefinita delle risorse non entity-set.</summary>
    public PayloadShape Shape { get; init; } = PayloadShape.ODataCollection;

    public bool SupportsODataQuery { get; init; } = true;
    public bool SupportsPaging { get; init; } = true;
    public string KeyField { get; init; } = "Id";
    public KeyKind KeyKind { get; init; } = KeyKind.Numeric;
    public string? NameField { get; init; } = "Name";
    public string? GuidField { get; init; } = "Key";
    public string? TimeField { get; init; }
    public IReadOnlyList<FieldDescriptor> Fields { get; init; } = [];
    public bool FolderScoped { get; init; } = true;
    public required string Scope { get; init; }
    public bool Verified { get; init; }
    public IReadOnlyList<ResourceAction> ExtraActions { get; init; } = [];

    public FieldDescriptor? FindField(string name) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] EntitySetActions = ["list", "get", "count"];
    private static readonly string[] FunctionActions = ["get"];

    public IEnumerable<string> ActionNames =>
        (IsEntitySet ? EntitySetActions : FunctionActions).Concat(ExtraActions.Select(a => a.Name));
}
