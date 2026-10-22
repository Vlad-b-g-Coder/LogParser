namespace LogParser.Models;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Message, string Source)
{
    public override string ToString() =>
        $"{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level.ToString().ToUpperInvariant(),-8}] {Message} ({Source})";
}

public static class LogLevelParser
{
    public static bool TryParse(string? text, out LogLevel level)
    {
        level = LogLevel.Info;
        switch (text?.Trim().ToLowerInvariant())
        {
            case "debug" or "trace" or "dbg": level = LogLevel.Debug; return true;
            case "info" or "information" or "notice": level = LogLevel.Info; return true;
            case "warn" or "warning": level = LogLevel.Warning; return true;
            case "error" or "err": level = LogLevel.Error; return true;
            case "critical" or "fatal" or "crit": level = LogLevel.Critical; return true;
            default: return false;
        }
    }
}
