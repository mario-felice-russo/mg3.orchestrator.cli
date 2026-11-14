using System.Globalization;
using System.Text;
using Mg3.Orchestrator.Abstractions.Errors;

namespace Mg3.Orchestrator.Query.Parsing;

/// <summary>Scompone il testo della DSL in token.</summary>
public static class Lexer
{
    public static IReadOnlyList<Token> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var start = i;
            if (char.IsLetter(c) || c == '_' || c == '@')
            {
                i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }

                var ident = text[start..i];
                tokens.Add(new Token(TokenKind.Identifier, ident, ident, start));
                continue;
            }

            if (char.IsDigit(c))
            {
                tokens.Add(ReadNumber(text, ref i));
                continue;
            }

            if (c is '\'' or '"')
            {
                tokens.Add(ReadString(text, ref i));
                continue;
            }

            tokens.Add(ReadSymbol(text, ref i));
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, null, text.Length));
        return tokens;
    }

    private static Token ReadNumber(string text, ref int i)
    {
        var start = i;
        while (i < text.Length && char.IsDigit(text[i]))
        {
            i++;
        }

        if (i + 1 < text.Length && text[i] == '.' && char.IsDigit(text[i + 1]))
        {
            i++;
            while (i < text.Length && char.IsDigit(text[i]))
            {
                i++;
            }
        }

        var number = decimal.Parse(text[start..i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        var unitStart = i;
        while (i < text.Length && char.IsLetter(text[i]))
        {
            i++;
        }

        if (unitStart == i)
        {
            return new Token(TokenKind.Number, text[start..i], number, start);
        }

        var unit = text[unitStart..i].ToLowerInvariant();
        var duration = unit switch
        {
            "ms" => TimeSpan.FromMilliseconds((double)number),
            "s" => TimeSpan.FromSeconds((double)number),
            "m" => TimeSpan.FromMinutes((double)number),
            "h" => TimeSpan.FromHours((double)number),
            "d" => TimeSpan.FromDays((double)number),
            "w" => TimeSpan.FromDays((double)number * 7),
            _ => throw new QueryException($"Unità di durata non valida '{unit}': attese ms, s, m, h, d, w", unitStart),
        };
        return new Token(TokenKind.Duration, text[start..i], duration, start);
    }

    private static Token ReadString(string text, ref int i)
    {
        var quote = text[i];
        var start = i;
        i++;
        var sb = new StringBuilder();
        while (true)
        {
            if (i >= text.Length)
            {
                throw new QueryException("Stringa non terminata", start);
            }

            if (text[i] == quote)
            {
                if (i + 1 < text.Length && text[i + 1] == quote)
                {
                    sb.Append(quote);
                    i += 2;
                    continue;
                }

                i++;
                break;
            }

            sb.Append(text[i]);
            i++;
        }

        var value = sb.ToString();
        return new Token(TokenKind.StringLiteral, value, value, start);
    }

    private static Token ReadSymbol(string text, ref int i)
    {
        var start = i;
        var c = text[i];
        var next = i + 1 < text.Length ? text[i + 1] : '\0';
        (TokenKind kind, int length) = (c, next) switch
        {
            ('=', '=') => (TokenKind.EqualEqual, 2),
            ('!', '=') => (TokenKind.NotEqual, 2),
            ('<', '=') => (TokenKind.LessOrEqual, 2),
            ('>', '=') => (TokenKind.GreaterOrEqual, 2),
            ('<', '>') => (TokenKind.NotEqual, 2),
            ('<', _) => (TokenKind.Less, 1),
            ('>', _) => (TokenKind.Greater, 1),
            ('=', _) => (TokenKind.Assign, 1),
            ('|', _) => (TokenKind.Pipe, 1),
            (',', _) => (TokenKind.Comma, 1),
            ('(', _) => (TokenKind.LeftParen, 1),
            (')', _) => (TokenKind.RightParen, 1),
            ('[', _) => (TokenKind.LeftBracket, 1),
            (']', _) => (TokenKind.RightBracket, 1),
            ('.', _) => (TokenKind.Dot, 1),
            ('$', _) => (TokenKind.Dollar, 1),
            ('+', _) => (TokenKind.Plus, 1),
            ('-', _) => (TokenKind.Minus, 1),
            ('*', _) => (TokenKind.Star, 1),
            ('/', _) => (TokenKind.Slash, 1),
            _ => throw new QueryException($"Carattere non valido '{c}'", start),
        };
        i += length;
        return new Token(kind, text.Substring(start, length), null, start);
    }
}
