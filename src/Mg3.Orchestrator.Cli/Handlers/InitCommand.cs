using System.Text.Json;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Cli.Hosting;
using Mg3.Orchestrator.Infrastructure.Configuration;

namespace Mg3.Orchestrator.Cli.Handlers;

/// <summary>Valori forniti da riga di comando a <c>mg3orch init</c>; quelli mancanti vengono chiesti.</summary>
public sealed record InitOptions(
    string? Url,
    string? Token,
    string? ClientId,
    string? ClientSecret,
    string? Scopes,
    string? Username,
    string? Password,
    string? TenantName,
    long? FolderId,
    string? FolderPath,
    bool Force);

/// <summary>Crea il file di configurazione. È l'unico comando che può fare domande; senza input interattivo usa solo le opzioni.</summary>
public static class InitCommand
{
    public const string DefaultScopes = "OR.Jobs.Read OR.Folders.Read OR.Queues.Read OR.Robots.Read OR.Machines.Read OR.Assets.Read OR.Execution.Read OR.Monitoring.Read";

    public static int Run(CliContext context, InitOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        var path = context.ConfigPath ?? throw new ConfigurationException("Indica dove creare il file: mg3orch init --config <percorso>.");
        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(context.Environment.WorkingDirectory, path);
        if (File.Exists(fullPath) && !options.Force)
        {
            throw new ConfigurationException($"Il file '{path}' esiste già. Usa --force per sovrascriverlo.");
        }

        var prompt = new Prompter(context.Environment);
        var url = OrchestratorUrl.Parse(options.Url ?? prompt.Ask("URL di Orchestrator (puoi incollare quello del browser)", "--url"));
        var file = new ConfigFile
        {
            Url = url.BaseUrl,
            FolderId = options.FolderId ?? (options.FolderPath is null ? url.FolderId ?? prompt.AskLong("Id della cartella (il valore 'fid' nell'URL; vuoto per nessuna)") : null),
            FolderPath = options.FolderPath,
            TenantName = options.TenantName,
        };
        file = WithCredentials(file, options, url, prompt);

        var profile = ConfigFileMapper.ToProfile(file);
        var validation = new Mg3ProfileValidator().Validate(null, profile);
        if (validation.Failed)
        {
            throw new ConfigurationException("Configurazione non valida: " + string.Join(" ", validation.Failures));
        }

        Write(fullPath, file);
        context.Environment.Out.WriteLine($"Configurazione scritta in {path}");
        context.Environment.Out.WriteLine($"Verifica con: mg3orch auth test --config {path}");
        return ExitCodes.Success;
    }

    private static ConfigFile WithCredentials(ConfigFile file, InitOptions o, OrchestratorUrl url, Prompter prompt)
    {
        if (o.Token is not null)
        {
            return file with { Token = o.Token };
        }

        if (o.ClientId is not null || o.ClientSecret is not null)
        {
            return file with { ClientId = o.ClientId ?? prompt.Ask("Client id", "--client-id"), ClientSecret = o.ClientSecret ?? prompt.Ask("Client secret", "--client-secret"), Scopes = o.Scopes ?? DefaultScopes };
        }

        if (o.Username is not null || o.Password is not null)
        {
            return file with { Username = o.Username ?? prompt.Ask("Utente", "--username"), Password = o.Password ?? prompt.Ask("Password", "--password") };
        }

        var choice = prompt.AskChoice("Tipo di credenziale: 1) Personal Access Token  2) Applicazione esterna (client id/secret)" + (url.IsCloud ? string.Empty : "  3) Utente e password"), url.IsCloud ? 2 : 3);
        return choice switch
        {
            1 => file with { Token = prompt.Ask("Personal Access Token", "--token") },
            2 => file with
            {
                ClientId = prompt.Ask("Client id", "--client-id"),
                ClientSecret = prompt.Ask("Client secret", "--client-secret"),
                Scopes = prompt.AskOptional($"Scope (invio per: {DefaultScopes})") ?? DefaultScopes,
            },
            _ => file with
            {
                Username = prompt.Ask("Utente", "--username"),
                Password = prompt.Ask("Password", "--password"),
                TenantName = file.TenantName ?? prompt.AskOptional("Tenant (invio per 'Default')"),
            },
        };
    }

    private static void Write(string path, ConfigFile file)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(file, ConfigurationJson.WriteOptions) + "\n";
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, json);
            return;
        }

        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite };
        using (var stream = new FileStream(path, options))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>Domande su stdin/stdout. Se l'input termina, indica quale opzione usare.</summary>
    private sealed class Prompter(CliEnvironment environment)
    {
        public string Ask(string question, string option)
        {
            var answer = AskOptional(question);
            return answer ?? throw new ConfigurationException($"Valore mancante per '{question}': indicalo con {option}.");
        }

        public string? AskOptional(string question)
        {
            environment.Out.Write(question + ": ");
            var line = environment.In.ReadLine();
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }

        public long? AskLong(string question)
        {
            var answer = AskOptional(question);
            return answer is null ? null
                : long.TryParse(answer, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value
                : throw new ConfigurationException($"'{answer}' non è un id di cartella valido.");
        }

        public int AskChoice(string question, int max)
        {
            var answer = AskOptional(question + " [1]") ?? "1";
            return int.TryParse(answer, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 1 && value <= max
                ? value
                : throw new ConfigurationException($"Scelta non valida '{answer}': indica un numero da 1 a {max}.");
        }
    }
}
