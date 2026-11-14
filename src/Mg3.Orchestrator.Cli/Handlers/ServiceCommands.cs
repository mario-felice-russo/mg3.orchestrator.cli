using System.Text.Json;
using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Abstractions.Http;
using Mg3.Orchestrator.Catalog;
using Mg3.Orchestrator.Cli.Hosting;
using Mg3.Orchestrator.Infrastructure.Configuration;
using Mg3.Orchestrator.Output.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Mg3.Orchestrator.Cli.Handlers;

/// <summary>Comandi di servizio: catalog, schema, config validate, auth test.</summary>
public static class ServiceCommands
{
    public static int Catalog(CliContext context, IResourceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(catalog);
        var profile = LenientProfile(context);
        using var services = context.BuildServices(profile);
        var payload = new JsonArray(catalog.All.Select(Describe).ToArray());
        var rows = catalog.All.Select(r => (JsonNode?)new JsonObject
        {
            ["name"] = r.Name,
            ["aliases"] = string.Join(", ", r.Aliases),
            ["actions"] = string.Join(", ", r.ActionNames),
            ["path"] = r.Path,
            ["folder"] = r.FolderScoped,
            ["scope"] = r.Scope,
            ["verified"] = r.Verified,
            ["filterable"] = string.Join(", ", r.Fields.Where(f => f.Filterable).Select(f => f.Name)),
        }).ToArray();
        return context.WriteResult(services, Single("catalog", payload, rows), profile.Output);
    }

    public static int Schema(CliContext context, IResourceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Environment.Out.WriteLine(ToolManifest.Build(catalog).ToJsonString(JsonOutputFormatter.WriteOptions));
        return ExitCodes.Success;
    }

    public static int ConfigValidate(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var profile = context.LoadProfile();
        using var services = context.BuildServices(profile);
        var masked = Mask(JsonSerializer.SerializeToNode(profile, ConfigurationJson.Options)!.AsObject());
        var rows = Flatten(masked, string.Empty).Select(kv => (JsonNode?)new JsonObject { ["key"] = kv.Key, ["value"] = kv.Value }).ToArray();
        return context.WriteResult(services, Single("config validate", masked, rows), profile.Output);
    }

    public static async Task<int> AuthTestAsync(CliContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var profile = context.LoadProfile();
        await using var services = context.BuildServices(profile);
        try
        {
            await services.GetRequiredService<ITokenProvider>().GetTokenAsync(cancellationToken).ConfigureAwait(false);
            var reader = services.GetRequiredService<IOrchestratorReader>();
            await reader.ReadAsync(new ReadRequest("odata/Folders", new ODataQueryOptions { Top = 1 }, null, Paginate: false), cancellationToken).ConfigureAwait(false);
        }
        catch (Mg3Exception ex)
        {
            return context.WriteError(ex, profile.Output);
        }

        var payload = new JsonObject
        {
            ["status"] = "ok",
            ["authMode"] = profile.Auth.Mode.ToString(),
            ["baseUrl"] = profile.Orchestrator.BaseUrl,
            ["message"] = "Token ottenuto e odata/Folders raggiungibile.",
        };
        return context.WriteResult(services, Single("auth test", payload, [payload]), profile.Output);
    }

    private static Mg3Profile LenientProfile(CliContext context)
    {
        try
        {
            return context.LoadProfile(validate: false);
        }
        catch (ConfigurationException)
        {
            return new Mg3Profile { Output = context.Options.FallbackOutput(context.ParseResult) };
        }
    }

    private static BatchResult Single(string command, JsonNode payload, IReadOnlyList<JsonNode?> rows) =>
        new([new CommandResult { Id = command, Command = command, Payload = payload, Shape = PayloadShape.Entity, Rows = rows }], IsBatch: false, ExitCodes.Success);

    private static JsonNode Describe(ResourceDescriptor r) => new JsonObject
    {
        ["name"] = r.Name,
        ["aliases"] = new JsonArray(r.Aliases.Select(a => (JsonNode?)a).ToArray()),
        ["description"] = r.Description,
        ["path"] = r.Path,
        ["actions"] = new JsonArray(r.ActionNames.Select(a => (JsonNode?)a).ToArray()),
        ["folderScoped"] = r.FolderScoped,
        ["scope"] = r.Scope,
        ["verified"] = r.Verified,
        ["timeField"] = r.TimeField,
        ["fields"] = new JsonArray(r.Fields.Select(f => (JsonNode?)new JsonObject
        {
            ["name"] = f.Name,
            ["type"] = f.Type.ToString(),
            ["filterable"] = f.Filterable,
        }).ToArray()),
    };

    private static JsonObject Mask(JsonObject profile)
    {
        if (profile["auth"] is JsonObject auth)
        {
            foreach (var key in new[] { "clientSecret", "token", "password" })
            {
                if (auth[key] is not null)
                {
                    auth[key] = "***";
                }
            }
        }

        return profile;
    }

    private static IEnumerable<KeyValuePair<string, JsonNode?>> Flatten(JsonObject obj, string prefix)
    {
        foreach (var (key, value) in obj)
        {
            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            if (value is JsonObject child)
            {
                foreach (var nested in Flatten(child, path))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return new KeyValuePair<string, JsonNode?>(path, value?.DeepClone());
            }
        }
    }

    /// <summary>Catalogo disponibile senza configurazione (serve a costruire l'albero dei comandi).</summary>
    public static IResourceCatalog CreateCatalog()
    {
        var services = new ServiceCollection().AddMg3Catalog();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IResourceCatalog>();
    }
}
