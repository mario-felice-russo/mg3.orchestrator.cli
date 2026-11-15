using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mg3.Orchestrator.Infrastructure.Configuration;

/// <summary>Opzioni JSON del file di configurazione: chiavi case-insensitive, enum come stringhe, chiavi sconosciute vietate.</summary>
public static class ConfigurationJson
{
    public static JsonSerializerOptions ReadOptions { get; } = Create(ignoreNulls: false);

    /// <summary>Per scrivere il file (init, config validate): indentato, senza valori null.</summary>
    public static JsonSerializerOptions WriteOptions { get; } = Create(ignoreNulls: true);

    private static JsonSerializerOptions Create(bool ignoreNulls)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = ignoreNulls ? JsonIgnoreCondition.WhenWritingNull : JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
