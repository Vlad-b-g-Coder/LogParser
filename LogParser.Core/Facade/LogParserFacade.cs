using System.Diagnostics;
using System.Text;
using LogParser.Models;
using LogParser.Sources;

namespace LogParser.Facade;

public enum SourceState
{
    Pending,   // ещё не загружался
    Loading,   // идёт загрузка
    Ok,        // всё прочитано без замечаний
    Warning,   // прочитано, но есть пропущенные строки или источник пуст
    Error      // источник недоступен
}

public sealed class SourceStatus
{
    internal SourceStatus(ILogSource source) => Source = source;

    public ILogSource Source { get; }
    public string Name => Source.Name;
    public string Location => Source.Location;
    public SourceState State { get; internal set; } = SourceState.Pending;
    public int Count { get; internal set; }
    public int Skipped { get; internal set; }
    public string? Message { get; internal set; }
    public TimeSpan Elapsed { get; internal set; }
    public bool Enabled { get; internal set; } = true;

    internal List<LogEntry> Entries { get; set; } = new();
}

public sealed record LogFilter(
    LogLevel? MinLevel = null,
    string? Text = null,
    DateTime? From = null,
    DateTime? To = null);

public enum ExportFormat { Text, Csv, Json }

public sealed class LogParserFacade
{
    private readonly List<SourceStatus> _sources = new();
    private readonly object _gate = new();
    private List<LogEntry>? _cache;

    public IReadOnlyList<SourceStatus> Sources => _sources;

    /// <summary>Записи всех включённых и успешно загруженных источников, по времени.</summary>
    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _cache ??= _sources
                    .Where(s => s.Enabled && s.State is SourceState.Ok or SourceState.Warning)
                    .SelectMany(s => s.Entries)
                    .OrderBy(e => e.Timestamp)
                    .ToList();
            }
        }
    }

    public LogParserFacade AddSource(ILogSource source)
    {
        Add(source);
        return this;
    }

    public SourceStatus Add(ILogSource source)
    {
        var status = new SourceStatus(source);
        lock (_gate)
        {
            _sources.Add(status);
            _cache = null;
        }
        return status;
    }

    public bool RemoveSource(SourceStatus status)
    {
        lock (_gate)
        {
            _cache = null;
            return _sources.Remove(status);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _sources.Clear();
            _cache = null;
        }
    }

    public void SetEnabled(SourceStatus status, bool enabled)
    {
        lock (_gate)
        {
            status.Enabled = enabled;
            _cache = null;
        }
    }

    /// <summary>Загружает все источники. Сбой одного не мешает остальным.</summary>
    public int Load(IProgress<SourceStatus>? progress = null, CancellationToken cancellationToken = default)
    {
        foreach (var status in _sources.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reload(status);
            progress?.Report(status);
        }
        return Entries.Count;
    }

    /// <summary>Перечитывает один источник и обновляет его состояние.</summary>
    public SourceStatus Reload(SourceStatus status)
    {
        var sw = Stopwatch.StartNew();
        List<LogEntry> entries;
        SourceState state;
        string? message = null;
        var skipped = 0;

        try
        {
            entries = status.Source.Read().ToList();
            skipped = status.Source.SkippedLines;
            if (skipped > 0)
            {
                state = SourceState.Warning;
                message = $"пропущено строк: {skipped}";
            }
            else if (entries.Count == 0)
            {
                state = SourceState.Warning;
                message = "записей не найдено";
            }
            else state = SourceState.Ok;
        }
        catch (Exception ex)
        {
            entries = new List<LogEntry>();
            state = SourceState.Error;
            message = ex.Message;
        }

        lock (_gate)
        {
            status.Entries = entries;
            status.Count = entries.Count;
            status.Skipped = skipped;
            status.Message = message;
            status.State = state;
            status.Elapsed = sw.Elapsed;
            _cache = null;
        }
        return status;
    }

    public IReadOnlyList<LogEntry> FilterByLevel(LogLevel minLevel) =>
        Entries.Where(e => e.Level >= minLevel).ToList();

    /// <summary>Фильтр по уровню, подстроке (в сообщении и источнике) и интервалу времени.</summary>
    public IReadOnlyList<LogEntry> Query(LogFilter filter)
    {
        IEnumerable<LogEntry> q = Entries;
        if (filter.MinLevel is { } min) q = q.Where(e => e.Level >= min);
        if (filter.From is { } from) q = q.Where(e => e.Timestamp >= from);
        if (filter.To is { } to) q = q.Where(e => e.Timestamp <= to);
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var text = filter.Text.Trim();
            q = q.Where(e => e.Message.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                             e.Source.Contains(text, StringComparison.OrdinalIgnoreCase));
        }
        return q.ToList();
    }

    public static IReadOnlyDictionary<LogLevel, int> CountByLevel(IEnumerable<LogEntry> entries)
    {
        var result = Enum.GetValues<LogLevel>().ToDictionary(l => l, _ => 0);
        foreach (var e in entries) result[e.Level]++;
        return result;
    }

    public string SaveReport(string path, LogLevel minLevel = LogLevel.Debug) =>
        Export(path, FilterByLevel(minLevel), ExportFormat.Text, $"Отчёт по логам, уровень не ниже {minLevel}");

    /// <summary>Сохраняет записи в файл. Формат по умолчанию выбирается по расширению (.csv, .json, иначе текст).</summary>
    public string Export(string path, IReadOnlyList<LogEntry> entries, ExportFormat? format = null, string? title = null)
    {
        format ??= Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" => ExportFormat.Csv,
            ".json" => ExportFormat.Json,
            _ => ExportFormat.Text
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var content = format switch
        {
            ExportFormat.Csv => ReportWriter.ToCsv(entries),
            ExportFormat.Json => ReportWriter.ToJson(entries),
            _ => ReportWriter.ToText(entries, title ?? "Отчёт по логам", Entries.Count)
        };
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return Path.GetFullPath(path);
    }
}

public static class ReportWriter
{
    public static string ToText(IReadOnlyList<LogEntry> entries, string title, int totalCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine($"Записей: {entries.Count} из {totalCount}");
        sb.AppendLine();
        foreach (var group in entries.GroupBy(e => e.Level).OrderBy(g => g.Key))
            sb.AppendLine($"  {group.Key,-9}: {group.Count()}");
        sb.AppendLine();
        foreach (var entry in entries) sb.AppendLine(entry.ToString());
        return sb.ToString();
    }

    public static string ToCsv(IReadOnlyList<LogEntry> entries)
    {
        var sb = new StringBuilder("timestamp,level,message,source\r\n");
        foreach (var e in entries)
            sb.Append(e.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss")).Append(',')
              .Append(e.Level.ToString().ToUpperInvariant()).Append(',')
              .Append(Quote(e.Message)).Append(',')
              .Append(Quote(e.Source)).Append("\r\n");
        return sb.ToString();
    }

    public static string ToJson(IReadOnlyList<LogEntry> entries)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        return System.Text.Json.JsonSerializer.Serialize(entries.Select(e => new
        {
            time = e.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
            level = e.Level.ToString().ToUpperInvariant(),
            message = e.Message,
            source = e.Source
        }), options);
    }

    private static string Quote(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
