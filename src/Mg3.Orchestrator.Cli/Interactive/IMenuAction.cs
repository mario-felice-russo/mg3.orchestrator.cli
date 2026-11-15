namespace Mg3.Orchestrator.Cli.Interactive;

/// <summary>Voce del menu interattivo. Per aggiungerne una basta una nuova classe nell'elenco di <see cref="InteractiveMenu"/>.</summary>
public interface IMenuAction
{
    string Key { get; }

    string Title { get; }

    Task RunAsync(MenuSession session, CancellationToken cancellationToken);
}
