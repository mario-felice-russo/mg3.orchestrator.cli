using System.Runtime.CompilerServices;

namespace Mg3.Orchestrator.Output.UnitTests;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        VerifierSettings.UseStrictJson();
        DiffEngine.DiffRunner.Disabled = true;
    }
}
