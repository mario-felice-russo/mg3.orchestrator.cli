using System.Globalization;
using System.Text.RegularExpressions;
using Mg3.Orchestrator.Abstractions.Errors;

namespace Mg3.Orchestrator.Core.Commands;

/// <summary>Argomenti nominati ammessi per un comando e loro conversione.</summary>
public static partial class CommandArguments
{
    public const string Folder = "folder";
    public const string Filter = "filter";
    public const string Select = "select";
    public const string OrderBy = "orderby";
    public const string Top = "top";
    public const string Skip = "skip";
    public const string Expand = "expand";
    public const string Since = "since";
    public const string Key = "key";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Folder, Filter, Select, OrderBy, Top, Skip, Expand, Since, Key,
    };

    [GeneratedRegex(@"^(?<n>\d+(\.\d+)?)(?<u>ms|s|m|h|d|w)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex DurationPattern();

    public static void Validate(IReadOnlyDictionary<string, string> args, string commandId)
    {
        ArgumentNullException.ThrowIfNull(args);
        var unknown = args.Keys.Where(k => !Known.Contains(k)).ToArray();
        if (unknown.Length > 0)
        {
            throw new ConfigurationException($"Comando '{commandId}': argomenti sconosciuti {string.Join(", ", unknown)}. Ammessi: {string.Join(", ", Known.Order(StringComparer.Ordinal))}.");
        }
    }

    public static string? Get(IReadOnlyDictionary<string, string> args, string name)
    {
        ArgumentNullException.ThrowIfNull(args);
        var match = args.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(match.Value) ? null : match.Value;
    }

    public static int? GetInt(IReadOnlyDictionary<string, string> args, string name)
    {
        var value = Get(args, name);
        if (value is null)
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new ConfigurationException($"L'argomento '{name}' deve essere un intero non negativo, trovato '{value}'.");
    }

    public static TimeSpan ParseDuration(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var match = DurationPattern().Match(value.Trim());
        if (!match.Success)
        {
            throw new ConfigurationException($"Durata non valida '{value}': usare ad esempio 30m, 24h, 7d.");
        }

        var n = double.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        return match.Groups["u"].Value.ToLowerInvariant() switch
        {
            "ms" => TimeSpan.FromMilliseconds(n),
            "s" => TimeSpan.FromSeconds(n),
            "m" => TimeSpan.FromMinutes(n),
            "h" => TimeSpan.FromHours(n),
            "d" => TimeSpan.FromDays(n),
            _ => TimeSpan.FromDays(n * 7),
        };
    }
}
