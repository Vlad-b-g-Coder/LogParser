using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogParser.Models;

namespace LogParser.Parsers;

public interface ILogLineParser
{
    string FormatName { get; }
    LogEntry? ParseLine(string line, string source);

    /// <summary>, которые не являются записями, но и ошибкой не считаются (например, заголовок CSV).</summary>
    bool IsIgnorable(string line) => false;
}

public sealed class JsonLogParser : ILogLineParser
{
    public string FormatName => "JSON";

    public LogEntry? ParseLine(string line, string source)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var time = Get(root, "time", "timestamp", "ts");
            var level = Get(root, "level", "severity");
            var message = Get(root, "message", "msg");

            if (!DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts)) return null;
            if (!LogLevelParser.TryParse(level, out var lvl)) return null;
            return new LogEntry(ts, lvl, message ?? string.Empty, source);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Get(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
            if (obj.TryGetProperty(name, out var value))
                return value.ToString();
        return null;
    }
}

public sealed class KeyValueLogParser : ILogLineParser
{
    private static readonly Regex Pair = new(@"(\w+)=(?:""([^""]*)""|(\S+))", RegexOptions.Compiled);

    public string FormatName => "key=value";

    public LogEntry? ParseLine(string line, string source)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Pair.Matches(line))
            values[m.Groups[1].Value] = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;

        values.TryGetValue("time", out var time);
        values.TryGetValue("level", out var level);
        if (!values.TryGetValue("msg", out var message)) values.TryGetValue("message", out message);

        if (!DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts)) return null;
        if (!LogLevelParser.TryParse(level, out var lvl)) return null;
        return new LogEntry(ts, lvl, message ?? string.Empty, source);
    }
}

public sealed class CsvLogParser : ILogLineParser
{
    public string FormatName => "CSV";

    public bool IsIgnorable(string line)
    {
        var first = SplitCsv(line)[0].Trim().ToLowerInvariant();
        return first is "timestamp" or "time" or "date" or "datetime";
    }

    public LogEntry? ParseLine(string line, string source)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var fields = SplitCsv(line);
        if (fields.Count < 3) return null;

        if (!DateTime.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts)) return null;
        if (!LogLevelParser.TryParse(fields[1], out var lvl)) return null;
        return new LogEntry(ts, lvl, fields[2], source);
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else current.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { result.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        result.Add(current.ToString());
        return result;
    }
}


public enum LogFormat
{
    Auto = 0,
    Json = 1,
    KeyValue = 2,
    Csv = 3
}

public static class LogFormatFactory
{
    public static ILogLineParser Create(LogFormat format) => format switch
    {
        LogFormat.Json => new JsonLogParser(),
        LogFormat.Csv => new CsvLogParser(),
        LogFormat.KeyValue => new KeyValueLogParser(),
        _ => throw new ArgumentException("Для формата Auto сначала вызовите Detect", nameof(format))
    };

    public static LogFormat Detect(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".json" or ".jsonl" or ".ndjson": return LogFormat.Json;
            case ".csv": return LogFormat.Csv;
            case ".kv": return LogFormat.KeyValue;
        }

        try
        {
            return DetectText(File.ReadLines(path).Take(30));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LogFormat.KeyValue;
        }
    }

    public static LogFormat DetectText(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('{')) return LogFormat.Json;
            if (Regex.IsMatch(line, @"\b(time|level|msg|message)=", RegexOptions.IgnoreCase)) return LogFormat.KeyValue;
            if (line.Contains(',')) return LogFormat.Csv;
        }
        return LogFormat.KeyValue;
    }
}
