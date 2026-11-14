using System.Text.Json.Nodes;

namespace Mg3.Orchestrator.Tests.Shared;

/// <summary>Accesso alle fixture JSON condivise tra i progetti di test.</summary>
internal static class Fixtures
{
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    public static string Text(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    public static JsonObject Object(string name) => JsonNode.Parse(Text(name))!.AsObject();

    public static IReadOnlyList<JsonNode?> Rows(string name) =>
        Object(name)["value"]!.AsArray().Select(n => n?.DeepClone()).ToArray();
}
