using System.Collections;

namespace Mg3.Orchestrator.Cli.Hosting;

/// <summary>Ambiente di esecuzione della CLI, iniettabile nei test.</summary>
public sealed record CliEnvironment(
    TextWriter Out,
    TextWriter Error,
    TextReader In,
    IReadOnlyDictionary<string, string> Variables,
    string WorkingDirectory)
{
    public static CliEnvironment FromProcess()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                variables[key] = value;
            }
        }

        return new CliEnvironment(Console.Out, Console.Error, Console.In, variables, Directory.GetCurrentDirectory());
    }
}
