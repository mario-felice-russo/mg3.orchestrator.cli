using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Configuration;
using Mg3.Orchestrator.Output.Json;

namespace Mg3.Orchestrator.Output.Text;

/// <summary>Converte i valori JSON in celle di testo: date nel fuso configurato, numeri nella cultura, troncamento con '…'.</summary>
public sealed class CellFormatter
{
    private readonly CultureInfo _culture;
    private readonly TimeZoneInfo _timeZone;
    private readonly int? _maxWidth;

    public CellFormatter(OutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var csv = settings.TextStyle == TextStyle.Csv;
        _culture = csv ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(settings.Culture);
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone);
        _maxWidth = csv ? null : settings.MaxCellWidth;
    }

    public string Format(JsonNode? node)
    {
        var text = node switch
        {
            null => string.Empty,
            JsonValue value => FormatValue(value),
            _ => node.ToJsonString(JsonOutputFormatter.WriteOptions),
        };
        text = Sanitize(text);
        return _maxWidth is { } max && text.Length > max ? text[..(max - 1)] + "…" : text;
    }

    private string FormatValue(JsonValue value) => value.GetValueKind() switch
    {
        JsonValueKind.String => FormatString(value.GetValue<string>()),
        JsonValueKind.Number => value.TryGetValue<decimal>(out var d) ? d.ToString(_culture) : value.ToJsonString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => string.Empty,
    };

    private string FormatString(string value)
    {
        if (value.Length >= 19 && value[4] == '-' && value[10] == 'T'
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            return TimeZoneInfo.ConvertTime(date, _timeZone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return value;
    }

    private static string Sanitize(string text)
    {
        if (!text.Any(char.IsControl))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(char.IsControl(c) ? ' ' : c);
        }

        return sb.ToString();
    }
}
