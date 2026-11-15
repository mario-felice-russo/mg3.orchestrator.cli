using System.CommandLine;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Cli.Handlers;
using Mg3.Orchestrator.Core.Commands;

namespace Mg3.Orchestrator.Cli.Hosting;

/// <summary>Costruisce l'albero dei comandi dal catalogo: ogni risorsa genera i propri sottocomandi.</summary>
public sealed class CommandTreeBuilder(CliEnvironment environment, IResourceCatalog catalog)
{
    private readonly GlobalOptions _global = new();
    private readonly Option<string?> _query = new("--query", "-q") { Description = "Query DSL da applicare al risultato." };
    private readonly Option<string?> _filter = new("--filter") { Description = "$filter OData passato così com'è." };
    private readonly Option<string?> _select = new("--select") { Description = "$select OData." };
    private readonly Option<string?> _orderBy = new("--orderby") { Description = "$orderby OData." };
    private readonly Option<string?> _top = new("--top") { Description = "$top OData." };
    private readonly Option<string?> _skip = new("--skip") { Description = "$skip OData." };
    private readonly Option<string?> _expand = new("--expand") { Description = "$expand OData." };
    private readonly Option<string?> _since = new("--since") { Description = "Solo elementi recenti, es. 24h, 7d (campo temporale della risorsa)." };

    public GlobalOptions Global => _global;

    public RootCommand Build()
    {
        var root = new RootCommand("mg3orch — CLI di sola lettura per UiPath Orchestrator.");
        foreach (var option in _global.All)
        {
            root.Options.Add(option);
        }

        foreach (var resource in catalog.All)
        {
            AddResource(root, resource);
        }

        root.Subcommands.Add(BuildInit());
        root.Subcommands.Add(BuildCatalog());
        root.Subcommands.Add(BuildSchema());
        root.Subcommands.Add(BuildConfig());
        root.Subcommands.Add(BuildAuth());
        root.Subcommands.Add(BuildOData());
        root.Subcommands.Add(BuildRun());
        return root;
    }

    private void AddResource(RootCommand root, ResourceDescriptor resource)
    {
        var words = resource.Name.Split(' ');
        Command parent = root;
        foreach (var word in words[..^1])
        {
            var existing = parent.Subcommands.FirstOrDefault(c => c.Name == word);
            if (existing is null)
            {
                existing = new Command(word, $"Gruppo '{word}'.");
                parent.Subcommands.Add(existing);
            }

            parent = existing;
        }

        var command = new Command(words[^1], resource.Description);
        foreach (var alias in resource.Aliases.Where(a => !a.Contains(' ', StringComparison.Ordinal)))
        {
            command.Aliases.Add(alias);
        }

        if (resource.IsEntitySet)
        {
            command.Subcommands.Add(ReadCommand("list", "Elenca gli elementi.", $"{resource.Name} list", resource.TimeField is not null));
            command.Subcommands.Add(ReadCommand("count", "Conta gli elementi.", $"{resource.Name} count", resource.TimeField is not null));
            command.Subcommands.Add(KeyCommand("get", "Singolo elemento per id, key o nome.", $"{resource.Name} get"));
        }
        else
        {
            command.Options.Add(_query);
            command.SetAction((result, ct) => RunSingleAsync(result, resource.Name, ct));
        }

        foreach (var action in resource.ExtraActions)
        {
            command.Subcommands.Add(KeyCommand(action.Name, action.Description, $"{resource.Name} {action.Name}"));
        }

        parent.Subcommands.Add(command);
    }

    private Command ReadCommand(string name, string description, string commandText, bool supportsSince)
    {
        var command = new Command(name, description);
        Option[] options = supportsSince
            ? [_query, _filter, _select, _orderBy, _top, _skip, _expand, _since]
            : [_query, _filter, _select, _orderBy, _top, _skip, _expand];
        foreach (var option in options)
        {
            command.Options.Add(option);
        }

        command.SetAction((result, ct) => RunSingleAsync(result, commandText, ct));
        return command;
    }

    private Command KeyCommand(string name, string description, string commandText)
    {
        var key = new Argument<string>("key") { Description = "Id numerico, key (GUID) o nome." };
        var command = new Command(name, description);
        command.Arguments.Add(key);
        command.Options.Add(_query);
        command.Options.Add(_select);
        command.Options.Add(_expand);
        command.SetAction((result, ct) => RunSingleAsync(result, commandText, ct, result.GetValue(key)));
        return command;
    }

    private Task<int> RunSingleAsync(ParseResult result, string commandText, CancellationToken cancellationToken, string? key = null) =>
        Guard(result, async context =>
        {
            var args = QueryRunner.Args(
                (CommandArguments.Key, key),
                (CommandArguments.Filter, Value(result, _filter)),
                (CommandArguments.Select, Value(result, _select)),
                (CommandArguments.OrderBy, Value(result, _orderBy)),
                (CommandArguments.Top, Value(result, _top)),
                (CommandArguments.Skip, Value(result, _skip)),
                (CommandArguments.Expand, Value(result, _expand)),
                (CommandArguments.Since, Value(result, _since)));
            var request = QueryRunner.SingleCommand(context, commandText, args, Value(result, _query));
            return await QueryRunner.RunAsync(context, request, cancellationToken).ConfigureAwait(false);
        });

    private static string? Value(ParseResult result, Option<string?> option) =>
        result.CommandResult.Command.Options.Contains(option) ? result.GetValue(option) : null;

    private Command BuildInit()
    {
        var url = new Option<string?>("--url") { Description = "URL di Orchestrator; va bene quello copiato dal browser (cartella ricavata da 'fid')." };
        var token = new Option<string?>("--token") { Description = "Personal Access Token." };
        var clientId = new Option<string?>("--client-id") { Description = "Client id dell'applicazione esterna." };
        var clientSecret = new Option<string?>("--client-secret") { Description = "Client secret dell'applicazione esterna." };
        var scopes = new Option<string?>("--scopes") { Description = $"Scope di sola lettura (default: {InitCommand.DefaultScopes})." };
        var username = new Option<string?>("--username") { Description = "Utente (solo on-prem)." };
        var password = new Option<string?>("--password") { Description = "Password (solo on-prem)." };
        var tenant = new Option<string?>("--tenant") { Description = "Tenant (solo on-prem)." };
        var folderId = new Option<long?>("--folder-id") { Description = "Id della cartella." };
        var folderPath = new Option<string?>("--folder-path") { Description = "Percorso della cartella, es. Shared/Finance." };
        var force = new Option<bool>("--force") { Description = "Sovrascrive un file esistente." };
        var command = new Command("init", "Crea il file di configurazione. Chiede i valori mancanti; con le opzioni funziona senza domande.");
        foreach (var option in new Option[] { url, token, clientId, clientSecret, scopes, username, password, tenant, folderId, folderPath, force })
        {
            command.Options.Add(option);
        }

        command.SetAction((result, _) => Guard(result, context => Task.FromResult(InitCommand.Run(context, new InitOptions(
            result.GetValue(url),
            result.GetValue(token),
            result.GetValue(clientId),
            result.GetValue(clientSecret),
            result.GetValue(scopes),
            result.GetValue(username),
            result.GetValue(password),
            result.GetValue(tenant),
            result.GetValue(folderId),
            result.GetValue(folderPath),
            result.GetValue(force))))));
        return command;
    }

    private Command BuildCatalog()
    {
        var command = new Command("catalog", "Elenca risorse, azioni e campi filtrabili.");
        command.SetAction((result, _) => Guard(result, context => Task.FromResult(ServiceCommands.Catalog(context, catalog))));
        return command;
    }

    private Command BuildSchema()
    {
        var command = new Command("schema", "Emette il manifest JSON dei tool per gli harness IA.");
        command.SetAction((result, _) => Guard(result, context => Task.FromResult(ServiceCommands.Schema(context, catalog))));
        return command;
    }

    private Command BuildConfig()
    {
        var validate = new Command("validate", "Valida la configurazione e la stampa con i segreti mascherati.");
        validate.SetAction((result, _) => Guard(result, context => Task.FromResult(ServiceCommands.ConfigValidate(context))));
        var command = new Command("config", "Gestione della configurazione.");
        command.Subcommands.Add(validate);
        return command;
    }

    private Command BuildAuth()
    {
        var test = new Command("test", "Ottiene un token e chiama un endpoint leggero.");
        test.SetAction((result, ct) => Guard(result, context => ServiceCommands.AuthTestAsync(context, ct)));
        var command = new Command("auth", "Verifica dell'autenticazione.");
        command.Subcommands.Add(test);
        return command;
    }

    private Command BuildOData()
    {
        var path = new Argument<string>("path") { Description = "Percorso relativo che inizia con 'odata/' o 'api/'." };
        var get = new Command("get", "Lettura GET generica su un percorso non presente nel catalogo.");
        get.Arguments.Add(path);
        foreach (var option in new[] { _query, _filter, _select, _orderBy, _top, _skip, _expand })
        {
            get.Options.Add(option);
        }

        get.SetAction((result, ct) => RunSingleAsync(result, "odata get " + Quote(result.GetValue(path) ?? string.Empty), ct));
        var command = new Command("odata", "Accesso OData generico (solo GET).");
        command.Subcommands.Add(get);
        return command;
    }

    private Command BuildRun()
    {
        var request = new Option<string>("--request", "-r") { Description = "File JSON della richiesta, oppure '-' per stdin.", Required = true };
        var command = new Command("run", "Esegue una richiesta batch JSON (più comandi, con dipendenze).");
        command.Options.Add(request);
        command.SetAction((result, ct) => Guard(result, async context =>
        {
            var source = result.GetValue(request)!;
            string json;
            if (source == "-")
            {
                json = await environment.In.ReadToEndAsync(ct).ConfigureAwait(false);
            }
            else
            {
                var path = Path.IsPathRooted(source) ? source : Path.Combine(environment.WorkingDirectory, source);
                if (!File.Exists(path))
                {
                    throw new ConfigurationException($"Il file della richiesta '{source}' non esiste.");
                }

                json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            }

            var batch = BatchRequestReader.Parse(json, context.BatchOptions());
            return await QueryRunner.RunAsync(context, batch, ct).ConfigureAwait(false);
        }));
        return command;
    }

    private async Task<int> Guard(ParseResult result, Func<CliContext, Task<int>> action)
    {
        var context = new CliContext(environment, _global, result);
        try
        {
            return await action(context).ConfigureAwait(false);
        }
        catch (Mg3Exception ex)
        {
            return context.WriteError(ex);
        }
    }

    private static string Quote(string value) => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
