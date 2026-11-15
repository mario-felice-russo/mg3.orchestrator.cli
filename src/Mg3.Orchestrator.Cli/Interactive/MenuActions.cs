using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Output.Text;

namespace Mg3.Orchestrator.Cli.Interactive;

/// <summary>Query comuni alle voci sui job in errore.</summary>
internal static class FaultedJobs
{
    public static string Filter(MenuSession session) => $"where State == 'Faulted' and EndTime >= now() - {session.Since}";

    public static string ListQuery(MenuSession session) =>
        $"{Filter(session)} | orderby EndTime desc | select JobKey = Key, Fine = EndTime, Processo = ReleaseName, VM = HostMachineName, Messaggio = Info";

    public static string None(MenuSession session) => $"Nessun job in errore nelle ultime {session.Since}.";
}

public sealed class FaultedJobsAction : IMenuAction
{
    public string Key => "1";

    public string Title => "Job in errore: messaggio e VM";

    public async Task RunAsync(MenuSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = await session.RunAsync("jobs list", FaultedJobs.ListQuery(session), cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return;
        }

        var keys = new List<string>();
        var rows = new List<JsonNode?>();
        foreach (var row in result.Rows.OfType<JsonObject>())
        {
            keys.Add(row["JobKey"]?.GetValue<string>() ?? string.Empty);
            var numbered = new JsonObject { ["#"] = keys.Count };
            foreach (var (name, value) in row.Where(kv => kv.Key != "JobKey"))
            {
                numbered[name] = value?.DeepClone();
            }

            rows.Add(numbered);
        }

        session.LastFaultedKeys = keys;
        session.Show($"Job in errore nelle ultime {session.Since}", rows, FaultedJobs.None(session), result.Truncated);
    }
}

/// <summary>Riepilogo dei job in errore raggruppati per i campi indicati.</summary>
public abstract class FaultedSummaryAction(string key, string title, string groupBy, string columns) : IMenuAction
{
    public string Key => key;

    public string Title => title;

    public async Task RunAsync(MenuSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var query = $"{FaultedJobs.Filter(session)} | groupby {groupBy} | orderby count desc | select {columns}, Errori = count";
        var result = await session.RunAsync("jobs list", query, cancellationToken).ConfigureAwait(false);
        if (result is not null)
        {
            session.Show($"{title} (ultime {session.Since})", result.Rows, FaultedJobs.None(session), result.Truncated);
        }
    }
}

public sealed class SummaryByProcessAndVmAction()
    : FaultedSummaryAction("2", "Riepilogo per processo e VM", "ReleaseName, HostMachineName", "Processo = ReleaseName, VM = HostMachineName");

public sealed class SummaryByVmAction()
    : FaultedSummaryAction("3", "Riepilogo per VM", "HostMachineName", "VM = HostMachineName");

/// <summary>Messaggi di livello Error/Fatal scritti dai robot; facoltativamente di un solo job.</summary>
public class RobotErrorsAction : IMenuAction
{
    public virtual string Key => "4";

    public virtual string Title => "Messaggi di errore dei robot (log Error/Fatal)";

    public virtual Task RunAsync(MenuSession session, CancellationToken cancellationToken) => ShowAsync(session, null, cancellationToken);

    protected static async Task ShowAsync(MenuSession session, string? jobKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var filter = $"Level in ('Error', 'Fatal') and TimeStamp >= now() - {session.Since}";
        if (jobKey is not null)
        {
            filter = $"JobKey == '{jobKey}' and {filter}";
        }

        var query = $"where {filter} | orderby TimeStamp desc | select Ora = TimeStamp, Processo = ProcessName, VM = HostMachineName, Livello = Level, Messaggio = Message";
        var result = await session.RunAsync("robotlogs list", query, cancellationToken).ConfigureAwait(false);
        if (result is not null)
        {
            session.Show($"Messaggi di errore dei robot (ultime {session.Since})", result.Rows, $"Nessun messaggio di errore dei robot nelle ultime {session.Since}.", result.Truncated);
        }
    }
}

public sealed class JobErrorsAction : RobotErrorsAction
{
    public override string Key => "5";

    public override string Title => "Messaggi di errore di un singolo job";

    public override Task RunAsync(MenuSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var answer = session.Ask("Numero del job (dall'elenco della voce 1) oppure la sua Key");
        if (string.IsNullOrEmpty(answer))
        {
            return Task.CompletedTask;
        }

        var key = answer;
        if (int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            if (number < 1 || number > session.LastFaultedKeys.Count)
            {
                session.Out.WriteLine("Numero non valido: esegui prima la voce 1.");
                return Task.CompletedTask;
            }

            key = session.LastFaultedKeys[number - 1];
        }

        if (!Guid.TryParse(key, out var guid))
        {
            session.Out.WriteLine($"'{key}' non è una Key valida.");
            return Task.CompletedTask;
        }

        return ShowAsync(session, guid.ToString("D"), cancellationToken);
    }
}

public sealed class ExportFaultedJobsAction : IMenuAction
{
    public string Key => "6";

    public string Title => "Esporta i job in errore in CSV";

    public async Task RunAsync(MenuSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = await session.RunAsync("jobs list", FaultedJobs.ListQuery(session), cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return;
        }

        if (result.Rows.Count == 0)
        {
            session.Out.WriteLine(FaultedJobs.None(session) + " Nessun file creato.");
            return;
        }

        var file = Path.Combine(session.Environment.WorkingDirectory, $"mg3orch-job-in-errore-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        var settings = session.Profile.Output with { Format = OutputFormat.Text, TextStyle = TextStyle.Csv };
        await using (var writer = new StreamWriter(file, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        {
            new TextOutputFormatter([new CsvRenderer()]).Write(writer, new BatchResult([result], IsBatch: false, 0), settings);
        }

        session.Out.WriteLine($"Esportati {result.Rows.Count} job in {file}");
    }
}

public sealed partial class ChangePeriodAction : IMenuAction
{
    public string Key => "7";

    public string Title => "Cambia periodo";

    [GeneratedRegex(@"^\d+(m|h|d|w)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Duration();

    public Task RunAsync(MenuSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var answer = session.Ask($"Nuovo periodo (es. 12h, 24h, 7d) [attuale: {session.Since}]");
        if (string.IsNullOrEmpty(answer))
        {
            return Task.CompletedTask;
        }

        if (!Duration().IsMatch(answer))
        {
            session.Out.WriteLine($"Periodo non valido '{answer}': usa un numero seguito da m, h, d o w (es. 24h).");
            return Task.CompletedTask;
        }

        session.Since = answer;
        session.LastFaultedKeys = [];
        return Task.CompletedTask;
    }
}

public sealed class HelpAction : IMenuAction
{
    public string Key => "8";

    public string Title => "Aiuto";

    public Task RunAsync(MenuSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Out.WriteLine();
        session.Out.WriteLine(HelpText.Text);
        return Task.CompletedTask;
    }
}
