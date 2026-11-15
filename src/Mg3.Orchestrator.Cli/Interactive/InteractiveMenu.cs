using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Abstractions.Output;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Cli.Handlers;
using Mg3.Orchestrator.Cli.Hosting;
using Mg3.Orchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mg3.Orchestrator.Cli.Interactive;

/// <summary>
/// Menu interattivo avviato da <c>mg3orch</c> senza comandi. Se manca la configurazione esegue prima <c>init</c>.
/// Termina con 0 alla voce Esci o quando l'input finisce (es. stdin rediretto e vuoto).
/// </summary>
public static class InteractiveMenu
{
    public const string DefaultConfigName = "mg3orch.json";

    public static IReadOnlyList<IMenuAction> Actions { get; } =
    [
        new FaultedJobsAction(),
        new SummaryByProcessAndVmAction(),
        new SummaryByVmAction(),
        new RobotErrorsAction(),
        new JobErrorsAction(),
        new ExportFaultedJobsAction(),
        new ChangePeriodAction(),
        new HelpAction(),
    ];

    public static async Task<int> RunAsync(CliContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var environment = context.Environment;
        var configPath = context.ConfigPath ?? FindConfig(environment);
        if (configPath is null)
        {
            environment.Out.WriteLine($"Nessun file di configurazione trovato: lo creo ora ({DefaultConfigName}).");
            InitCommand.Run(environment, new InitOptions(null, null, null, null, null, null, null, null, null, null, false), DefaultConfigName);
            configPath = DefaultConfigName;
        }

        var profile = new ConfigFileLoader().Load(configPath, environment.WorkingDirectory);
        await using var services = CliContext.BuildServices(profile);
        var session = new MenuSession(
            environment,
            services.GetRequiredService<IOrchestratorQueryService>(),
            services.GetServices<IOutputFormatter>().First(f => f.Format == OutputFormat.Text),
            profile,
            configPath);

        while (true)
        {
            WriteMenu(session);
            var choice = session.Ask("Scelta");
            if (choice is null || choice == "0")
            {
                return ExitCodes.Success;
            }

            var action = Actions.FirstOrDefault(a => a.Key == choice);
            if (action is null)
            {
                session.Out.WriteLine($"Scelta non valida: '{choice}'.");
                continue;
            }

            try
            {
                await action.RunAsync(session, cancellationToken).ConfigureAwait(false);
            }
            catch (Mg3Exception ex)
            {
                session.Out.WriteLine(Output.Text.TextOutputFormatter.ErrorLine(new ErrorInfo(ex.ExitCode, ex.Code, ex.Message, null)));
            }
        }
    }

    /// <summary>Cerca mg3orch.json nella cartella corrente e poi in quella del programma.</summary>
    public static string? FindConfig(CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return new[] { Path.Combine(environment.WorkingDirectory, DefaultConfigName), Path.Combine(AppContext.BaseDirectory, DefaultConfigName) }
            .FirstOrDefault(File.Exists);
    }

    private static void WriteMenu(MenuSession session)
    {
        session.Out.WriteLine();
        session.Out.WriteLine($"=== mg3orch - processi in errore (ultime {session.Since}) ===");
        session.Out.WriteLine($"    Configurazione: {session.ConfigPath}");
        foreach (var action in Actions)
        {
            session.Out.WriteLine($"  {action.Key}) {action.Title}");
        }

        session.Out.WriteLine("  0) Esci");
    }
}
