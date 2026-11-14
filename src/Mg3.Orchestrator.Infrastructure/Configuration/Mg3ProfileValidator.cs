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
            errors.Add("[orchestrator.baseUrl] obbligatorio.");
        }
        else if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add($"[orchestrator.baseUrl] deve essere un URL http(s) assoluto, trovato '{settings.BaseUrl}'.");
        }

        if (settings.Folder.Path is { } path && path.Any(c => c > 127 || char.IsControl(c)))
        {
            errors.Add("[orchestrator.folder.path] può contenere solo caratteri ASCII stampabili; usare folder.id o folder.key.");
        }

        if (settings.Folder.Key is { } key && !Guid.TryParse(key, out _))
        {
            errors.Add($"[orchestrator.folder.key] deve essere un GUID, trovato '{key}'.");
        }
    }

    private static void ValidateAuth(AuthSettings auth, List<string> errors)
    {
        void Require(string? value, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"[auth.{key}] obbligatorio con auth.mode = {auth.Mode}.");
            }
        }

        switch (auth.Mode)
        {
            case AuthMode.ClientCredentials:
                Require(auth.IdentityUrl, "identityUrl");
                Require(auth.ClientId, "clientId");
                Require(auth.ClientSecret, "clientSecret");
                if (auth.IdentityUrl is not null && !Uri.TryCreate(auth.IdentityUrl, UriKind.Absolute, out _))
                {
                    errors.Add($"[auth.identityUrl] deve essere un URL assoluto, trovato '{auth.IdentityUrl}'.");
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
            errors.Add($"[auth.scopes] lo scope '{scope}' non è di sola lettura: sono ammessi solo scope '*.Read'.");
        }
    }

    private static void ValidateRanges(Mg3Profile options, List<string> errors)
    {
        void Range(int value, int min, int max, string key)
        {
            if (value < min || value > max)
            {
                errors.Add($"[{key}] deve essere compreso tra {min} e {max}, trovato {value}.");
            }
        }

        Range(options.Http.TimeoutSeconds, 1, 600, "http.timeoutSeconds");
        Range(options.Http.MaxRetries, 0, 10, "http.maxRetries");
        Range(options.Http.RetryBaseDelayMs, 0, 60_000, "http.retryBaseDelayMs");
        Range(options.Http.PageSize, 1, 1000, "http.pageSize");
        Range(options.Limits.MaxItems, 1, 1_000_000, "limits.maxItems");
        Range(options.Limits.MaxPages, 1, 10_000, "limits.maxPages");
        Range(options.Limits.GlobalTimeoutSeconds, 1, 3600, "limits.globalTimeoutSeconds");
        Range(options.Limits.MaxJoinRows, 1, 10_000_000, "limits.maxJoinRows");
        Range(options.Output.MaxRows, 1, 1_000_000, "output.maxRows");
        Range(options.Output.MaxCellWidth, 4, 10_000, "output.maxCellWidth");
    }

    private static void ValidateOutput(OutputSettings output, List<string> errors)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(output.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            errors.Add($"[output.timeZone] fuso orario sconosciuto '{output.TimeZone}'.");
        }

        try
        {
            CultureInfo.GetCultureInfo(output.Culture, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            errors.Add($"[output.culture] cultura sconosciuta '{output.Culture}'.");
        }
    }
}
