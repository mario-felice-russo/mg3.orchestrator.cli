using Mg3.Orchestrator.Abstractions.Query;

namespace Mg3.Orchestrator.Query.Parsing;

/// <summary>Parser di uno stadio della pipeline, identificato dalla parola chiave iniziale.</summary>
public interface IStageParser
{
    string Keyword { get; }

    /// <summary>Analizza lo stadio; la parola chiave è già stata consumata.</summary>
    StageNode Parse(TokenCursor stream, int position);
}
