namespace Mg3.Orchestrator.Abstractions.Configuration;

/// <summary>Risolve riferimenti a segreti nella forma <c>env:NOME</c> o <c>file:percorso</c>.</summary>
public interface ISecretResolver
{
    string? Resolve(string? value);
}
