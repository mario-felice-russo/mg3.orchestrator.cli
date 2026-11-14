namespace Mg3.Orchestrator.Abstractions.Configuration;

/// <summary>Configurazione effettiva di un profilo, dopo la fusione di tutti gli strati.</summary>
public sealed record Mg3Profile
{
    public OrchestratorSettings Orchestrator { get; init; } = new();
    public AuthSettings Auth { get; init; } = new();
    public HttpSettings Http { get; init; } = new();
    public LimitsSettings Limits { get; init; } = new();
    public OutputSettings Output { get; init; } = new();
}

public sealed record OrchestratorSettings
{
    public string? BaseUrl { get; init; }
    public string? TenantName { get; init; }
    public FolderSettings Folder { get; init; } = new();
}

public sealed record FolderSettings
{
    public string? Path { get; init; }
    public long? Id { get; init; }
    public string? Key { get; init; }

    public bool IsEmpty => Id is null && string.IsNullOrWhiteSpace(Key) && string.IsNullOrWhiteSpace(Path);

    /// <summary>Interpreta un valore libero: numero → id, GUID → key, altrimenti percorso.</summary>
    public static FolderSettings Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id))
        {
            return new FolderSettings { Id = id };
        }

        return Guid.TryParse(value, out _) ? new FolderSettings { Key = value } : new FolderSettings { Path = value };
    }
}

public enum AuthMode
{
    ClientCredentials,
    Pat,
    Legacy,
}

public enum TokenCacheMode
{
    Memory,
    Disk,
}

public sealed record AuthSettings
{
    public AuthMode Mode { get; init; } = AuthMode.ClientCredentials;
    public string? IdentityUrl { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? Scopes { get; init; }
    public string? Token { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public TokenCacheMode TokenCache { get; init; } = TokenCacheMode.Memory;
    public string? TokenCachePath { get; init; }
}

public sealed record HttpSettings
{
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxRetries { get; init; } = 3;
    public int RetryBaseDelayMs { get; init; } = 500;
    public int PageSize { get; init; } = 500;
}

public sealed record LimitsSettings
{
    public int MaxItems { get; init; } = 1000;
    public int MaxPages { get; init; } = 50;
    public int GlobalTimeoutSeconds { get; init; } = 120;
    public int MaxJoinRows { get; init; } = 100_000;
}

public enum OutputFormat
{
    Text,
    Json,
}

public enum TextStyle
{
    Table,
    List,
    Markdown,
    Csv,
}

public sealed record OutputSettings
{
    public OutputFormat Format { get; init; } = OutputFormat.Text;
    public TextStyle TextStyle { get; init; } = TextStyle.Table;
    public bool Envelope { get; init; }
    public int MaxRows { get; init; } = 200;
    public int MaxCellWidth { get; init; } = 80;
    public string TimeZone { get; init; } = "UTC";
    public string Culture { get; init; } = "it-IT";
}
