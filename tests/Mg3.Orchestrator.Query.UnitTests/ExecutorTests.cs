using System.Text.Json.Nodes;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Tests.Shared;

namespace Mg3.Orchestrator.Query.UnitTests;

public sealed class ExecutorTests
{
    private static readonly IReadOnlyList<JsonNode?> Jobs = Fixtures.Rows("jobs.json");
    private static readonly IReadOnlyList<JsonNode?> Releases = Fixtures.Rows("releases.json");

    private static IReadOnlyList<JsonNode?> Rows(string query) => QueryHost.Run(query, Jobs, Sources()).Rows;

    private static Dictionary<string, IReadOnlyList<JsonNode?>> Sources() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["releases"] = Releases,
        ["$rel"] = Releases,
    };

    [Fact]
    public void WhereFiltersCaseInsensitively()
    {
        Rows("where State == 'faulted'").Count.ShouldBe(8);
        Rows("where State != 'Faulted'").Count.ShouldBe(32);
    }

    [Fact]
    public void WhereComparesDatesAndDurations()
    {
        Rows("where CreationTime >= now() - 12h").Count.ShouldBe(Jobs.Count(j => DateTimeOffset.Parse(j!["CreationTime"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture) >= Fixtures.Now.AddHours(-12)));
        Rows("where EndTime - StartTime > 10m").Count.ShouldBe(Jobs.Count(j => j!["EndTime"] is not null && (i(j) % 7) + 5 > 10));

        static int i(JsonNode j) => j["Id"]!.GetValue<int>() - 1000;
    }

    [Fact]
    public void NullsAreHandledSafely()
    {
        Rows("where EndTime > now()").Count.ShouldBe(0);
        Rows("where Missing == null").Count.ShouldBe(40);
        Rows("where Missing.Deep is null").Count.ShouldBe(40);
        Rows("where not (Missing == 'x')").Count.ShouldBe(40);
        Rows("where EndTime is null").Count.ShouldBe(16);
    }

    [Fact]
    public void SelectProjectsFieldsAliasesAndExpressions()
    {
        var row = Rows("where Id == 1001 | select Id, Robot.Name, processo = ReleaseName, durata = EndTime - StartTime, Id * 2").Single()!;
        row.ToJsonString().ShouldBe("{\"Id\":1001,\"Robot.Name\":\"Robot-VM02\",\"processo\":\"Ordini\",\"durata\":\"00:06:00\",\"(Id * 2)\":2002}");
    }

    [Fact]
    public void OrderByIsStableAndSupportsMultipleKeys()
    {
        var ids = Rows("orderby ReleaseName asc, Id desc | take 3 | select Id").Select(r => r!["Id"]!.GetValue<int>());
        ids.ShouldBe([1040, 1036, 1032]);
    }

    [Fact]
    public void GroupByAggregates()
    {
        var rows = Rows("groupby ReleaseName | aggregate n = count(), errori = countif(State == 'Faulted'), somma = sum(Id), media = avg(Id), primo = min(CreationTime), ultimo = max(Id), fine = count(EndTime) | orderby ReleaseName");
        rows.Count.ShouldBe(4);
        var fatture = rows[0]!;
        fatture["ReleaseName"]!.GetValue<string>().ShouldBe("Fatture");
        fatture["n"]!.GetValue<decimal>().ShouldBe(10);
        fatture["errori"]!.GetValue<decimal>().ShouldBe(Jobs.Count(j => j!["ReleaseName"]!.GetValue<string>() == "Fatture" && j["State"]!.GetValue<string>() == "Faulted"));
        fatture["somma"]!.GetValue<decimal>().ShouldBe(Jobs.Where(j => j!["ReleaseName"]!.GetValue<string>() == "Fatture").Sum(j => j!["Id"]!.GetValue<int>()));
        fatture["media"]!.GetValue<decimal>().ShouldBe(1022m);
        fatture["primo"]!.GetValue<string>().ShouldBe("2026-11-01T10:28:00.000Z");
        fatture["ultimo"]!.GetValue<decimal>().ShouldBe(1040);
    }

    [Fact]
    public void GroupByWithoutAggregateCounts()
    {
        var row = Rows("where State == 'Faulted' | groupby HostMachineName | orderby HostMachineName")[0]!;
        row.ToJsonString().ShouldBe("{\"HostMachineName\":\"VM01\",\"count\":3}");
    }

    [Fact]
    public void AggregateWithoutGroupByProducesOneRow()
    {
        Rows("aggregate n = count(), m = max(Id)").Single()!.ToJsonString().ShouldBe("{\"n\":40,\"m\":1040}");
        QueryHost.Run("aggregate n = count(), s = sum(Id), a = avg(Id), mi = min(Id)", []).Rows.Single()!.ToJsonString()
            .ShouldBe("{\"n\":0,\"s\":0,\"a\":null,\"mi\":null}");
    }

    [Fact]
    public void InnerJoinMatchesOnKeys()
    {
        var rows = Rows("from jobs as j | join releases as r on j.ReleaseName == r.Name | select j.Id, r.ProcessKey | take 2");
        rows.Select(r => r!.ToJsonString()).ShouldBe(["{\"Id\":1001,\"r.ProcessKey\":\"OrdiniProcess\"}", "{\"Id\":1002,\"r.ProcessKey\":\"PagamentiProcess\"}"]);
    }

    [Fact]
    public void LeftJoinKeepsUnmatchedRows()
    {
        var releases = new JsonNode?[] { JsonNode.Parse("{\"Name\":\"Ordini\",\"Owner\":\"Anna\"}") };
        var sources = new Dictionary<string, IReadOnlyList<JsonNode?>> { ["$rel"] = releases };
        var left = QueryHost.Run("left join $rel as r on ReleaseName == r.Name | select Id, r.Owner | take 2", Jobs, sources).Rows;
        left.Select(r => r!.ToJsonString()).ShouldBe(["{\"Id\":1001,\"r.Owner\":\"Anna\"}", "{\"Id\":1002,\"r.Owner\":null}"]);
        QueryHost.Run("join $rel as r on ReleaseName == r.Name", Jobs, sources).Rows.Count.ShouldBe(10);
    }

    [Fact]
    public void JoinRespectsMaxRows()
    {
        var result = QueryHost.Run("join releases as r on ReleaseName == r.Name", Jobs, Sources(), maxJoinRows: 5);
        result.Rows.Count.ShouldBe(5);
        result.Truncated.ShouldBeTrue();
        result.Warnings.ShouldContain(w => w.Contains("maxJoinRows", StringComparison.Ordinal));
    }

    [Fact]
    public void JoinWithUnknownSourceFails()
    {
        Should.Throw<QueryException>(() => QueryHost.Run("join $missing as m on Id == m.Id", Jobs));
    }

    [Fact]
    public void FlattenExpandsArrays()
    {
        var rows = Rows("where Id == 1004 | flatten Tags | select Id, Tags");
        rows.Select(r => r!.ToJsonString()).ShouldBe(["{\"Id\":1004,\"Tags\":\"t1\"}", "{\"Id\":1004,\"Tags\":\"batch\"}"]);
        Rows("where Id == 1001 | flatten Tags").Count.ShouldBe(0);
        Rows("where Id == 1001 | flatten Robot").Count.ShouldBe(1);
    }

    [Fact]
    public void DistinctOnFieldsAndWholeRows()
    {
        Rows("distinct ReleaseName").Count.ShouldBe(4);
        Rows("select HostMachineName | distinct").Count.ShouldBe(3);
    }

    [Fact]
    public void CountTakeSkip()
    {
        Rows("count").Single()!["count"]!.GetValue<int>().ShouldBe(40);
        Rows("skip 38 | take 5").Count.ShouldBe(2);
    }

    [Fact]
    public void OperatorsOnStringsAndLists()
    {
        Rows("where Info contains 'IMPORTO'").Count.ShouldBe(8);
        Rows("where Tags contains 'batch'").Count.ShouldBe(10);
        Rows("where State in ['Faulted', 'Stopped']").Count.ShouldBe(16);
        Rows("where ReleaseName startswith 'fat' and HostMachineName endswith '1'").Count.ShouldBe(Jobs.Count(j => j!["ReleaseName"]!.GetValue<string>() == "Fatture" && j["HostMachineName"]!.GetValue<string>() == "VM01"));
        Rows("where Info matches '^Errore.*\\(€ \\d+\\)$'").Count.ShouldBe(8);
    }

    [Fact]
    public void FunctionsWork()
    {
        var row = Rows("where Id == 1001 | select y = year(CreationTime), m = month(CreationTime), d = day(CreationTime), h = hour(CreationTime), l = len(ReleaseName), u = upper(ReleaseName), lo = lower(ReleaseName), c = coalesce(Missing, 'x'), t = today(), n = now(), dt = date('2026-01-01')").Single()!;
        row.ToJsonString().ShouldBe("{\"y\":2026,\"m\":11,\"d\":1,\"h\":8,\"l\":6,\"u\":\"ORDINI\",\"lo\":\"ordini\",\"c\":\"x\",\"t\":\"2026-11-02T00:00:00Z\",\"n\":\"2026-11-02T12:00:00Z\",\"dt\":\"2026-01-01T00:00:00Z\"}");
    }

    [Fact]
    public void ArithmeticHandlesNumbersStringsAndDates()
    {
        var row = Rows("where Id == 1001 | select a = Id + 1 - 2 * 3 / 2, s = ReleaseName + '!', z = Id / 0, neg = -Id, d = now() + 1d, dd = now() - date('2026-11-01'), tt = 1h + 30m - 15m, nd = -(1h)").Single()!;
        row.ToJsonString().ShouldBe("{\"a\":999,\"s\":\"Ordini!\",\"z\":null,\"neg\":-1001,\"d\":\"2026-11-03T12:00:00Z\",\"dd\":\"1.12:00:00\",\"tt\":\"01:15:00\",\"nd\":\"-01:00:00\"}");
    }

    [Theory]
    [InlineData("where unknownfn(Id) == 1", "Funzione sconosciuta")]
    [InlineData("where now(1) == 1", "accetta da 0 a 0")]
    [InlineData("where date('ieri') == 1", "non riconosciuto")]
    [InlineData("where State in 'x'", "richiede una lista")]
    [InlineData("where State matches 1", "richiede un'espressione regolare")]
    [InlineData("where State matches '('", "non valida")]
    [InlineData("where ReleaseName - 1 == 0", "non applicabile")]
    [InlineData("where -ReleaseName == 0", "Impossibile negare")]
    public void ReportsSemanticErrors(string query, string fragment)
    {
        var error = Should.Throw<QueryException>(() => Rows(query));
        error.Message.ShouldContain(fragment);
    }

    [Fact]
    public void RegexTimesOut()
    {
        var rows = new JsonNode?[] { new JsonObject { ["Text"] = new string('a', 5000) + "!" } };
        var error = Should.Throw<QueryException>(() => QueryHost.Run("where Text matches '^(a|aa)+$'", rows));
        error.Message.ShouldContain("timeout");
    }

    [Fact]
    public void WarnsAboutFieldsMissingFromAllRows()
    {
        var result = QueryHost.Run("where Sconosciuto == 1 or Id > 0 | select Id", Jobs);
        result.Warnings.ShouldBe(["Il campo 'Sconosciuto' è assente da tutte le righe."]);
        QueryHost.Run("where Sconosciuto == 1", []).Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void SourceAliasIsTransparent()
    {
        QueryHost.Run("from jobs as j | where j.State == 'Faulted' | select j.Id", Jobs).Rows.Count.ShouldBe(8);
    }

    [Fact]
    public void ComparisonsAcrossTypesAreDeterministic()
    {
        var rows = new JsonNode?[]
        {
            JsonNode.Parse("{\"v\":\"10\"}"), JsonNode.Parse("{\"v\":2}"), JsonNode.Parse("{\"v\":true}"), JsonNode.Parse("{\"v\":null}"),
            JsonNode.Parse("{\"v\":\"abc\"}"), JsonNode.Parse("{\"v\":{\"x\":1}}"),
        };
        var ordered = QueryHost.Run("orderby v", rows).Rows.Select(r => r!["v"]?.ToJsonString() ?? "null").ToArray();
        ordered.ShouldBe(["null", "true", "2", "\"10\"", "\"abc\"", "{\"x\":1}"]);
        QueryHost.Run("where v == 10", rows).Rows.Count.ShouldBe(1);
        QueryHost.Run("where v == true", rows).Rows.Count.ShouldBe(1);
    }

    [Fact]
    public void DottedOutputNamesCanBeReferencedLater()
    {
        var rows = Rows("from jobs as j | join releases as r on j.ReleaseName == r.Name | groupby r.ProcessKey | orderby count desc, r.ProcessKey | take 1");
        rows.Single()!.ToJsonString().ShouldBe("{\"r.ProcessKey\":\"FattureProcess\",\"count\":10}");
    }

    [Fact]
    public void LenAndDateHandleEveryInput()
    {
        var row = Rows("where Id == 1004 | select a = len(Tags), b = len([1, 2, 3]), c = len(Id), d = date(Missing), e = len(Missing)").Single()!;
        row.ToJsonString().ShouldBe("{\"a\":2,\"b\":3,\"c\":null,\"d\":null,\"e\":null}");
    }

    [Fact]
    public void FlattenNestedAndAliasedPaths()
    {
        var rows = new JsonNode?[] { JsonNode.Parse("{\"Id\":1,\"Data\":{\"Items\":[\"a\",\"b\"]}}"), JsonNode.Parse("[1]") };
        var nested = QueryHost.Run("flatten Data.Items | select Id, Data.Items", rows).Rows;
        nested.Select(r => r?.ToJsonString()).ShouldBe(["{\"Id\":1,\"Data.Items\":\"a\"}", "{\"Id\":1,\"Data.Items\":\"b\"}", "{\"Id\":null,\"Data.Items\":null}"]);
        var aliased = QueryHost.Run("from x as s | flatten s.Data.Items | select s.Id", rows).Rows;
        aliased.Count.ShouldBe(3);
        QueryHost.Run("flatten Data.Missing.Items", rows).Rows.Count.ShouldBe(2);
    }

    [Fact]
    public void JoinSkipsNonObjectsAndNullKeys()
    {
        var left = new JsonNode?[] { JsonNode.Parse("[1]"), JsonNode.Parse("{\"K\":null}"), JsonNode.Parse("{\"K\":1}") };
        var right = new JsonNode?[] { JsonNode.Parse("{\"K\":null}"), JsonNode.Parse("{\"K\":1,\"V\":\"x\"}"), JsonNode.Parse("{\"K\":1,\"V\":\"y\"}") };
        var sources = new Dictionary<string, IReadOnlyList<JsonNode?>> { ["$r"] = right };
        QueryHost.Run("join $r as r on K == r.K | select r.V", left, sources).Rows.Select(r => r!.ToJsonString()).ShouldBe(["{\"r.V\":\"x\"}", "{\"r.V\":\"y\"}"]);
        QueryHost.Run("left join $r as r on K == r.K", left, sources).Rows.Count.ShouldBe(3);
        QueryHost.Run("left join $r as r on K == r.K", left, sources, maxJoinRows: 1).Truncated.ShouldBeTrue();
    }

    [Fact]
    public void ValueHelpersCoverAllTypes()
    {
        Evaluation.Values.ToJson(TimeSpan.FromMinutes(1))!.ToJsonString().ShouldBe("\"00:01:00\"");
        Evaluation.Values.ToJson(new object?[] { 1m, "a" })!.ToJsonString().ShouldBe("[1,\"a\"]");
        Evaluation.Values.ToJson(42)!.ToJsonString().ShouldBe("\"42\"");
        Evaluation.Values.CanonicalKey(TimeSpan.FromTicks(5)).ShouldBe("p:5");
        Evaluation.Values.CanonicalKey(new object?[] { null, true }).ShouldBe("l:[n:,b:1]");
        Evaluation.Values.CanonicalKey(Fixtures.Now).ShouldBe("t:2026-11-02T12:00:00Z");
        Evaluation.Values.CanonicalKey(3).ShouldBe("o:3");
        Evaluation.Values.Compare(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)).ShouldBeLessThan(0);
        Evaluation.Values.Compare(true, false).ShouldBeGreaterThan(0);
        Evaluation.Values.Compare(Fixtures.Now, "2026-11-01").ShouldBeGreaterThan(0);
        Evaluation.Values.TryNumber(true, out _).ShouldBeFalse();
        Evaluation.Values.Describe(null).ShouldBe("null");
        Evaluation.Values.FromJson(JsonNode.Parse("1e400")).ShouldBeNull();
    }

    [Fact]
    public void TokenDescriptions()
    {
        new Parsing.Token(Parsing.TokenKind.StringLiteral, "x", "x", 0).Describe().ShouldBe("stringa 'x'");
        new Parsing.Token(Parsing.TokenKind.End, string.Empty, null, 0).Describe().ShouldBe("fine della query");
    }
}
