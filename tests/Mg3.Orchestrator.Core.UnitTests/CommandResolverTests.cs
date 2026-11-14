using Mg3.Orchestrator.Abstractions.Catalog;
using Mg3.Orchestrator.Abstractions.Errors;
using Mg3.Orchestrator.Abstractions.Http;
using Mg3.Orchestrator.Core.Commands;
using Mg3.Orchestrator.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Mg3.Orchestrator.Core.UnitTests;

public sealed class CommandResolverTests
{
    private static readonly ICommandResolver Resolver = new ServiceCollection().AddMg3Orchestrator(CoreHost.Profile()).BuildServiceProvider().GetRequiredService<ICommandResolver>();

    private static ResolvedCommand Resolve(string command, Dictionary<string, string>? args = null) => Resolver.Resolve(command, args ?? [], Fixtures.Now);

    [Fact]
    public void ResolvesListWithOptionsAndSince()
    {
        var resolved = Resolve("jobs list", new() { ["filter"] = "State eq 'Faulted'", ["top"] = "5", ["skip"] = "1", ["select"] = "Id", ["orderby"] = "Id", ["expand"] = "Robot", ["since"] = "24h" });
        resolved.Kind.ShouldBe(CommandKind.List);
        resolved.Paginate.ShouldBeTrue();
        resolved.Folder!.Path.ShouldBe("Shared");
        resolved.Options.ShouldBe(new ODataQueryOptions
        {
            Filter = "(State eq 'Faulted') and (CreationTime ge 2026-11-01T12:00:00.000Z)",
            Top = 5,
            Skip = 1,
            Select = "Id",
            OrderBy = "Id",
            Expand = "Robot",
            Count = true,
        });
    }

    [Fact]
    public void DefaultActionIsListOrGet()
    {
        Resolve("jobs").Kind.ShouldBe(CommandKind.List);
        Resolve("stats jobs").Kind.ShouldBe(CommandKind.Function);
        Resolve("jobstats").Path.ShouldBe("api/Stats/GetJobsStats");
        Resolve("license").Folder.ShouldBeNull();
    }

    [Fact]
    public void ResolvesCount()
    {
        var resolved = Resolve("queues count");
        resolved.Kind.ShouldBe(CommandKind.Count);
        resolved.Options.Top.ShouldBe(0);
        resolved.Options.Count.ShouldBeTrue();
    }

    [Theory]
    [InlineData("jobs get 42", CommandKind.GetByPath, "odata/Jobs(42)", null)]
    [InlineData("packages get My.Package", CommandKind.GetByPath, "odata/Processes('My.Package')", null)]
    [InlineData("settings get \"Abp.Net.Mail.Smtp\"", CommandKind.GetByPath, "odata/Settings('Abp.Net.Mail.Smtp')", null)]
    [InlineData("jobs get 00000000-0000-0000-0000-000000000003", CommandKind.GetByFilter, "odata/Jobs", "Key eq 00000000-0000-0000-0000-000000000003")]
    [InlineData("releases get \"O'Brien Report\"", CommandKind.GetByFilter, "odata/Releases", "Name eq 'O''Brien Report'")]
    public void ResolvesGet(string command, CommandKind kind, string path, string? filter)
    {
        var resolved = Resolve(command);
        resolved.Kind.ShouldBe(kind);
        resolved.Path.ShouldBe(path);
        resolved.Options.Filter.ShouldBe(filter);
    }

    [Fact]
    public void GetAcceptsKeyArgument()
    {
        Resolve("jobs get", new() { ["key"] = "7" }).Path.ShouldBe("odata/Jobs(7)");
    }

    [Fact]
    public void ResolvesExtraActions()
    {
        var resolved = Resolve("packages versions It's.Mine");
        resolved.Path.ShouldBe("odata/Processes/UiPath.Server.Configuration.OData.GetProcessVersions(processId='It''s.Mine')");
        resolved.Folder.ShouldBeNull();
    }

    [Fact]
    public void ResolvesRawOData()
    {
        var resolved = Resolve("odata get /odata/QueueDefinitions", new() { ["folder"] = "12", ["top"] = "3" });
        resolved.Kind.ShouldBe(CommandKind.Raw);
        resolved.Path.ShouldBe("odata/QueueDefinitions");
        resolved.Folder!.Id.ShouldBe(12);
        resolved.Options.Top.ShouldBe(3);
    }

    [Theory]
    [InlineData("", "vuoto")]
    [InlineData("pippo list", "Risorsa sconosciuta")]
    [InlineData("jobs delete", "non disponibile")]
    [InlineData("jobs get", "richiede una chiave")]
    [InlineData("packages versions", "richiede una chiave")]
    [InlineData("odata get https://evil.test/x", "non ammesso")]
    [InlineData("odata get odata/../x", "non ammesso")]
    [InlineData("odata get odata/Jobs?$top=1", "non ammesso")]
    [InlineData("odata get other/Jobs", "non ammesso")]
    [InlineData("odata list odata/Jobs", "Sintassi")]
    [InlineData("jobs get \"aperta", "Virgolette")]
    [InlineData("queueitemevents get Nome", "solo per id numerico")]
    public void RejectsInvalidCommands(string command, string fragment)
    {
        var error = Should.Throw<Mg3Exception>(() => Resolve(command));
        error.ExitCode.ShouldBe(ExitCodes.InvalidUsage);
        error.Message.ShouldContain(fragment);
    }

    [Theory]
    [InlineData("since", "ieri", "Durata non valida")]
    [InlineData("top", "-1", "intero non negativo")]
    public void RejectsInvalidArguments(string name, string value, string fragment)
    {
        Should.Throw<ConfigurationException>(() => Resolve("jobs list", new() { [name] = value })).Message.ShouldContain(fragment);
    }

    [Fact]
    public void SinceRequiresTimeField()
    {
        Should.Throw<ConfigurationException>(() => Resolve("robots list", new() { ["since"] = "1d" })).Message.ShouldContain("campo temporale");
    }

    [Theory]
    [InlineData("1500ms", 1.5)]
    [InlineData("30s", 30)]
    [InlineData("2m", 120)]
    [InlineData("1h", 3600)]
    [InlineData("1d", 86400)]
    [InlineData("1w", 604800)]
    public void ParsesDurations(string value, double seconds) =>
        CommandArguments.ParseDuration(value).TotalSeconds.ShouldBe(seconds);

    [Fact]
    public void RejectsUnknownArguments()
    {
        Should.Throw<ConfigurationException>(() => CommandArguments.Validate(new Dictionary<string, string> { ["pippo"] = "1" }, "c")).Message.ShouldContain("pippo");
    }

    [Fact]
    public void TokenizeRespectsQuotes()
    {
        CommandResolver.Tokenize("a  'b c' \"d\"").ShouldBe(["a", "b c", "d"]);
    }

    [Fact]
    public void SupportsPushdownOnlyForLists()
    {
        Resolve("jobs list").SupportsPushdown.ShouldBeTrue();
        Resolve("jobs get 1").SupportsPushdown.ShouldBeFalse();
        Resolve("queuestats").SupportsPushdown.ShouldBeFalse();
        Resolve("queuestats").Resource!.Shape.ShouldBe(PayloadShape.ODataCollection);
    }
}
