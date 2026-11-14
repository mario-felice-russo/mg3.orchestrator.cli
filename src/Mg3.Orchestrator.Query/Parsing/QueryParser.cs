using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Query;

namespace Mg3.Orchestrator.Query.Parsing;

/// <summary>Parser della DSL. Gli stadi sono forniti come strategie registrate (<see cref="IStageParser"/>).</summary>
public sealed class QueryParser : IQueryParser
{
    private readonly Dictionary<string, IStageParser> _stages;

    public QueryParser(IEnumerable<IStageParser> stageParsers)
    {
        ArgumentNullException.ThrowIfNull(stageParsers);
        _stages = stageParsers.ToDictionary(p => p.Keyword, StringComparer.OrdinalIgnoreCase);
    }

    public QueryAst Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var stream = new TokenCursor(Lexer.Tokenize(text));
        if (stream.Current.Kind == TokenKind.End)
        {
            throw new QueryException("La query è vuota", 0);
        }

        SourceNode? source = null;
        if (stream.MatchKeyword("from"))
        {
            source = SourceParser.Parse(stream, requireAlias: false);
            if (stream.Current.Kind != TokenKind.End)
            {
                stream.Expect(TokenKind.Pipe, "'|' oppure la fine della query");
            }
        }

        var stages = new List<StageNode>();
        while (stream.Current.Kind != TokenKind.End)
        {
            stages.Add(ParseStage(stream));
            if (stream.Current.Kind != TokenKind.End)
            {
                stream.Expect(TokenKind.Pipe, "'|' oppure la fine della query");
            }
        }

        return new QueryAst(source, stages);
    }

    private StageNode ParseStage(TokenCursor stream)
    {
        var token = stream.Current;
        if (token.Kind != TokenKind.Identifier || !_stages.TryGetValue(token.Text, out var parser))
        {
            throw stream.Error($"uno stadio ({string.Join(", ", _stages.Keys.Order(StringComparer.Ordinal))})");
        }

        stream.Advance();
        return parser.Parse(stream, token.Position);
    }
}
