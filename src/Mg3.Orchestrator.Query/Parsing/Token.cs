namespace Mg3.Orchestrator.Query.Parsing;

public enum TokenKind
{
    Identifier,
    StringLiteral,
    Number,
    Duration,
    Pipe,
    Comma,
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    Dot,
    Dollar,
    EqualEqual,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Assign,
    Plus,
    Minus,
    Star,
    Slash,
    End,
}

/// <param name="Value">Valore del letterale: string per identificatori/stringhe, decimal per numeri, TimeSpan per durate.</param>
public sealed record Token(TokenKind Kind, string Text, object? Value, int Position)
{
    public bool IsKeyword(string keyword) =>
        Kind == TokenKind.Identifier && string.Equals(Text, keyword, StringComparison.OrdinalIgnoreCase);

    public string Describe() => Kind switch
    {
        TokenKind.End => "fine della query",
        TokenKind.StringLiteral => $"stringa '{Text}'",
        _ => $"'{Text}'",
    };
}
