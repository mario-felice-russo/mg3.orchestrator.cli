namespace Mg3.Orchestrator.Abstractions.Execution;

/// <summary>Punto d'ingresso per l'uso come libreria.</summary>
public interface IOrchestratorQueryService
{
    Task<BatchResult> ExecuteAsync(BatchRequest request, CancellationToken cancellationToken);
}

/// <summary>Gestore generico di un comando di servizio.</summary>
public interface ICommandHandler<in TRequest, TResult>
{
    Task<TResult> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
