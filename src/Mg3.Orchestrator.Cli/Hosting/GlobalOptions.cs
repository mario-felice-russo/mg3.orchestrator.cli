using System.CommandLine;
using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Microsoft.Extensions.Logging;

namespace Mg3.Orchestrator.Cli.Hosting;

/// <summary>Opzioni comuni a tutti i comandi (ricorsive sulla radice).</summary>
public sealed class GlobalOptions
{
    public Option<string?> Config { get; } = new("--config") { Description = "Percorso del file di configurazione JSON.", Recursive = true };
    public Option<string?> Profile { get; } = new("--profile") { Description = "Profilo da usare (default: 'default').", Recursive = true };
    public Option<string?> Folder { get; } = new("--folder") { Description = "Cartella: id numerico, key (GUID) o percorso.", Recursive = true };
    public Option<string?> Output { get; } = new("--output", "-o") { Description = "Formato di output: text | json.", Recursive = true };
    public Option<string?> TextStyle { get; } = new("--text-style") { Description = "Stile del testo: table | list | markdown | csv.", Recursive = true };
    public Option<bool> Envelope { get; } = new("--envelope") { Description = "Incapsula ogni risultato con metadati (JSON).", Recursive = true };
    public Option<string?> Verbosity { get; } = new("--verbosity") { Description = "quiet | normal | detailed | diagnostic (log su stderr).", Recursive = true };
    public Option<bool> Explain { get; } = new("--explain") { Description = "Mostra il piano di esecuzione senza eseguire nulla.", Recursive = true };
    public Option<bool> NoPushdown { get; } = new("--no-pushdown") { Description = "Esegue l'intera query in memoria, senza tradurla in OData.", Recursive = true };

    public IEnumerable<Option> All => [Config, Profile, Folder, Output, TextStyle, Envelope, Verbosity, Explain, NoPushdown];

    /// <summary>Strato di configurazione derivato dalla riga di comando.</summary>
    public JsonObject ToOverrides(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var overrides = new JsonObject();
        if (result.GetValue(Folder) is { } folder)
        {
            var parsed = FolderSettings.Parse(folder);
            overrides["orchestrator"] = new JsonObject
            {
                ["folder"] = new JsonObject { ["path"] = parsed.Path, ["id"] = parsed.Id, ["key"] = parsed.Key },
            };
        }

        var output = new JsonObject();
        if (result.GetValue(Output) is { } format)
        {
            output["format"] = ParseEnum<OutputFormat>(format, "--output").ToString();
        }

        if (result.GetValue(TextStyle) is { } style)
        {
            output["textStyle"] = ParseEnum<Abstractions.Configuration.TextStyle>(style, "--text-style").ToString();
        }

        if (result.GetValue(Envelope))
        {
            output["envelope"] = true;
        }

        if (output.Count > 0)
        {
            overrides["output"] = output;
        }

        return overrides;
    }

    /// <summary>Impostazioni di output usabili anche quando la configurazione non è caricabile.</summary>
    public OutputSettings FallbackOutput(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var settings = new OutputSettings();
        var format = result.GetValue(Output);
        if (format is not null && Enum.TryParse<OutputFormat>(format, true, out var f))
        {
            settings = settings with { Format = f };
        }

        var style = result.GetValue(TextStyle);
        if (style is not null && Enum.TryParse<Abstractions.Configuration.TextStyle>(style, true, out var s))
        {
            settings = settings with { TextStyle = s };
        }

        return settings with { Envelope = result.GetValue(Envelope) };
    }

    public LogLevel LogLevel(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return (result.GetValue(Verbosity) ?? "normal").ToLowerInvariant() switch
        {
            "quiet" => Microsoft.Extensions.Logging.LogLevel.None,
            "normal" => Microsoft.Extensions.Logging.LogLevel.Warning,
            "detailed" => Microsoft.Extensions.Logging.LogLevel.Information,
            "diagnostic" => Microsoft.Extensions.Logging.LogLevel.Debug,
            var other => throw new ConfigurationException($"--verbosity non valido '{other}': usare quiet, normal, detailed o diagnostic."),
        };
    }

    private static T ParseEnum<T>(string value, string option)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ConfigurationException($"{option} non valido '{value}': valori ammessi {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}.");
}
