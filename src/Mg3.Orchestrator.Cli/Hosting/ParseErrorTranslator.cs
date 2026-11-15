using System.CommandLine;

namespace Mg3.Orchestrator.Cli.Hosting;

/// <summary>Traduce gli errori di System.CommandLine in messaggi italiani, con un suggerimento quando possibile.</summary>
public static class ParseErrorTranslator
{
    public static string Translate(ParseResult parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        var command = parse.CommandResult.Command;
        var path = CommandPath(parse);
        var help = $"Usa 'mg3orch{(path.Length == 0 ? string.Empty : " " + path)} --help' per l'elenco.";
        if (parse.UnmatchedTokens.Count > 0)
        {
            var token = parse.UnmatchedTokens[0];
            var message = token.StartsWith('-')
                ? $"'{token}' non è un'opzione valida per '{Display(path)}'."
                : $"'{token}' non è un comando valido per '{Display(path)}'.";
            return Suggest(command, path, parse.UnmatchedTokens) is { } suggestion
                ? $"{message} Forse intendevi: {suggestion}"
                : $"{message} {help}";
        }

        var error = parse.Errors[0].Message;
        if (error.Contains("Required command was not provided", StringComparison.Ordinal))
        {
            var names = command.Subcommands.Where(c => !c.Hidden).Select(c => c.Name).ToArray();
            return $"'{Display(path)}' richiede un sottocomando: {string.Join(", ", names)}. {help}";
        }

        if (error.Contains("Required argument missing", StringComparison.Ordinal))
        {
            var argument = command.Arguments.FirstOrDefault()?.Name ?? "argomento";
            return $"'{Display(path)}' richiede l'argomento <{argument}>. {help}";
        }

        if (error.Contains("is required", StringComparison.Ordinal))
        {
            var option = command.Options.FirstOrDefault(o => o.Required)?.Name ?? "un'opzione";
            return $"'{Display(path)}' richiede l'opzione {option}. {help}";
        }

        return $"{error} {help}";
    }

    private static string? Suggest(Command command, string path, IReadOnlyList<string> unmatched)
    {
        var token = unmatched[0];
        var bare = token.TrimStart('-');
        var rest = string.Join(' ', unmatched.Skip(1));
        var names = command.Subcommands.SelectMany(c => c.Aliases.Prepend(c.Name)).ToArray();
        var match = names.FirstOrDefault(n => string.Equals(n, bare, StringComparison.OrdinalIgnoreCase))
            ?? names.Where(n => Distance(n, bare.ToLowerInvariant()) <= 2).OrderBy(n => Distance(n, bare.ToLowerInvariant())).FirstOrDefault();
        if (match is null)
        {
            return null;
        }

        return string.Join(' ', new[] { "mg3orch", path, match, rest }.Where(p => p.Length > 0));
    }

    private static string CommandPath(ParseResult parse)
    {
        var names = new List<string>();
        for (var result = parse.CommandResult; result is not null; result = result.Parent as System.CommandLine.Parsing.CommandResult)
        {
            if (result.Command is not RootCommand)
            {
                names.Insert(0, result.Command.Name);
            }
        }

        return string.Join(' ', names);
    }

    private static string Display(string path) => path.Length == 0 ? "mg3orch" : "mg3orch " + path;

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[a.Length, b.Length];
    }
}
