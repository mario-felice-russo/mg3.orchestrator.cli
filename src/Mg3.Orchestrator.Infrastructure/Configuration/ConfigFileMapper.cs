using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;

namespace Mg3.Orchestrator.Infrastructure.Configuration;

/// <summary>Converte il file nel profilo interno: ricava la modalità di autenticazione e i default.</summary>
public static class ConfigFileMapper
{
    public static Mg3Profile ToProfile(ConfigFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var defaults = new Mg3Profile();
        var advanced = file.Advanced ?? new AdvancedSettings();
        var mode = DetectMode(file);
        return new Mg3Profile
        {
            Orchestrator = new OrchestratorSettings
            {
                BaseUrl = file.Url,
                TenantName = file.TenantName,
                Folder = Folder(file),
            },
            Auth = new AuthSettings
            {
                Mode = mode,
                Token = file.Token,
                ClientId = file.ClientId,
                ClientSecret = file.ClientSecret,
                Scopes = file.Scopes,
                IdentityUrl = mode == AuthMode.ClientCredentials ? file.IdentityUrl ?? DefaultIdentityUrl(file.Url) : file.IdentityUrl,
                Username = file.Username,
                Password = file.Password,
                TokenCache = advanced.TokenCache ?? defaults.Auth.TokenCache,
                TokenCachePath = advanced.TokenCachePath,
            },
            Http = new HttpSettings
            {
                TimeoutSeconds = advanced.TimeoutSeconds ?? defaults.Http.TimeoutSeconds,
                MaxRetries = advanced.MaxRetries ?? defaults.Http.MaxRetries,
                RetryBaseDelayMs = advanced.RetryBaseDelayMs ?? defaults.Http.RetryBaseDelayMs,
                PageSize = advanced.PageSize ?? defaults.Http.PageSize,
            },
            Limits = new LimitsSettings
            {
                MaxItems = advanced.MaxItems ?? defaults.Limits.MaxItems,
                MaxPages = advanced.MaxPages ?? defaults.Limits.MaxPages,
                GlobalTimeoutSeconds = advanced.GlobalTimeoutSeconds ?? defaults.Limits.GlobalTimeoutSeconds,
                MaxJoinRows = advanced.MaxJoinRows ?? defaults.Limits.MaxJoinRows,
            },
            Output = new OutputSettings
            {
                Format = file.Format ?? defaults.Output.Format,
                TextStyle = file.Style ?? defaults.Output.TextStyle,
                Envelope = advanced.Envelope ?? defaults.Output.Envelope,
                MaxRows = advanced.MaxRows ?? defaults.Output.MaxRows,
                MaxCellWidth = advanced.MaxCellWidth ?? defaults.Output.MaxCellWidth,
                TimeZone = advanced.TimeZone ?? defaults.Output.TimeZone,
                Culture = advanced.Culture ?? defaults.Output.Culture,
            },
            Verbosity = advanced.Verbosity ?? defaults.Verbosity,
        };
    }

    /// <summary>Configurazione effettiva in formato file, con i default espliciti e i segreti mascherati.</summary>
    public static ConfigFile ToMaskedFile(Mg3Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        static string? Mask(string? value) => value is null ? null : "***";
        return new ConfigFile
        {
            Url = profile.Orchestrator.BaseUrl,
            FolderId = profile.Orchestrator.Folder.Id,
            FolderPath = profile.Orchestrator.Folder.Path,
            FolderKey = profile.Orchestrator.Folder.Key,
            TenantName = profile.Orchestrator.TenantName,
            Token = Mask(profile.Auth.Token),
            ClientId = profile.Auth.ClientId,
            ClientSecret = Mask(profile.Auth.ClientSecret),
            Scopes = profile.Auth.Scopes,
            IdentityUrl = profile.Auth.IdentityUrl,
            Username = profile.Auth.Username,
            Password = Mask(profile.Auth.Password),
            Format = profile.Output.Format,
            Style = profile.Output.TextStyle,
            Advanced = new AdvancedSettings
            {
                Envelope = profile.Output.Envelope,
                MaxRows = profile.Output.MaxRows,
                MaxCellWidth = profile.Output.MaxCellWidth,
                TimeZone = profile.Output.TimeZone,
                Culture = profile.Output.Culture,
                TimeoutSeconds = profile.Http.TimeoutSeconds,
                MaxRetries = profile.Http.MaxRetries,
                RetryBaseDelayMs = profile.Http.RetryBaseDelayMs,
                PageSize = profile.Http.PageSize,
                MaxItems = profile.Limits.MaxItems,
                MaxPages = profile.Limits.MaxPages,
                GlobalTimeoutSeconds = profile.Limits.GlobalTimeoutSeconds,
                MaxJoinRows = profile.Limits.MaxJoinRows,
                TokenCache = profile.Auth.TokenCache,
                TokenCachePath = profile.Auth.TokenCachePath,
                Verbosity = profile.Verbosity,
            },
        };
    }

    /// <summary>Cloud (<c>.../orchestrator_/</c>) → <c>/identity_/connect/token</c>; on‑prem → <c>/identity/connect/token</c>.</summary>
    public static string? DefaultIdentityUrl(string? orchestratorUrl)
    {
        if (!Uri.TryCreate(orchestratorUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var cloud = uri.AbsolutePath.Contains("/orchestrator_", StringComparison.OrdinalIgnoreCase);
        return $"{uri.Scheme}://{uri.Authority}/{(cloud ? "identity_" : "identity")}/connect/token";
    }

    private static AuthMode DetectMode(ConfigFile file)
    {
        var pat = !string.IsNullOrWhiteSpace(file.Token);
        var app = !string.IsNullOrWhiteSpace(file.ClientId) || !string.IsNullOrWhiteSpace(file.ClientSecret);
        var user = !string.IsNullOrWhiteSpace(file.Username) || !string.IsNullOrWhiteSpace(file.Password);
        return (pat, app, user) switch
        {
            (true, false, false) => AuthMode.Pat,
            (false, true, false) => AuthMode.ClientCredentials,
            (false, false, true) => AuthMode.Legacy,
            (false, false, false) => throw new ConfigurationException(
                "Mancano le credenziali: indica 'token' (Personal Access Token), oppure 'clientId' + 'clientSecret' + 'scopes' (applicazione esterna), oppure 'username' + 'password' (on-prem)."),
            _ => throw new ConfigurationException(
                "Indica un solo tipo di credenziali: 'token', oppure 'clientId'/'clientSecret', oppure 'username'/'password'."),
        };
    }

    private static FolderSettings Folder(ConfigFile file)
    {
        var count = (file.FolderId is null ? 0 : 1) + (string.IsNullOrWhiteSpace(file.FolderPath) ? 0 : 1) + (string.IsNullOrWhiteSpace(file.FolderKey) ? 0 : 1);
        if (count > 1)
        {
            throw new ConfigurationException("Indica la cartella in un solo modo: 'folderId', 'folderPath' oppure 'folderKey'.");
        }

        return new FolderSettings { Id = file.FolderId, Path = file.FolderPath, Key = file.FolderKey };
    }
}
