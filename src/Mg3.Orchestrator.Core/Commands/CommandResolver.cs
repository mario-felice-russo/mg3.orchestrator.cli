using System.Globalization;
using System.Text;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Http;

namespace Mg3.Orchestrator.Core.Commands;

public interface ICommandResolver
{
    ResolvedCommand Resolve(string command, IReadOnlyDictionary<string, string> args, DateTimeOffset now);
}

/// <summary>Traduce <c>"jobs list"</c>, <c>"jobs get 42"</c>, <c>"stats jobs"</c>, <c>"odata get odata/Jobs"</c> in una lettura.</summary>
public sealed class CommandResolver(IResourceCatalog catalog, Mg3Profile profile) : ICommandResolver
{
    private const int MaxResourceWords = 3;

    public ResolvedCommand Resolve(string command, IReadOnlyDictionary<string, string> args, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(args);
        var tokens = Tokenize(command);
        if (tokens.Count == 0)
        {
            throw new ConfigurationException("Comando vuoto.");
        }

        var folder = CommandArguments.Get(args, CommandArguments.Folder) is { } f ? FolderSettings.Parse(f) : profile.Orchestrator.Folder;
        if (string.Equals(tokens[0], "odata", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveRaw(tokens, args, folder);
        }

        var (resource, consumed) = FindResource(tokens);
        var action = tokens.Count > consumed ? tokens[consumed].ToLowerInvariant() : (resource.IsEntitySet ? "list" : "get");
        var key = tokens.Count > consumed + 1 ? string.Join(' ', tokens.Skip(consumed + 1)) : CommandArguments.Get(args, CommandArguments.Key);
        var scopedFolder = resource.FolderScoped ? folder : null;

        if (!resource.IsEntitySet && action == "get")
        {
            return new ResolvedCommand(CommandKind.Function, resource, resource.Path, ODataQueryOptions.Empty, scopedFolder, Paginate: false);
        }

        if (resource.ExtraActions.FirstOrDefault(a => string.Equals(a.Name, action, StringComparison.OrdinalIgnoreCase)) is { } extra)
        {
            if (extra.RequiresKey && key is null)
            {
                throw new ConfigurationException($"'{resource.Name} {extra.Name}' richiede una chiave.");
            }

            var path = extra.PathTemplate.Replace("{key}", Escape(key ?? string.Empty), StringComparison.Ordinal);
            return new ResolvedCommand(CommandKind.Function, resource, path, BuildOptions(args, null, now), scopedFolder, Paginate: false);
        }

        return action switch
        {
            "list" => new ResolvedCommand(CommandKind.List, resource, resource.Path, BuildOptions(args, resource, now) with { Count = true }, scopedFolder, resource.SupportsPaging),
            "count" => new ResolvedCommand(CommandKind.Count, resource, resource.Path, BuildOptions(args, resource, now) with { Top = 0, Skip = null, Count = true }, scopedFolder, Paginate: false),
            "get" when key is null => throw new ConfigurationException($"'{resource.Name} get' richiede una chiave (id, key o nome)."),
            "get" => ResolveGet(resource, key, args, scopedFolder),
            _ => throw new ConfigurationException($"Azione '{action}' non disponibile per '{resource.Name}'. Azioni: {string.Join(", ", resource.ActionNames)}."),
        };
    }

    private (ResourceDescriptor Resource, int Consumed) FindResource(List<string> tokens)
    {
        for (var n = Math.Min(MaxResourceWords, tokens.Count); n >= 1; n--)
        {
            if (catalog.TryResolve(string.Join(' ', tokens.Take(n)), out var descriptor))
            {
                return (descriptor, n);
            }
        }

        throw new ConfigurationException($"Risorsa sconosciuta '{tokens[0]}'. Usa 'mg3orch catalog' per l'elenco.");
    }

    private static ResolvedCommand ResolveGet(ResourceDescriptor resource, string key, IReadOnlyDictionary<string, string> args, FolderSettings? folder)
    {
        var select = CommandArguments.Get(args, CommandArguments.Select);
        var expand = CommandArguments.Get(args, CommandArguments.Expand);
        var options = new ODataQueryOptions { Select = select, Expand = expand };
        if (resource.KeyKind == KeyKind.Numeric && long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return new ResolvedCommand(CommandKind.GetByPath, resource, $"{resource.Path}({id.ToString(CultureInfo.InvariantCulture)})", options, folder, Paginate: false);
        }

        if (resource.KeyKind == KeyKind.Textual)
        {
            return new ResolvedCommand(CommandKind.GetByPath, resource, $"{resource.Path}('{Escape(key)}')", options, folder, Paginate: false);
        }

        string filter;
        if (Guid.TryParse(key, out var guid) && resource.GuidField is { } guidField)
        {
            filter = $"{guidField} eq {guid:D}";
        }
        else if (resource.NameField is { } nameField)
        {
            filter = $"{nameField} eq '{Escape(key)}'";
        }
        else
        {
            throw new ConfigurationException($"'{resource.Name}' supporta 'get' solo per id numerico.");
        }

        return new ResolvedCommand(CommandKind.GetByFilter, resource, resource.Path, options with { Filter = filter, Top = 2 }, folder, Paginate: false);
    }

    private static ResolvedCommand ResolveRaw(List<string> tokens, IReadOnlyDictionary<string, string> args, FolderSettings? folder)
    {
        if (tokens.Count != 3 || !string.Equals(tokens[1], "get", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException("Sintassi: odata get <percorso-relativo> (es. odata get odata/Jobs).");
        }

        var path = tokens[2].TrimStart('/');
        var lower = path.ToLowerInvariant();
        if ((!lower.StartsWith("odata/", StringComparison.Ordinal) && !lower.StartsWith("api/", StringComparison.Ordinal))
            || path.Contains("://", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal) || path.Contains('?', StringComparison.Ordinal))
        {
            throw new ConfigurationException($"Percorso non ammesso '{tokens[2]}': deve iniziare con 'odata/' o 'api/' e non contenere '..', '?' o URL assoluti. Usa --filter, --top, ... per le opzioni.");
        }

        return new ResolvedCommand(CommandKind.Raw, null, path, BuildOptions(args, null, DateTimeOffset.UtcNow), folder, Paginate: false);
    }

    private static ODataQueryOptions BuildOptions(IReadOnlyDictionary<string, string> args, ResourceDescriptor? resource, DateTimeOffset now)
    {
        var filter = CommandArguments.Get(args, CommandArguments.Filter);
        if (CommandArguments.Get(args, CommandArguments.Since) is { } since)
        {
            var field = resource?.TimeField ?? throw new ConfigurationException($"L'argomento 'since' non è disponibile per '{resource?.Name ?? "odata get"}': la risorsa non ha un campo temporale.");
            var from = now - CommandArguments.ParseDuration(since);
            var sinceFilter = $"{field} ge {from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)}";
            filter = filter is null ? sinceFilter : $"({filter}) and ({sinceFilter})";
        }

        return new ODataQueryOptions
        {
            Filter = filter,
            Select = CommandArguments.Get(args, CommandArguments.Select),
            OrderBy = CommandArguments.Get(args, CommandArguments.OrderBy),
            Expand = CommandArguments.Get(args, CommandArguments.Expand),
            Top = CommandArguments.GetInt(args, CommandArguments.Top),
            Skip = CommandArguments.GetInt(args, CommandArguments.Skip),
        };
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    /// <summary>Divide il comando su spazi, rispettando le virgolette.</summary>
    internal static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        foreach (var c in command)
        {
            if (quote is not null)
            {
                if (c == quote)
                {
                    quote = null;
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c is '"' or '\'' && current.Length == 0)
            {
                quote = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (quote is not null)
        {
            throw new ConfigurationException($"Virgolette non chiuse nel comando '{command}'.");
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
