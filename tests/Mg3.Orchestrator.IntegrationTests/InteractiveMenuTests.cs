using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Cli.Interactive;

namespace Mg3.Orchestrator.IntegrationTests;

/// <summary>Menu interattivo: mg3orch senza comandi.</summary>
public sealed class InteractiveMenuTests : IDisposable
{
    private const string FaultedJobs = """
        {"@odata.context":"ctx","@odata.count":2,"value":[
          {"Key":"00000000-0000-0000-0000-000000000001","State":"Faulted","ReleaseName":"Fatture","HostMachineName":"VM-01","EndTime":"2026-11-15T10:00:00.000Z","Info":"Timeout durante il login al portale fornitori: il sistema remoto non ha risposto entro 30 secondi"},
          {"Key":"00000000-0000-0000-0000-000000000002","State":"Faulted","ReleaseName":"Ordini","HostMachineName":"VM-02","EndTime":"2026-11-15T09:00:00.000Z","Info":"Selettore non trovato"}]}
        """;

    private const string RobotLogs = """
        {"value":[{"TimeStamp":"2026-11-15T10:00:00.000Z","ProcessName":"Fatture","HostMachineName":"VM-01","Level":"Error","Message":"Login fallito","JobKey":"00000000-0000-0000-0000-000000000001"}]}
        """;

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private Task<CliRun> Menu(string input) => _cli.RunAsync([], stdin: input, withConfig: false);

    /// <summary>Configurazione dell'harness salvata come oo-&lt;nome&gt;.json nella cartella di lavoro.</summary>
    private string ConfigureMenu(string environment = "dev")
    {
        _cli.Configure();
        var path = Path.Combine(_cli.WorkingDirectory, $"oo-{environment}.json");
        File.Copy(_cli.ConfigPath, path, overwrite: true);
        File.Delete(_cli.ConfigPath);
        return path;
    }

    [Fact]
    public async Task ShowsMenuWithoutCommandsAndExits()
    {
        ConfigureMenu();
        var run = await Menu("0\n");
        run.ExitCode.ShouldBe(ExitCodes.Success);
        run.Out.ShouldContain("=== mg3orch - processi in errore (ultime 24h) ===");
        run.Out.ShouldContain("Configurazione: oo-dev.json");
        foreach (var action in InteractiveMenu.Actions)
        {
            run.Out.ShouldContain($"  {action.Key}) {action.Title}");
        }

        run.Out.ShouldContain("  0) Esci");
    }

    [Fact]
    public async Task EndOfInputEndsTheLoop()
    {
        ConfigureMenu();
        (await Menu(string.Empty)).ExitCode.ShouldBe(ExitCodes.Success);
    }

    [Fact]
    public async Task FaultedJobsShowFullMessageAndVm()
    {
        ConfigureMenu();
        _cli.ServeRaw("odata/Jobs", FaultedJobs);
        var run = await Menu("1\n0\n");
        run.Out.ShouldContain("## Job in errore nelle ultime 24h — 2 righe");
        run.Out.ShouldContain("#  Fine                 Processo  VM     Messaggio");
        run.Out.ShouldContain("1  2026-11-15 10:00:00  Fatture   VM-01  Timeout durante il login al portale fornitori: il sistema remoto non ha risposto entro 30 secondi");
        var query = CliHarness.Query(_cli.OrchestratorRequests().Single(), "$filter");
        query.ShouldStartWith("(State eq 'Faulted') and (EndTime ge ");
    }

    [Fact]
    public async Task SummariesGroupByProcessAndVm()
    {
        ConfigureMenu();
        _cli.ServeRaw("odata/Jobs", FaultedJobs);
        var run = await Menu("2\n3\n0\n");
        run.Out.ShouldContain("Processo  VM     Errori");
        run.Out.ShouldContain("Fatture   VM-01  1");
        run.Out.ShouldContain("## Riepilogo per VM (ultime 24h) — 2 righe");
    }

    [Fact]
    public async Task RobotErrorsForAllAndForOneJob()
    {
        ConfigureMenu();
        _cli.ServeRaw("odata/Jobs", FaultedJobs);
        _cli.ServeRaw("odata/RobotLogs", RobotLogs);
        var run = await Menu("4\n5\n1\n1\n5\n1\n5\n9\n5\nnon-guid\n5\n00000000-0000-0000-0000-000000000002\n0\n");
        run.Out.ShouldContain("Ora                  Processo  VM     Livello  Messaggio");
        run.Out.ShouldContain("Numero non valido: esegui prima la voce 1.");
        run.Out.ShouldContain("'non-guid' non è una Key valida.");
        var filters = _cli.OrchestratorRequests().Where(r => CliHarness.PathOf(r).EndsWith("/RobotLogs", StringComparison.Ordinal)).Select(r => CliHarness.Query(r, "$filter")).ToList();
        filters.Count.ShouldBe(3);
        filters[1].ShouldStartWith("((JobKey eq 00000000-0000-0000-0000-000000000001)");
        filters[2].ShouldStartWith("((JobKey eq 00000000-0000-0000-0000-000000000002)");
    }

    [Fact]
    public async Task ExportsCsv()
    {
        ConfigureMenu();
        _cli.ServeRaw("odata/Jobs", FaultedJobs);
        var run = await Menu("6\n0\n");
        run.Out.ShouldContain("Esportati 2 job in ");
        var file = Directory.GetFiles(_cli.WorkingDirectory, "mg3orch-job-in-errore-*.csv").Single();
        var lines = await File.ReadAllLinesAsync(file);
        lines[0].ShouldBe("JobKey,Fine,Processo,VM,Messaggio");
        lines.Length.ShouldBe(3);
    }

    [Fact]
    public async Task EmptyResultsAndExport()
    {
        ConfigureMenu();
        _cli.ServeRaw("odata/Jobs", "{\"value\":[]}");
        _cli.ServeRaw("odata/RobotLogs", "{\"value\":[]}");
        var run = await Menu("1\n4\n6\n0\n");
        run.Out.ShouldContain("Nessun job in errore nelle ultime 24h.");
        run.Out.ShouldContain("Nessun messaggio di errore dei robot nelle ultime 24h.");
        run.Out.ShouldContain("Nessun file creato.");
    }

    [Fact]
    public async Task ChangesPeriodHelpAndInvalidChoices()
    {
        ConfigureMenu();
        _cli.ServeRaw("odata/Jobs", "{\"value\":[]}");
        var run = await Menu("7\nieri\n7\n7d\n1\n7\n\n8\nx\n0\n");
        run.Out.ShouldContain("Periodo non valido 'ieri'");
        run.Out.ShouldContain("=== mg3orch - processi in errore (ultime 7d) ===");
        run.Out.ShouldContain("Nessun job in errore nelle ultime 7d.");
        run.Out.ShouldContain(HelpText.Text.Split('\n')[0]);
        run.Out.ShouldContain("Scelta non valida: 'x'.");
    }

    [Fact]
    public async Task ErrorsAreShownAndTheLoopContinues()
    {
        ConfigureMenu();
        _cli.ServeRaw("odata/Jobs", "{}", status: 403);
        var run = await Menu("1\n0\n");
        run.ExitCode.ShouldBe(ExitCodes.Success);
        run.Out.ShouldContain("ERRORE (4 – accesso negato o risorsa non trovata)");
        run.Out.ShouldContain("  0) Esci");
    }

    [Fact]
    public async Task WithoutConfigurationRunsInitFirst()
    {
        _cli.ServeRaw("odata/Jobs", "{\"value\":[]}");
        var run = await Menu($"\n{_cli.BaseUrl}jobs?fid=42\n1\npat-menu\n1\n0\n");
        run.ExitCode.ShouldBe(ExitCodes.Success);
        run.Out.ShouldContain("Nessun file oo-*.json nella cartella");
        run.Out.ShouldContain("Configurazione scritta in oo-dev.json");
        run.Out.ShouldContain("Configurazione: oo-dev.json");
        run.Out.ShouldContain("Nessun job in errore nelle ultime 24h.");
        File.ReadAllText(Path.Combine(_cli.WorkingDirectory, "oo-dev.json")).ShouldContain("\"token\": \"pat-menu\"");
        CliHarness.Header(_cli.OrchestratorRequests().Single(), "Authorization").ShouldBe("Bearer pat-menu");
    }

    [Fact]
    public async Task WithoutConfigurationAndInputInitFails()
    {
        var run = await Menu(string.Empty);
        run.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
        run.Out.ShouldContain("Nessun file oo-*.json trovato. Crealo con: mg3orch init --config oo-dev.json");
        (await Menu("prod\n")).Out.ShouldContain("indicalo con --url");
    }

    [Fact]
    public async Task CreatesNamedEnvironmentAndRejectsInvalidNames()
    {
        _cli.ServeRaw("odata/Jobs", "{\"value\":[]}");
        var created = await Menu($"prod\n{_cli.BaseUrl}\n\n1\npat-prod\n0\n");
        created.ExitCode.ShouldBe(ExitCodes.Success);
        File.Exists(Path.Combine(_cli.WorkingDirectory, "oo-prod.json")).ShouldBeTrue();
        File.Delete(Path.Combine(_cli.WorkingDirectory, "oo-prod.json"));
        var invalid = await Menu("../x\n");
        invalid.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
        invalid.Out.ShouldContain("Nome dell'ambiente non valido '../x'");
    }

    [Fact]
    public async Task ChoosesAmongSeveralConfigurations()
    {
        ConfigureMenu("dev");
        File.Copy(Path.Combine(_cli.WorkingDirectory, "oo-dev.json"), Path.Combine(_cli.WorkingDirectory, "oo-prod.json"));
        var run = await Menu("5\n2\n0\n");
        run.Out.ShouldContain("File di configurazione trovati:\n  1) oo-dev.json\n  2) oo-prod.json".Replace("\n", Environment.NewLine, StringComparison.Ordinal));
        run.Out.ShouldContain("Scelta non valida: '5'. Indica un numero da 1 a 2.");
        run.Out.ShouldContain("Configurazione: oo-prod.json");
        (await Menu("\n0\n")).Out.ShouldContain("Configurazione: oo-dev.json");
        (await Menu(string.Empty)).ExitCode.ShouldBe(ExitCodes.Success);
    }

    [Fact]
    public async Task IgnoresFilesThatDoNotMatchThePattern()
    {
        _cli.Configure();
        var run = await Menu(string.Empty);
        run.Out.ShouldContain("Nessun file oo-*.json nella cartella");
    }

    [Fact]
    public async Task ExplicitConfigIsUsed()
    {
        _cli.Configure();
        var run = await _cli.RunAsync([], stdin: "0\n");
        run.Out.ShouldContain("Configurazione: " + _cli.ConfigPath);
    }

    [Fact]
    public async Task HelpCommandPrintsOnlineHelp()
    {
        var run = await _cli.RunAsync(["help"], withConfig: false);
        run.ExitCode.ShouldBe(ExitCodes.Success);
        run.Out.ShouldContain("MENU (mg3orch senza comandi)");
        run.Out.ShouldContain("EXIT CODE");
    }
}
