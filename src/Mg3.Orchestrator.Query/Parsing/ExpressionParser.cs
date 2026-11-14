using Mg3.Orchestrator.Abstractions.Query;

namespace Mg3.Orchestrator.Query.Parsing;

/// <summary>Parser a discesa ricorsiva delle espressioni. Precedenza: or &lt; and &lt; not &lt; confronto &lt; additivo &lt; moltiplicativo &lt; unario.</summary>
public static class ExpressionParser
{
    private static readonly Dictionary<TokenKind, BinaryOperator> SymbolComparisons = new()
    {
        [TokenKind.EqualEqual] = BinaryOperator.Equal,
        [TokenKind.NotEqual] = BinaryOperator.NotEqual,
        [TokenKind.Less] = BinaryOperator.Less,
        [TokenKind.LessOrEqual] = BinaryOperator.LessOrEqual,
        [TokenKind.Greater] = BinaryOperator.Greater,
        [TokenKind.GreaterOrEqual] = BinaryOperator.GreaterOrEqual,
    };

    private static readonly Dictionary<string, BinaryOperator> KeywordComparisons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["in"] = BinaryOperator.In,
        ["contains"] = BinaryOperator.Contains,
        ["startswith"] = BinaryOperator.StartsWith,
        ["endswith"] = BinaryOperator.EndsWith,
        ["matches"] = BinaryOperator.Matches,
    };

    public static Expr Parse(TokenCursor s) => ParseOr(s);

    public static FieldExpr ParseField(TokenCursor s)
    {
        var path = new List<string> { s.ExpectIdentifier("un nome di campo") };
        while (s.Current.Kind == TokenKind.Dot)
        {
            s.Advance();
            path.Add(s.ExpectIdentifier("un nome di campo dopo '.'"));
        }

        return new FieldExpr(path);
    }

    private static Expr ParseOr(TokenCursor s)
    {
        var left = ParseAnd(s);
        while (s.MatchKeyword("or"))
        {
            left = new BinaryExpr(BinaryOperator.Or, left, ParseAnd(s));
        }

        return left;
    }

    private static Expr ParseAnd(TokenCursor s)
    {
        var left = ParseNot(s);
        while (s.MatchKeyword("and"))
        {
            left = new BinaryExpr(BinaryOperator.And, left, ParseNot(s));
        }

        return left;
    }

    private static Expr ParseNot(TokenCursor s) =>
        s.MatchKeyword("not") ? new UnaryExpr(UnaryOperator.Not, ParseNot(s)) : ParseComparison(s);

    private static Expr ParseComparison(TokenCursor s)
    {
        var left = ParseAdditive(s);
        var current = s.Current;
        if (SymbolComparisons.TryGetValue(current.Kind, out var op))
        {
            s.Advance();
            return new BinaryExpr(op, left, ParseAdditive(s));
        }

        if (current.Kind == TokenKind.Identifier && KeywordComparisons.TryGetValue(current.Text, out op))
        {
            s.Advance();
            if (op == BinaryOperator.In && s.Current.Kind == TokenKind.LeftParen)
            {
                s.Advance();
                return new BinaryExpr(op, left, new ListExpr(ParseList(s, TokenKind.RightParen, "')'")));
            }

            return new BinaryExpr(op, left, ParseAdditive(s));
        }

        if (current.IsKeyword("is"))
        {
            s.Advance();
            var negated = s.MatchKeyword("not");
            s.ExpectKeyword("null");
            return new IsNullExpr(left, negated);
        }

        return left;
    }

    private static Expr ParseAdditive(TokenCursor s)
    {
        var left = ParseMultiplicative(s);
        while (s.Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var op = s.Advance().Kind == TokenKind.Plus ? BinaryOperator.Add : BinaryOperator.Subtract;
            left = new BinaryExpr(op, left, ParseMultiplicative(s));
        }

        return left;
    }

    private static Expr ParseMultiplicative(TokenCursor s)
    {
        var left = ParseUnary(s);
        while (s.Current.Kind is TokenKind.Star or TokenKind.Slash)
        {
            var op = s.Advance().Kind == TokenKind.Star ? BinaryOperator.Multiply : BinaryOperator.Divide;
            left = new BinaryExpr(op, left, ParseUnary(s));
        }

        return left;
    }

    private static Expr ParseUnary(TokenCursor s) =>
        s.Match(TokenKind.Minus) ? new UnaryExpr(UnaryOperator.Negate, ParseUnary(s)) : ParsePrimary(s);

    private static Expr ParsePrimary(TokenCursor s)
    {
        var token = s.Current;
        switch (token.Kind)
        {
            case TokenKind.StringLiteral:
            case TokenKind.Number:
            case TokenKind.Duration:
                s.Advance();
                return new LiteralExpr(token.Value);
            case TokenKind.LeftParen:
                return ParseParenthesized(s);
            case TokenKind.LeftBracket:
                s.Advance();
                return new ListExpr(ParseList(s, TokenKind.RightBracket, "']'"));
            case TokenKind.Identifier:
                return ParseIdentifier(s);
            default:
                throw s.Error("un'espressione");
        }
    }

    private static Expr ParseParenthesized(TokenCursor s)
    {
        s.Advance();
        var first = Parse(s);
        if (s.Match(TokenKind.RightParen))
        {
            return first;
        }

        s.Expect(TokenKind.Comma, "',' oppure ')'");
        var items = new List<Expr> { first };
        items.AddRange(ParseList(s, TokenKind.RightParen, "')'"));
        return new ListExpr(items);
    }

    private static List<Expr> ParseList(TokenCursor s, TokenKind close, string closeText)
    {
        var items = new List<Expr>();
        if (s.Match(close))
        {
            return items;
        }

        do
        {
            items.Add(Parse(s));
        }
        while (s.Match(TokenKind.Comma));

        s.Expect(close, closeText);
        return items;
    }

    private static Expr ParseIdentifier(TokenCursor s)
    {
        var token = s.Current;
        if (token.IsKeyword("true") || token.IsKeyword("false"))
        {
            s.Advance();
            return new LiteralExpr(token.IsKeyword("true"));
        }

        if (token.IsKeyword("null"))
        {
            s.Advance();
            return new LiteralExpr(null);
        }

        if (s.Peek().Kind == TokenKind.LeftParen)
        {
            var name = s.Advance().Text.ToLowerInvariant();
            s.Advance();
            return new CallExpr(name, ParseList(s, TokenKind.RightParen, "')'"));
        }

        return ParseField(s);
    }
}
