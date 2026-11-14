using System.Reflection;
using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Errors;

namespace Mg3.Orchestrator.Cli.Handlers;

/// <summary>Manifest dei tool per gli harness IA: un tool per comando, con JSON Schema dei parametri.</summary>
public static class ToolManifest
{
    private const string QueryDescription = "Query DSL a pipeline, es. \"where State == 'Faulted' | select Key, ReleaseName | take 10\".";

    public static JsonObject Build(IResourceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var tools = new JsonArray();
        foreach (var resource in catalog.All)
        {
            var baseName = resource.Name.Replace(' ', '_');
            if (resource.IsEntitySet)
            {
                tools.Add(Tool($"{baseName}_list", $"{resource.Name} list", $"Elenca: {resource.Description}", ListParameters(resource)));
                tools.Add(Tool($"{baseName}_get", $"{resource.Name} get", $"Singolo elemento: {resource.Description}", KeyParameters()));
                tools.Add(Tool($"{baseName}_count", $"{resource.Name} count", $"Conteggio: {resource.Description}", ListParameters(resource)));
            }
            else
            {
                tools.Add(Tool(baseName, resource.Name, resource.Description, Schema(("query", "string", QueryDescription), ("folder", "string", "Cartella: id, key o percorso."))));
            }

            foreach (var action in resource.ExtraActions)
            {
                tools.Add(Tool($"{baseName}_{action.Name}", $"{resource.Name} {action.Name}", action.Description, KeyParameters()));
            }
        }

        tools.Add(Tool("odata_get", "odata get", "Lettura GET generica su un percorso 'odata/...' o 'api/...' non presente nel catalogo.", Schema(
            ("path", "string", "Percorso relativo, es. odata/Jobs."),
            ("filter", "string", "$filter OData."),
            ("select", "string", "$select OData."),
            ("top", "integer", "$top OData."),
            ("folder", "string", "Cartella."))));
        tools.Add(new JsonObject
        {
            ["name"] = "run",
            ["command"] = "run --request -",
            ["description"] = "Esegue più comandi in un'unica richiesta JSON letta da stdin; i comandi possono riferirsi ai risultati di altri con 'from $id'.",
            ["inputSchema"] = BatchSchema(),
        });

        return new JsonObject
        {
            ["name"] = "mg3orch",
            ["version"] = typeof(ToolManifest).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "1.0.0",
            ["description"] = "CLI di sola lettura per UiPath Orchestrator con query DSL; output testo o JSON originale.",
            ["invocation"] = "mg3orch <risorsa> <azione> [chiave] [--query <DSL>] [--output text|json] — oppure mg3orch run --request - con la richiesta JSON su stdin.",
            ["exitCodes"] = ExitCodeTable(),
            ["envelopeSchema"] = EnvelopeSchema(),
            ["tools"] = tools,
        };
    }

    private static JsonObject Tool(string name, string command, string description, JsonObject parameters) => new()
    {
        ["name"] = name,
        ["command"] = command,
        ["description"] = description,
        ["inputSchema"] = parameters,
    };

    private static JsonObject ListParameters(ResourceDescriptor resource)
    {
        var schema = Schema(
            ("query", "string", QueryDescription),
            ("filter", "string", "$filter OData passato così com'è."),
            ("select", "string", "$select OData."),
            ("orderby", "string", "$orderby OData."),
            ("top", "integer", "Numero massimo di elementi."),
            ("skip", "integer", "Elementi da saltare."),
            ("expand", "string", "$expand OData."),
            ("folder", "string", "Cartella: id, key o percorso."));
        if (resource.TimeField is not null)
        {
            schema["properties"]!["since"] = Property("string", $"Solo elementi con {resource.TimeField} negli ultimi N (es. 24h, 7d).");
        }

        return schema;
    }

    private static JsonObject KeyParameters()
    {
        var schema = Schema(("key", "string", "Id numerico, key (GUID) o nome."), ("query", "string", QueryDescription), ("folder", "string", "Cartella."));
        schema["required"] = new JsonArray("key");
        return schema;
    }

    private static JsonObject Schema(params (string Name, string Type, string Description)[] properties)
    {
        var props = new JsonObject();
        foreach (var (name, type, description) in properties)
        {
            props[name] = Property(type, description);
        }

        return new JsonObject { ["type"] = "object", ["properties"] = props, ["additionalProperties"] = false };
    }

    private static JsonObject Property(string type, string description) => new() { ["type"] = type, ["description"] = description };

    private static JsonObject BatchSchema() => new()
    {
        ["type"] = "object",
        ["required"] = new JsonArray("commands"),
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["config"] = new JsonObject { ["type"] = "object", ["description"] = "Override della configurazione (stessa struttura del profilo, più 'profile')." },
            ["commands"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("id"),
                    ["additionalProperties"] = false,
                    ["properties"] = new JsonObject
                    {
                        ["id"] = Property("string", "Identificativo univoco, referenziabile con '$id'."),
                        ["command"] = Property("string", "Es. 'jobs list', 'jobs get 42', 'stats jobs'."),
                        ["args"] = new JsonObject { ["type"] = "object", ["description"] = "folder, filter, select, orderby, top, skip, expand, since, key." },
                        ["query"] = Property("string", QueryDescription),
                    },
                },
            },
            ["options"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["stopOnError"] = Property("boolean", "Interrompe il batch al primo errore."),
                    ["parallelism"] = Property("integer", "Comandi indipendenti eseguiti in parallelo (1-64)."),
                },
            },
        },
    };

    private static JsonObject EnvelopeSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["command"] = Property("string", "Comando eseguito."),
            ["status"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("ok", "error", "timeout", "skipped") },
            ["durationMs"] = Property("integer", "Durata in millisecondi."),
            ["pages"] = Property("integer", "Pagine lette da Orchestrator."),
            ["truncated"] = Property("boolean", "True se i dati sono stati limitati da maxItems, maxPages, maxJoinRows o timeout."),
            ["pushdown"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["description"] = "Parametri OData inviati al server." },
            ["warnings"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["error"] = new JsonObject { ["type"] = new JsonArray("object", "null"), ["description"] = "{ code, exitCode, message, commandId }" },
            ["data"] = new JsonObject { ["description"] = "Payload di Orchestrator (stessa struttura dell'originale)." },
        },
    };

    private static JsonObject ExitCodeTable() => new()
    {
        [ExitCodes.Success.ToString(System.Globalization.CultureInfo.InvariantCulture)] = "Successo",
        ["1"] = "Errore non classificato",
        ["2"] = "Uso o configurazione non validi",
        ["3"] = "Autenticazione fallita",
        ["4"] = "Accesso negato o risorsa non trovata",
        ["5"] = "Errore di parsing o semantico della query",
        ["6"] = "Timeout globale superato",
        ["7"] = "Batch con successo parziale",
        ["8"] = "Orchestrator non raggiungibile o 5xx dopo i retry",
        ["130"] = "Annullato dall'utente",
    };
}
