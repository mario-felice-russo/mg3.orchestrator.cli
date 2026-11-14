using Mg3.Orchestrator.Abstractions.Errors;

namespace Mg3.Orchestrator.Query.Parsing;

/// <summary>Cursore sui token con utilità per errori precisi.</summary>
public sealed class TokenCursor(IReadOnlyList<Token> tokens)
{
    private int _index;

    public Token Current => tokens[_index];

    public Token Peek(int offset = 1) => tokens[Math.Min(_index + offset, tokens.Count - 1)];

    public Token Advance()
    {
        var token = tokens[_index];
        if (_index < tokens.Count - 1)
        {
            _index++;
        }

        return token;
    }

    public bool Match(TokenKind kind)
    {
        if (Current.Kind != kind)
        {
            return false;
        }

        Advance();
        return true;
    }

    public bool MatchKeyword(string keyword)
    {
        if (!Current.IsKeyword(keyword))
        {
            return false;
        }

        Advance();
        return true;
    }

    public Token Expect(TokenKind kind, string expected)
    {
        if (Current.Kind != kind)
        {
            throw Error(expected);
        }

        return Advance();
    }

    public void ExpectKeyword(string keyword)
    {
        if (!MatchKeyword(keyword))
        {
            throw Error($"'{keyword}'");
        }
    }

    public string ExpectIdentifier(string expected) => Expect(TokenKind.Identifier, expected).Text;

    public int ExpectInteger(string expected)
    {
        var token = Expect(TokenKind.Number, expected);
        var value = (decimal)token.Value!;
        if (value != decimal.Truncate(value) || value < 0 || value > int.MaxValue)
        {
            throw new QueryException($"Atteso un intero non negativo, trovato {token.Describe()}", token.Position);
        }

        return (int)value;
    }

    public QueryException Error(string expected) =>
        new($"Atteso {expected}, trovato {Current.Describe()}", Current.Position);
}
