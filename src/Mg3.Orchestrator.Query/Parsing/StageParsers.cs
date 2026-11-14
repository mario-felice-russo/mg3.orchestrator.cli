using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Query;

namespace Mg3.Orchestrator.Query.Parsing;

public sealed class WhereStageParser : IStageParser
{
    public string Keyword => "where";

    public StageNode Parse(TokenCursor stream, int position) => new WhereStage(ExpressionParser.Parse(stream), position);
}

public sealed class SelectStageParser : IStageParser
{
    public string Keyword => "select";

    public StageNode Parse(TokenCursor stream, int position)
    {
        var projections = new List<Projection>();
        do
        {
            if (stream.Current.Kind == TokenKind.Identifier && stream.Peek().Kind == TokenKind.Assign)
            {
                var alias = stream.Advance().Text;
                stream.Advance();
                projections.Add(new Projection(ExpressionParser.Parse(stream), alias));
                continue;
            }

            var expression = ExpressionParser.Parse(stream);
            var name = stream.MatchKeyword("as") ? stream.ExpectIdentifier("un alias dopo 'as'") : null;
            projections.Add(new Projection(expression, name));
        }
        while (stream.Match(TokenKind.Comma));

        return new SelectStage(projections, position);
    }
}

public sealed class OrderByStageParser : IStageParser
{
    public string Keyword => "orderby";

    public StageNode Parse(TokenCursor stream, int position)
    {
        var keys = new List<OrderKey>();
        do
        {
            var expression = ExpressionParser.Parse(stream);
            var descending = stream.MatchKeyword("desc");
            if (!descending)
            {
                stream.MatchKeyword("asc");
            }

            keys.Add(new OrderKey(expression, descending));
        }
        while (stream.Match(TokenKind.Comma));

        return new OrderByStage(keys, position);
    }
}

public sealed class TakeStageParser : IStageParser
{
    public string Keyword => "take";

    public StageNode Parse(TokenCursor stream, int position) => new TakeStage(stream.ExpectInteger("un intero dopo 'take'"), position);
}

public sealed class SkipStageParser : IStageParser
{
    public string Keyword => "skip";

    public StageNode Parse(TokenCursor stream, int position) => new SkipStage(stream.ExpectInteger("un intero dopo 'skip'"), position);
}

public sealed class DistinctStageParser : IStageParser
{
    public string Keyword => "distinct";

    public StageNode Parse(TokenCursor stream, int position)
    {
        var fields = new List<FieldExpr>();
        if (stream.Current.Kind == TokenKind.Identifier)
        {
            do
            {
                fields.Add(ExpressionParser.ParseField(stream));
            }
            while (stream.Match(TokenKind.Comma));
        }

        return new DistinctStage(fields, position);
    }
}

public sealed class GroupByStageParser : IStageParser
{
    public string Keyword => "groupby";

    public StageNode Parse(TokenCursor stream, int position)
    {
        var fields = new List<FieldExpr>();
        do
        {
            fields.Add(ExpressionParser.ParseField(stream));
        }
        while (stream.Match(TokenKind.Comma));

        return new GroupByStage(fields, position);
    }
}

public sealed class AggregateStageParser : IStageParser
{
    private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase) { "count", "sum", "avg", "min", "max", "countif" };

    public string Keyword => "aggregate";

    public StageNode Parse(TokenCursor stream, int position)
    {
        var aggregates = new List<AggregateDefinition>();
        do
        {
            var name = stream.ExpectIdentifier("il nome dell'aggregato");
            stream.Expect(TokenKind.Assign, "'='");
            var functionToken = stream.Expect(TokenKind.Identifier, "una funzione di aggregazione");
            if (!Functions.Contains(functionToken.Text))
            {
                throw new QueryException($"Funzione di aggregazione sconosciuta '{functionToken.Text}': attese {string.Join(", ", Functions)}", functionToken.Position);
            }

            var function = functionToken.Text.ToLowerInvariant();
            stream.Expect(TokenKind.LeftParen, "'('");
            Expr? argument = null;
            if (stream.Current.Kind != TokenKind.RightParen)
            {
                argument = ExpressionParser.Parse(stream);
            }

            stream.Expect(TokenKind.RightParen, "')'");
            if (argument is null && function is not "count")
            {
                throw new QueryException($"La funzione '{function}' richiede un argomento", functionToken.Position);
            }

            aggregates.Add(new AggregateDefinition(name, function, argument));
        }
        while (stream.Match(TokenKind.Comma));

        return new AggregateStage(aggregates, position);
    }
}

public sealed class JoinStageParser : IStageParser
{
    public string Keyword => "join";

    public StageNode Parse(TokenCursor stream, int position) => ParseJoin(stream, position, left: false);

    internal static JoinStage ParseJoin(TokenCursor stream, int position, bool left)
    {
        var source = SourceParser.Parse(stream, requireAlias: true);
        stream.ExpectKeyword("on");
        var leftKey = ExpressionParser.ParseField(stream);
        stream.Expect(TokenKind.EqualEqual, "'=='");
        var rightKey = ExpressionParser.ParseField(stream);
        return new JoinStage(left, source, source.Alias!, leftKey, rightKey, position);
    }
}

public sealed class LeftJoinStageParser : IStageParser
{
    public string Keyword => "left";

    public StageNode Parse(TokenCursor stream, int position)
    {
        stream.ExpectKeyword("join");
        return JoinStageParser.ParseJoin(stream, position, left: true);
    }
}

public sealed class FlattenStageParser : IStageParser
{
    public string Keyword => "flatten";

    public StageNode Parse(TokenCursor stream, int position) => new FlattenStage(ExpressionParser.ParseField(stream), position);
}

public sealed class CountStageParser : IStageParser
{
    public string Keyword => "count";

    public StageNode Parse(TokenCursor stream, int position) => new CountStage(position);
}

/// <summary>Sorgente: nome di risorsa (anche di più parole) oppure <c>$id</c>, con alias opzionale.</summary>
public static class SourceParser
{
    private static readonly HashSet<string> Terminators = new(StringComparer.OrdinalIgnoreCase) { "as", "on" };

    public static SourceNode Parse(TokenCursor stream, bool requireAlias)
    {
        var position = stream.Current.Position;
        SourceNode source;
        if (stream.Match(TokenKind.Dollar))
        {
            source = new SourceNode(stream.ExpectIdentifier("l'id di un comando dopo '$'"), true, null, position);
        }
        else
        {
            var words = new List<string> { stream.ExpectIdentifier("il nome di una risorsa o '$id'") };
            while (stream.Current.Kind == TokenKind.Identifier && !Terminators.Contains(stream.Current.Text))
            {
                words.Add(stream.Advance().Text);
            }

            source = new SourceNode(string.Join(' ', words), false, null, position);
        }

        if (stream.MatchKeyword("as"))
        {
            return source with { Alias = stream.ExpectIdentifier("un alias dopo 'as'") };
        }

        return requireAlias ? throw stream.Error("'as' seguito da un alias") : source;
    }
}
