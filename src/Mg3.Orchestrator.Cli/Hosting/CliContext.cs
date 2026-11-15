using System.CommandLine;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Abstractions.Output;
using Mg3.Orchestrator.Core;
using Mg3.Orchestrator.Infrastructure.Configuration;
using Mg3.Orchestrator.Output.Json;
using Mg3.Orchestrator.Output.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Mg3.Orchestrator.Cli.Hosting;

/// <summary>Composition root per un'invocazione: carica il file di configurazione, costruisce il container, scrive output ed errori.</summary>
public sealed class CliContext(CliEnvironment environment, GlobalOptions options, ParseResult parseResult)
{
    private Mg3Profile? _lenient;

    public CliEnvironment Environment { get; } = environment;

    public GlobalOptions Options { get; } = options;

    public ParseResult ParseResult { get; } = parseResult;

    public string? ConfigPath => ParseResult.GetValue(Options.Config);

    public Mg3Profile LoadProfile() => new ConfigFileLoader().Load(ConfigPath, Environment.WorkingDirectory);

    /// <summary>Profilo senza validazione, per i comandi che non contattano Orchestrator e per formattare gli errori.</summary>
    public Mg3Profile LenientProfile()
    {
        if (_lenient is not null)
        {
            return _lenient;
        }

        try
        {
            _lenient = ConfigPath is null ? new Mg3Profile() : new ConfigFileLoader().Load(ConfigPath, Environment.WorkingDirectory, validate: false);
        }
        catch (ConfigurationException)
        {
            _lenient = new Mg3Profile();
        }

        return _lenient;
    }

    public static ServiceProvider BuildServices(Mg3Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var level = profile.Verbosity switch
        {
            LogVerbosity.Detailed => LogLevel.Information,
            LogVerbosity.Diagnostic => LogLevel.Debug,
            _ => LogLevel.None,
        };
        var services = new ServiceCollection();
        services.AddMg3Orchestrator(profile);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(level);
            if (level != LogLevel.None)
            {
                builder.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
                builder.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.ColorBehavior = LoggerColorBehavior.Disabled;
                });
            }
        });
        return services.BuildServiceProvider();
    }

    public BatchOptions BatchOptions(BatchOptions? fromRequest = null) =>
        (fromRequest ?? new BatchOptions()) with { ExplainOnly = ParseResult.GetValue(Options.Explain) };

    /// <summary>Scrive il risultato su stdout; gli avvisi vanno su stderr (salvo verbosity quiet).</summary>
    public int WriteResult(IServiceProvider services, BatchResult result, Mg3Profile profile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(profile);
        var formatter = services.GetServices<IOutputFormatter>().First(f => f.Format == profile.Output.Format);
        formatter.Write(Environment.Out, result, profile.Output);
        if (profile.Verbosity != LogVerbosity.Quiet)
        {
            foreach (var command in result.Results)
            {
                foreach (var warning in command.Warnings)
                {
                    Environment.Error.WriteLine($"AVVISO [{command.Id}]: {warning}");
                }
            }
        }

        return result.ExitCode;
    }

    /// <summary>Scrive l'errore una sola volta, su stdout, nel formato configurato.</summary>
    public int WriteError(Mg3Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var profile = LenientProfile();
        var info = new ErrorInfo(exception.ExitCode, exception.Code, exception.Message, null);
        IOutputFormatter formatter = profile.Output.Format == OutputFormat.Json ? new JsonOutputFormatter() : new TextOutputFormatter([]);
        formatter.WriteError(Environment.Out, info, profile.Output);
        if (profile.Verbosity >= LogVerbosity.Detailed && exception.InnerException is { } inner)
        {
            Environment.Error.WriteLine($"Dettaglio: {inner.GetType().Name}: {inner.Message}");
        }

        return exception.ExitCode;
    }
}
