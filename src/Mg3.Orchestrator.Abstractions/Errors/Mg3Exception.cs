namespace Mg3.Orchestrator.Abstractions.Errors;

public static class ExitCodes
{
    public const int Success = 0;
    public const int Generic = 1;
    public const int InvalidUsage = 2;
    public const int Authentication = 3;
    public const int AccessDenied = 4;
    public const int Query = 5;
    public const int Timeout = 6;
    public const int PartialSuccess = 7;
    public const int Unavailable = 8;
    public const int Cancelled = 130;
}

/// <summary>Errore applicativo con exit code associato.</summary>
public class Mg3Exception : Exception
{
    public Mg3Exception(int exitCode, string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        ExitCode = exitCode;
        Code = code;
    }

    public Mg3Exception()
        : this(ExitCodes.Generic, "generic", "Errore non classificato.")
    {
    }

    public Mg3Exception(string message)
        : this(ExitCodes.Generic, "generic", message)
    {
    }

    public Mg3Exception(string message, Exception inner)
        : this(ExitCodes.Generic, "generic", message, inner)
    {
    }

    public int ExitCode { get; }

    public string Code { get; }
}

public sealed class ConfigurationException(string message) : Mg3Exception(ExitCodes.InvalidUsage, "invalid_usage", message);

public sealed class AuthenticationException(string message, Exception? inner = null) : Mg3Exception(ExitCodes.Authentication, "authentication_failed", message, inner);

public sealed class AccessDeniedException(string code, string message) : Mg3Exception(ExitCodes.AccessDenied, code, message);

public sealed class OrchestratorUnavailableException(string message, Exception? inner = null) : Mg3Exception(ExitCodes.Unavailable, "unavailable", message, inner);

public sealed class GlobalTimeoutException(string message) : Mg3Exception(ExitCodes.Timeout, "timeout", message);

public sealed class ReadOnlyViolationException(string message) : Mg3Exception(ExitCodes.InvalidUsage, "read_only_violation", message);

/// <summary>Errore di parsing o semantico della DSL.</summary>
public sealed class QueryException : Mg3Exception
{
    public QueryException(string message, int position = -1)
        : base(ExitCodes.Query, "query_error", position >= 0 ? $"{message} (posizione {position})" : message)
    {
        Position = position;
    }

    public int Position { get; }
}
