using Mg3.Orchestrator.Abstractions.Execution;

namespace Mg3.Orchestrator.Core.Batch;

/// <summary>Token e opzioni dell'esecuzione: distingue annullamento dell'utente, timeout globale e stop per errore.</summary>
public sealed record ExecutionScope(
    BatchOptions Options,
    CancellationToken UserToken,
    CancellationToken TimeoutToken,
    CancellationToken StopToken)
{
    public bool IsTimedOut => TimeoutToken.IsCancellationRequested && !UserToken.IsCancellationRequested;

    public bool IsStopped => StopToken.IsCancellationRequested && !TimeoutToken.IsCancellationRequested && !UserToken.IsCancellationRequested;
}
