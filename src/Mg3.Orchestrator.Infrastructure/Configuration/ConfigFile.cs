using Mg3.Orchestrator.Abstractions.Configuration;

namespace Mg3.Orchestrator.Infrastructure.Configuration;

/// <summary>
/// Struttura del file di configurazione. Obbligatori: <c>url</c> e un solo tipo di credenziali
/// (<c>token</c>, oppure <c>clientId</c> + <c>clientSecret</c> + <c>scopes</c>, oppure <c>username</c> + <c>password</c>).
/// Tutto il resto è facoltativo.
/// </summary>
public sealed record ConfigFile
{
    /// <summary>URL di Orchestrator, es. <c>https://cloud.uipath.com/org/tenant/orchestrator_/</c>.</summary>
    public string? Url { get; init; }

    public long? FolderId { get; init; }
    public string? FolderPath { get; init; }
    public string? FolderKey { get; init; }

    /// <summary>Solo on‑prem.</summary>
    public string? TenantName { get; init; }

    /// <summary>Personal Access Token.</summary>
    public string? Token { get; init; }

    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? Scopes { get; init; }

    /// <summary>Facoltativo: se assente si ricava da <see cref="Url"/>.</summary>
    public string? IdentityUrl { get; init; }

    /// <summary>Solo on‑prem (autenticazione legacy).</summary>
    public string? Username { get; init; }
    public string? Password { get; init; }

    public OutputFormat? Format { get; init; }
    public TextStyle? Style { get; init; }

    public AdvancedSettings? Advanced { get; init; }
}

/// <summary>Impostazioni facoltative; i valori assenti prendono il default.</summary>
public sealed record AdvancedSettings
{
    public bool? Envelope { get; init; }
    public int? MaxRows { get; init; }
    public int? MaxCellWidth { get; init; }
    public string? TimeZone { get; init; }
    public string? Culture { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? MaxRetries { get; init; }
    public int? RetryBaseDelayMs { get; init; }
    public int? PageSize { get; init; }
    public int? MaxItems { get; init; }
    public int? MaxPages { get; init; }
    public int? GlobalTimeoutSeconds { get; init; }
    public int? MaxJoinRows { get; init; }
    public TokenCacheMode? TokenCache { get; init; }
    public string? TokenCachePath { get; init; }
    public LogVerbosity? Verbosity { get; init; }
}
