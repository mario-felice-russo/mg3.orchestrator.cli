using System.Globalization;
using Mg3.Orchestrator.Abstractions.Configuration;
using Microsoft.Extensions.Options;

namespace Mg3.Orchestrator.Infrastructure.Configuration;

/// <summary>Valida il profilo; ogni errore indica la chiave e il motivo.</summary>
public sealed class Mg3ProfileValidator : IValidateOptions<Mg3Profile>
{
    public ValidateOptionsResult Validate(string? name, Mg3Profile options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();
        ValidateOrchestrator(options.Orchestrator, errors);
        ValidateAuth(options.Auth, errors);
        ValidateRanges(options, errors);
        ValidateOutput(options.Output, errors);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    public static IEnumerable<string> WriteScopes(string? scopes) =>
        (scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !s.EndsWith(".Read", StringComparison.OrdinalIgnoreCase));

    private static void ValidateOrchestrator(OrchestratorSettings settings, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            errors.Add("[url] obbligatorio: indica l'URL di Orchestrator.");
        }
        else if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add($"[url] deve essere un URL http(s) assoluto, trovato '{settings.BaseUrl}'.");
        }

        if (settings.Folder.Path is { } path && path.Any(c => c > 127 || char.IsControl(c)))
        {
            errors.Add("[folderPath] può contenere solo caratteri ASCII stampabili; usa folderId o folderKey.");
        }

        if (settings.Folder.Key is { } key && !Guid.TryParse(key, out _))
        {
            errors.Add($"[folderKey] deve essere un GUID, trovato '{key}'.");
        }
    }

    private static void ValidateAuth(AuthSettings auth, List<string> errors)
    {
        void Require(string? value, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"[{key}] obbligatorio con {Credentials(auth.Mode)}.");
            }
        }

        switch (auth.Mode)
        {
            case AuthMode.ClientCredentials:
                Require(auth.IdentityUrl, "identityUrl");
                Require(auth.ClientId, "clientId");
                Require(auth.ClientSecret, "clientSecret");
                Require(auth.Scopes, "scopes");
                if (auth.IdentityUrl is not null && !Uri.TryCreate(auth.IdentityUrl, UriKind.Absolute, out _))
                {
                    errors.Add($"[identityUrl] deve essere un URL assoluto, trovato '{auth.IdentityUrl}'.");
                }

                break;
            case AuthMode.Pat:
                Require(auth.Token, "token");
                break;
            case AuthMode.Legacy:
                Require(auth.Username, "username");
                Require(auth.Password, "password");
                break;
        }

        foreach (var scope in WriteScopes(auth.Scopes))
        {
            errors.Add($"[scopes] lo scope '{scope}' non è di sola lettura: sono ammessi solo scope '*.Read'.");
        }
    }

    private static string Credentials(AuthMode mode) => mode switch
    {
        AuthMode.ClientCredentials => "le credenziali di un'applicazione esterna (clientId)",
        AuthMode.Pat => "un Personal Access Token",
        _ => "utente e password",
    };

    private static void ValidateRanges(Mg3Profile options, List<string> errors)
    {
        void Range(int value, int min, int max, string key)
        {
            if (value < min || value > max)
            {
                errors.Add($"[{key}] deve essere compreso tra {min} e {max}, trovato {value}.");
            }
        }

        Range(options.Http.TimeoutSeconds, 1, 600, "advanced.timeoutSeconds");
        Range(options.Http.MaxRetries, 0, 10, "advanced.maxRetries");
        Range(options.Http.RetryBaseDelayMs, 0, 60_000, "advanced.retryBaseDelayMs");
        Range(options.Http.PageSize, 1, 1000, "advanced.pageSize");
        Range(options.Limits.MaxItems, 1, 1_000_000, "advanced.maxItems");
        Range(options.Limits.MaxPages, 1, 10_000, "advanced.maxPages");
        Range(options.Limits.GlobalTimeoutSeconds, 1, 3600, "advanced.globalTimeoutSeconds");
        Range(options.Limits.MaxJoinRows, 1, 10_000_000, "advanced.maxJoinRows");
        Range(options.Output.MaxRows, 1, 1_000_000, "advanced.maxRows");
        Range(options.Output.MaxCellWidth, 4, 10_000, "advanced.maxCellWidth");
    }

    private static void ValidateOutput(OutputSettings output, List<string> errors)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(output.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            errors.Add($"[advanced.timeZone] fuso orario sconosciuto '{output.TimeZone}'.");
        }

        try
        {
            CultureInfo.GetCultureInfo(output.Culture, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            errors.Add($"[advanced.culture] cultura sconosciuta '{output.Culture}'.");
        }
    }
}
