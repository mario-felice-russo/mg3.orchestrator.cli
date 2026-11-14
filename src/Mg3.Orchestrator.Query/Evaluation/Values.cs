using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Mg3.Orchestrator.Query.Evaluation;

/// <summary>
/// Conversione e confronto dei valori. Tipi interni: null, bool, decimal, string, DateTimeOffset, TimeSpan,
/// JsonNode (oggetti/array) e IReadOnlyList&lt;object?&gt; (liste letterali).
/// </summary>
public static partial class Values
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:?\d{2})?)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex IsoDatePattern();

    public static object? FromJson(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return node;
        }

        return value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.Number => value.TryGetValue<decimal>(out var d) ? d : (decimal)value.GetValue<double>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    public static JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        bool b => JsonValue.Create(b),
        decimal d => JsonValue.Create(d),
        string s => JsonValue.Create(s),
        DateTimeOffset dt => JsonValue.Create(FormatDate(dt)),
        TimeSpan t => JsonValue.Create(t.ToString("c", CultureInfo.InvariantCulture)),
        IReadOnlyList<object?> list => new JsonArray(list.Select(ToJson).ToArray()),
        _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    public static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);

    public static bool TryParseDate(object? value, out DateTimeOffset date)
    {
        switch (value)
        {
            case DateTimeOffset d:
                date = d;
                return true;
            case string s when IsoDatePattern().IsMatch(s):
                return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);
            default:
                date = default;
                return false;
        }
    }

    public static bool TryNumber(object? value, out decimal number)
    {
        switch (value)
        {
            case decimal d:
                number = d;
                return true;
            case string s:
                return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out number);
            default:
                number = 0;
                return false;
        }
    }

    public static bool IsTruthy(object? value) => value is true;

    public static new bool Equals(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return Compare(left, right) == 0;
    }

    /// <summary>Confronto totale e deterministico: null per primo; stringhe case-insensitive; date ISO come date.</summary>
    public static int Compare(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return (left is null ? 0 : 1) - (right is null ? 0 : 1);
        }

        if (left is decimal || right is decimal)
        {
            if (TryNumber(left, out var a) && TryNumber(right, out var b))
            {
                return a.CompareTo(b);
            }
        }

        if (left is DateTimeOffset || right is DateTimeOffset || (left is string && right is string))
        {
            if (TryParseDate(left, out var a) && TryParseDate(right, out var b))
            {
                return a.CompareTo(b);
            }
        }

        return (left, right) switch
        {
            (string a, string b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase),
            (bool a, bool b) => a.CompareTo(b),
            (TimeSpan a, TimeSpan b) => a.CompareTo(b),
            _ => CompareByRank(left, right),
        };
    }

    /// <summary>Chiave canonica per raggruppamenti, distinct e join.</summary>
    public static string CanonicalKey(object? value) => value switch
    {
        null => "n:",
        bool b => b ? "b:1" : "b:0",
        decimal d => "d:" + d.ToString("G29", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.'),
        string s when TryParseDate(s, out var date) => "t:" + FormatDate(date),
        string s => "s:" + s.ToUpperInvariant(),
        DateTimeOffset dt => "t:" + FormatDate(dt),
        TimeSpan t => "p:" + t.Ticks.ToString(CultureInfo.InvariantCulture),
        JsonNode node => "j:" + node.ToJsonString(),
        IReadOnlyList<object?> list => "l:[" + string.Join(",", list.Select(CanonicalKey)) + "]",
        _ => "o:" + Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    public static string Describe(object? value) => value switch
    {
        null => "null",
        string s => s,
        _ => ToJson(value)?.ToJsonString() ?? "null",
    };

    private static int CompareByRank(object left, object right)
    {
        var byRank = Rank(left).CompareTo(Rank(right));
        return byRank != 0 ? byRank : string.CompareOrdinal(CanonicalKey(left), CanonicalKey(right));
    }

    private static int Rank(object value) => value switch
    {
        bool => 1,
        decimal => 2,
        TimeSpan => 3,
        DateTimeOffset => 4,
        string => 5,
        _ => 6,
    };
}
