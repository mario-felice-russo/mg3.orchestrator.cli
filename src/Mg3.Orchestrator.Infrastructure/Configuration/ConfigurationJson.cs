using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mg3.Orchestrator.Infrastructure.Configuration;

/// <summary>Opzioni di serializzazione della configurazione: chiavi case-insensitive, enum come stringhe, chiavi sconosciute vietate.</summary>
public static class ConfigurationJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
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
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FlexibleBooleanConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Accetta booleani anche come stringa (utile per le variabili d'ambiente).</summary>
    private sealed class FlexibleBooleanConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String when bool.TryParse(reader.GetString(), out var value) => value,
            _ => throw new JsonException("Atteso un valore booleano (true/false)."),
        };

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteBooleanValue(value);
        }
    }
}
