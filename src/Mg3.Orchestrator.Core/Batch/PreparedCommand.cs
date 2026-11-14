using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Execution;
using Mg3.Orchestrator.Abstractions.Query;
using Mg3.Orchestrator.Core.Commands;

namespace Mg3.Orchestrator.Core.Batch;

/// <summary>Comando con query già analizzata e dipendenze note. Un errore di preparazione fallisce solo questo comando.</summary>
public sealed record PreparedCommand(CommandRequest Request, QueryAst? Ast, Mg3Exception? Error, IReadOnlyList<string> Dependencies)
{
    public string Id => Request.Id;

    public string DisplayCommand => Request.Command ?? (Ast?.Source is { } s ? "from " + s.SourceKey : "query");

    public static PreparedCommand Prepare(CommandRequest request, IQueryParser parser)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(parser);
        QueryAst? ast = null;
        try
        {
            CommandArguments.Validate(request.Args, request.Id);
            if (!string.IsNullOrWhiteSpace(request.Query))
            {
                ast = parser.Parse(request.Query);
            }

            if (string.IsNullOrWhiteSpace(request.Command) && ast?.Source is null)
            {
                throw new ConfigurationException($"Comando '{request.Id}': specificare 'command' oppure una query che inizi con 'from'.");
            }

            if (!string.IsNullOrWhiteSpace(request.Command) && ast?.Source is not null)
            {
                throw new QueryException($"Comando '{request.Id}': la sorgente va indicata nel comando oppure con 'from', non in entrambi.", ast.Source.Position);
            }

            return new PreparedCommand(request, ast, null, References(ast));
        }
        catch (Mg3Exception ex)
        {
            return new PreparedCommand(request, ast, ex, ast is null ? [] : References(ast));
        }
    }

    private static string[] References(QueryAst? ast)
    {
        if (ast is null)
        {
            return [];
        }

        var refs = new List<string>();
        if (ast.Source is { IsReference: true } source)
        {
            refs.Add(source.Name);
        }

        refs.AddRange(ast.Stages.OfType<JoinStage>().Where(j => j.Right.IsReference).Select(j => j.Right.Name));
        return refs.Distinct(StringComparer.Ordinal).ToArray();
    }
}
