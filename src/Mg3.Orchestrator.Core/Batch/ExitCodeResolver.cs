using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Execution;

namespace Mg3.Orchestrator.Core.Batch;

/// <summary>Calcola l'exit code complessivo secondo la tabella del README.</summary>
public static class ExitCodeResolver
{
    public static int Resolve(IReadOnlyList<CommandResult> results, bool isBatch, bool stopOnError)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Any(r => r.Status == CommandStatus.Timeout))
        {
            return ExitCodes.Timeout;
        }

        var failed = results.Where(r => !r.IsSuccess).ToList();
        if (failed.Count == 0)
        {
            return ExitCodes.Success;
        }

        var firstReal = failed.FirstOrDefault(r => r.Status == CommandStatus.Error) ?? failed[0];
        var firstCode = firstReal.Error?.ExitCode ?? ExitCodes.Generic;
        if (!isBatch || stopOnError)
        {
            return firstCode;
        }

        return results.Any(r => r.IsSuccess) ? ExitCodes.PartialSuccess : firstCode;
    }
}
