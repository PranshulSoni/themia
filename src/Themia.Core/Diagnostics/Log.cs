namespace Themia.Core.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

public readonly record struct LogRecord(DateTimeOffset At, LogLevel Level, string Scope, string Message, Exception? Error);

/// <summary>
/// Minimal sink-based log. Nothing here writes to disk by default — the host installs a sink. The
/// point is that no error path is allowed to be silent: "handled" means logged and, where the user
/// would notice, surfaced.
/// </summary>
public static class Log
{
    private static readonly List<Action<LogRecord>> Sinks = [];
    private static readonly Lock Gate = new();

    public static void AddSink(Action<LogRecord> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (Gate)
        {
            Sinks.Add(sink);
        }
    }

    public static void Debug(string scope, string message) => Write(LogLevel.Debug, scope, message, null);

    public static void Info(string scope, string message) => Write(LogLevel.Info, scope, message, null);

    public static void Warn(string scope, string message, Exception? error = null) => Write(LogLevel.Warn, scope, message, error);

    public static void Error(string scope, string message, Exception? error = null) => Write(LogLevel.Error, scope, message, error);

    private static void Write(LogLevel level, string scope, string message, Exception? error)
    {
        var record = new LogRecord(DateTimeOffset.Now, level, scope, message, error);

        Action<LogRecord>[] snapshot;
        lock (Gate)
        {
            snapshot = [.. Sinks];
        }

        foreach (var sink in snapshot)
        {
            try
            {
                sink(record);
            }
            catch (Exception ex)
            {
                // A failing sink must never take down the caller's error path, and must never
                // recurse back into Log. Last resort only.
                System.Diagnostics.Debug.WriteLine($"[themia] log sink threw: {ex}");
            }
        }
    }
}
