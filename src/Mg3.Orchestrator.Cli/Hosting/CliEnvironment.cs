namespace Mg3.Orchestrator.Cli.Hosting;

/// <summary>Ambiente di esecuzione della CLI, iniettabile nei test.</summary>
public sealed record CliEnvironment(TextWriter Out, TextWriter Error, TextReader In, string WorkingDirectory)
{
    public static CliEnvironment FromProcess() =>
        new(Console.Out, Console.Error, Console.In, Directory.GetCurrentDirectory());
}
