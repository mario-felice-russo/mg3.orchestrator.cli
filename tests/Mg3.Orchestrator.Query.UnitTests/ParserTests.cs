using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Query;
using Mg3.Orchestrator.Query.Parsing;

namespace Mg3.Orchestrator.Query.UnitTests;

public sealed class ParserTests
{
    public static TheoryData<string> ValidQueries =>
    [
        "where State == 'Faulted'",
        "where State == 'Faulted' and (Id > 10 or not Source == 'Manual')",
        "select Key, ReleaseName as Processo, durata = EndTime - StartTime",
        "orderby StartTime desc, Id asc | take 10 | skip 2",
        "distinct ReleaseName, HostMachineName",
        "groupby ReleaseName | aggregate n = count(), ultimi = max(EndTime), media = avg(Id), errori = countif(State == 'Faulted')",
        "from jobs as j | join releases as r on j.ReleaseName == r.Name | select j.Key, r.ProcessKey",
        "from $faults | left join $rel as r on ReleaseName == r.Name",
        "from stats jobs | where count > 0",
        "where CreationTime >= now() - 7d and EndTime is not null",
        "where State in ('Faulted', 'Stopped') | count",
        "where State in ['Faulted'] and Info matches '^Errore.*$'",
        "flatten Tags | where Tags startswith 't'",
        "where -Id < -5 and len(Info) > 3 * 2 / 1 + 0",
        "where date('2026-11-01') <= CreationTime and year(CreationTime) == 2026 and coalesce(EndTime, StartTime) is not null",
        "where Info contains 'O''Brien' and Info endswith \"x\"",
        "select n = 1.5, d = 90m, b = true, z = null",
    ];

    [Theory]
    [MemberData(nameof(ValidQueries))]
    public void ParsesValidQueries(string query)
    {
        var ast = QueryHost.Parser.Parse(query);
        (ast.Source is not null || ast.Stages.Count > 0).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(ValidQueries))]
    public void RoundTripsThroughPrinter(string query)
    {
        var first = QueryHost.Printer.Print(QueryHost.Parser.Parse(query));
        var second = QueryHost.Printer.Print(QueryHost.Parser.Parse(first));
        second.ShouldBe(first);
    }

    [Theory]
    [InlineData("", 0, "vuota")]
    [InlineData("wher State == 'x'", 0, "uno stadio")]
    [InlineData("where State == ", 15, "un'espressione")]
    [InlineData("where State == 'x' take 3", 19, "'|'")]
    [InlineData("take -1", 5, "un intero")]
    [InlineData("take 1.5", 5, "intero non negativo")]
    [InlineData("where Name == 'abc", 14, "Stringa non terminata")]
    [InlineData("where Id > 5x", 12, "Unità di durata")]
    [InlineData("where Id # 5", 9, "Carattere non valido")]
    [InlineData("aggregate n = median(Id)", 14, "sconosciuta")]
    [InlineData("aggregate n = sum()", 14, "richiede un argomento")]
    [InlineData("join releases on A == B", 14, "'as'")]
    [InlineData("from jobs | from jobs", 12, "uno stadio")]
    [InlineData("where Id is 5", 12, "'null'")]
    [InlineData("select (Id, Name", 16, "')'")]
    public void ReportsPositionOfErrors(string query, int position, string fragment)
    {
        var error = Should.Throw<QueryException>(() => QueryHost.Parser.Parse(query));
        error.Position.ShouldBe(position);
        error.Message.ShouldContain(fragment);
        error.ExitCode.ShouldBe(ExitCodes.Query);
    }

    [Fact]
    public void ParsesSourceWithAliasAndMultiWordResource()
    {
        var ast = QueryHost.Parser.Parse("from stats jobs as s");
        ast.Source.ShouldBe(new SourceNode("stats jobs", false, "s", 5));
        ast.Stages.ShouldBeEmpty();
    }

    [Fact]
    public void ParsesReferenceSource()
    {
        var ast = QueryHost.Parser.Parse("from $faults | count");
        ast.Source!.IsReference.ShouldBeTrue();
        ast.Source.SourceKey.ShouldBe("$faults");
        ast.Stages.Single().ShouldBeOfType<CountStage>();
    }

    [Fact]
    public void KeywordsAreCaseInsensitive()
    {
        var ast = QueryHost.Parser.Parse("WHERE State == 'x' AND Id > 1 | ORDERBY Id DESC | TAKE 2");
        ast.Stages.Count.ShouldBe(3);
        ((OrderByStage)ast.Stages[1]).Keys[0].Descending.ShouldBeTrue();
    }

    [Fact]
    public void LexerParsesDurationsAndEscapes()
    {
        var tokens = Lexer.Tokenize("1500ms 2s 3m 4h 5d 1w 'a''b' \"c\"\"d\"");
        tokens.Select(t => t.Value).Take(8).ShouldBe(
        [
            TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(3), TimeSpan.FromHours(4),
            TimeSpan.FromDays(5), TimeSpan.FromDays(7), "a'b", "c\"d",
        ]);
        tokens[^1].Kind.ShouldBe(TokenKind.End);
    }

    [Fact]
    public void LexerRecognizesAllSymbols()
    {
        var kinds = Lexer.Tokenize("| , ( ) [ ] . $ == != <> < <= > >= = + - * /").Select(t => t.Kind).ToArray();
        kinds.ShouldBe(
        [
            TokenKind.Pipe, TokenKind.Comma, TokenKind.LeftParen, TokenKind.RightParen, TokenKind.LeftBracket, TokenKind.RightBracket,
            TokenKind.Dot, TokenKind.Dollar, TokenKind.EqualEqual, TokenKind.NotEqual, TokenKind.NotEqual, TokenKind.Less,
            TokenKind.LessOrEqual, TokenKind.Greater, TokenKind.GreaterOrEqual, TokenKind.Assign, TokenKind.Plus, TokenKind.Minus,
            TokenKind.Star, TokenKind.Slash, TokenKind.End,
        ]);
    }

    [Fact]
    public void PrinterUsesCanonicalForm()
    {
        var text = QueryHost.Printer.Print(QueryHost.Parser.Parse("from jobs as j | where a==1 and b in ('x') | select n=count | orderby n"));
        text.ShouldBe("from jobs as j | where ((a == 1) and (b in ['x'])) | select n = count | orderby n asc");
    }
}
