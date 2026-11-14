using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mg3.Orchestrator.Abstractions.Http;

namespace Mg3.Orchestrator.Infrastructure.Auth;

/// <summary>Cache in memoria, valida per la durata del processo.</summary>
public sealed class InMemoryTokenCache : ITokenCache
{
    private readonly ConcurrentDictionary<string, AccessToken> _tokens = new(StringComparer.Ordinal);

    public AccessToken? TryGetToken(string cacheKey) => _tokens.TryGetValue(cacheKey, out var token) ? token : null;

    public void StoreToken(string cacheKey, AccessToken token) => _tokens[cacheKey] = token;
}

/// <summary>Cache su disco, opzionale. Il file è creato con permessi 600; le chiavi sono hash SHA-256.</summary>
public sealed class FileTokenCache(string path) : ITokenCache
{
    private readonly InMemoryTokenCache _memory = new();

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mg3orch", "token-cache.json");

    public AccessToken? TryGetToken(string cacheKey)
    {
        if (_memory.TryGetToken(cacheKey) is { } token)
        {
            return token;
        }

        var entries = Load();
        return entries.TryGetValue(Hash(cacheKey), out var stored) ? stored : null;
    }

    public void StoreToken(string cacheKey, AccessToken token)
    {
        _memory.StoreToken(cacheKey, token);
        var entries = Load();
        entries[Hash(cacheKey)] = token;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(entries);
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, json);
            return;
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
        using (var stream = new FileStream(path, options))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private Dictionary<string, AccessToken> Load()
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, AccessToken>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, AccessToken>>(File.ReadAllText(path))
                ?? new Dictionary<string, AccessToken>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, AccessToken>(StringComparer.Ordinal);
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
