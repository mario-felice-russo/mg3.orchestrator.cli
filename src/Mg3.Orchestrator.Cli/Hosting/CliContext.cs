using System.CommandLine;
using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Abstractions.Output;
using Mg3.Orchestrator.Core;
using Mg3.Orchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Mg3.Orchestrator.Cli.Hosting;

/// <summary>Composition root per un'invocazione: carica il profilo, costruisce il container, scrive output ed errori.</summary>
public sealed class CliContext(CliEnvironment environment, GlobalOptions options, ParseResult parseResult)
{
    public CliEnvironment Environment { get; } = environment;

    public GlobalOptions Options { get; } = options;

    public ParseResult ParseResult { get; } = parseResult;

    public Mg3Profile LoadProfile(JsonObject? requestOverrides = null, bool validate = true) =>
        new ProfileLoader().Load(
            new ConfigurationSources(
                ParseResult.GetValue(Options.Config),
                ParseResult.GetValue(Options.Profile),
                Environment.Variables,
                Environment.WorkingDirectory,
                Options.ToOverrides(ParseResult),
                requestOverrides),
            validate);

    public ServiceProvider BuildServices(Mg3Profile profile)
    {
        var level = Options.LogLevel(ParseResult);
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

    public BatchOptions BatchOptions(BatchOptions? fromRequest = null) => (fromRequest ?? new BatchOptions()) with
    {
        ExplainOnly = ParseResult.GetValue(Options.Explain),
        PushdownEnabled = !ParseResult.GetValue(Options.NoPushdown),
    };

    public int WriteResult(IServiceProvider services, BatchResult result, OutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(result);
        var formatter = services.GetServices<IOutputFormatter>().First(f => f.Format == settings.Format);
        formatter.Write(Environment.Out, result, settings);
        var quiet = Options.LogLevel(ParseResult) == LogLevel.None;
        foreach (var command in result.Results)
        {
            if (!quiet)
            {
                foreach (var warning in command.Warnings)
                {
                    Environment.Error.WriteLine($"AVVISO [{command.Id}]: {warning}");
                }
            }

            if (command.Error is { } error)
            {
                Environment.Error.WriteLine($"ERRORE [{command.Id}] {error.Code} (exit {error.ExitCode}): {error.Message}");
            }
        }

        return result.ExitCode;
    }

    public int WriteError(Mg3Exception exception, OutputSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var output = settings ?? SafeFallbackOutput();
        var info = new ErrorInfo(exception.ExitCode, exception.Code, exception.Message, null);
        IOutputFormatter formatter = output.Format == OutputFormat.Json
            ? new Output.Json.JsonOutputFormatter()
            : new Output.Text.TextOutputFormatter([]);
        formatter.WriteError(Environment.Out, info, output);
        Environment.Error.WriteLine($"ERRORE {exception.Code} (exit {exception.ExitCode}): {exception.Message}");
        return exception.ExitCode;
    }

    private OutputSettings SafeFallbackOutput()
    {
        try
        {
            return Options.FallbackOutput(ParseResult);
        }
        catch (InvalidOperationException)
        {
            return new OutputSettings();
        }
    }
}
