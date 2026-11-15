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
/// Menu interattivo avviato da <c>mg3orch</c> senza comandi. Senza <c>--config</c> cerca i file <c>oo-*.json</c>
/// nella cartella corrente (escluso <c>oo-sample.json</c>): uno viene usato, tra più file si sceglie, se non ce ne sono esegue prima <c>init</c>.
/// Termina con 0 alla voce Esci o quando l'input finisce (es. stdin rediretto e vuoto).
/// </summary>
public static partial class InteractiveMenu
{
    public const string ConfigPattern = "oo-*.json";

    /// <summary>File d'esempio del repository: non è una configurazione reale e il menu lo ignora.</summary>
    public const string SampleFile = "oo-sample.json";
    public const string DefaultEnvironment = "dev";

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_-]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial System.Text.RegularExpressions.Regex EnvironmentName();

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
        var configPath = context.ConfigPath ?? ChooseConfig(environment);
        if (configPath is null)
        {
            return ExitCodes.Success;
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

    /// <summary>File <c>oo-*.json</c> della cartella corrente, in ordine alfabetico (solo il nome del file).</summary>
    public static IReadOnlyList<string> FindConfigs(CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return Directory.GetFiles(environment.WorkingDirectory, ConfigPattern)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !string.Equals(name, SampleFile, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Sceglie il file di configurazione; null se l'input termina prima della scelta.</summary>
    private static string? ChooseConfig(CliEnvironment environment)
    {
        var files = FindConfigs(environment);
        switch (files.Count)
        {
            case 1:
                return files[0];
            case 0:
                return CreateConfig(environment);
        }

        environment.Out.WriteLine("File di configurazione trovati:");
        for (var i = 0; i < files.Count; i++)
        {
            environment.Out.WriteLine($"  {i + 1}) {files[i]}");
        }

        while (true)
        {
            environment.Out.Write("Scelta [1]: ");
            var answer = environment.In.ReadLine()?.Trim();
            if (answer is null)
            {
                return null;
            }

            if (answer.Length == 0)
            {
                return files[0];
            }

            if (int.TryParse(answer, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= files.Count)
            {
                return files[n - 1];
            }

            environment.Out.WriteLine($"Scelta non valida: '{answer}'. Indica un numero da 1 a {files.Count}.");
        }
    }

    private static string? CreateConfig(CliEnvironment environment)
    {
        environment.Out.WriteLine($"Nessun file {ConfigPattern} nella cartella {environment.WorkingDirectory}: lo creo ora.");
        environment.Out.Write($"Nome dell'ambiente, es. dev o prod [{DefaultEnvironment}]: ");
        var name = environment.In.ReadLine()?.Trim();
        if (name is null)
        {
            throw new ConfigurationException($"Nessun file {ConfigPattern} trovato. Crealo con: mg3orch init --config oo-{DefaultEnvironment}.json --url <URL> --token <PAT>");
        }

        if (name.Length == 0)
        {
            name = DefaultEnvironment;
        }

        if (!EnvironmentName().IsMatch(name))
        {
            throw new ConfigurationException($"Nome dell'ambiente non valido '{name}': usa lettere, cifre, '-' o '_'.");
        }

        var file = $"oo-{name}.json";
        InitCommand.Run(environment, new InitOptions(null, null, null, null, null, null, null, null, null, null, false), file);
        return file;
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
