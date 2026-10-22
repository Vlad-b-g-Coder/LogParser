using System.Globalization;
using System.Text.Json;
using LogParser.Models;
using LogParser.Parsers;

namespace LogParser.Sources;

public interface ILogSource
{
    string Name { get; }

    string Location => Name;

    int SkippedLines { get; }

    IEnumerable<LogEntry> Read();
}

/// <summary>Общая часть построчных источников: файл, stdin, вставленный текст.</summary>
public abstract class LineLogSource : ILogSource
{
    protected LineLogSource(ILogLineParser parser) => Parser = parser;

    protected ILogLineParser Parser { get; }

    public abstract string Name { get; }
    public virtual string Location => Name;
    public int SkippedLines { get; private set; }

    protected abstract IEnumerable<string> ReadLines();

    public IEnumerable<LogEntry> Read()
    {
        SkippedLines = 0;
        foreach (var line in ReadLines())
        {
            var entry = Parser.ParseLine(line, Name);
            if (entry is not null) yield return entry;
            else if (!string.IsNullOrWhiteSpace(line) && !Parser.IsIgnorable(line)) SkippedLines++;
        }
    }
}

public sealed class FileLogSource : LineLogSource
{
    private readonly string _path;

    public FileLogSource(string path, ILogLineParser parser) : base(parser) => _path = path;

    public override string Name => $"файл {Path.GetFileName(_path)} [{Parser.FormatName}]";
    public override string Location => Path.GetFullPath(_path);

    protected override IEnumerable<string> ReadLines()
    {
        if (!File.Exists(_path))
            throw new FileNotFoundException($"Файл лога не найден: {_path}", _path);

        // FileShare.ReadWrite: лог можно читать, пока им пользуется другое приложение
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) is not null) yield return line;
    }
}

public sealed class StdinLogSource : LineLogSource
{
    private readonly TextReader _reader;

    public StdinLogSource(ILogLineParser parser, TextReader? reader = null) : base(parser) =>
        _reader = reader ?? Console.In;

    public override string Name => $"stdin [{Parser.FormatName}]";

    protected override IEnumerable<string> ReadLines()
    {
        string? line;
        while ((line = _reader.ReadLine()) is not null) yield return line;
    }
}

/// <summary>Текст, вставленный пользователем. В отличие от stdin его можно перечитывать.</summary>
public sealed class TextLogSource : LineLogSource
{
    private readonly string _text;
    private readonly string _title;

    public TextLogSource(string text, ILogLineParser parser, string title = "вставленный текст") : base(parser)
    {
        _text = text;
        _title = title;
    }

    public override string Name => $"{_title} [{Parser.FormatName}]";

    protected override IEnumerable<string> ReadLines()
    {
        using var reader = new StringReader(_text);
        string? line;
        while ((line = reader.ReadLine()) is not null) yield return line;
    }
}

public sealed class VendorRecord
{
    public long EpochSeconds { get; init; }
    public int Severity { get; init; }
    public string Text { get; init; } = string.Empty;
}

public sealed class VendorMonitoringService
{
    private readonly List<VendorRecord> _store;

    public VendorMonitoringService(IEnumerable<VendorRecord> store) => _store = store.ToList();

    public int PageCount(int pageSize) => (_store.Count + pageSize - 1) / pageSize;

    public VendorRecord[] FetchPage(int page, int pageSize) =>
        _store.Skip(page * pageSize).Take(pageSize).ToArray();

    /// <summary>Демо-данные «стороннего сервиса».</summary>
    public static VendorMonitoringService CreateDemo() => new(new[]
    {
        new VendorRecord { EpochSeconds = 1792663500, Severity = 3, Text = "Vendor: потеряно соединение с шлюзом" },
        new VendorRecord { EpochSeconds = 1792663620, Severity = 1, Text = "Vendor: соединение восстановлено" },
        new VendorRecord { EpochSeconds = 1792663800, Severity = 4, Text = "Vendor: сбой питания узла 7" },
    });
}

public sealed class VendorServiceAdapter : ILogSource
{
    private const int PageSize = 2;
    private readonly VendorMonitoringService _service;

    public VendorServiceAdapter(VendorMonitoringService service) => _service = service;

    public string Name => "сторонний сервис мониторинга";
    public int SkippedLines => 0;

    public IEnumerable<LogEntry> Read()
    {
        for (var page = 0; page < _service.PageCount(PageSize); page++)
            foreach (var rec in _service.FetchPage(page, PageSize))
                yield return Convert(rec);
    }

    private LogEntry Convert(VendorRecord rec) => new(
        DateTimeOffset.FromUnixTimeSeconds(rec.EpochSeconds).UtcDateTime,
        rec.Severity switch
        {
            <= 0 => LogLevel.Debug,
            1 => LogLevel.Info,
            2 => LogLevel.Warning,
            3 => LogLevel.Error,
            _ => LogLevel.Critical
        },
        rec.Text,
        Name);
}

public sealed class PegastSearchAdapter : ILogSource
{
    private readonly string _path;
    private readonly int _maxItems;

    public PegastSearchAdapter(string path, int maxItems = int.MaxValue)
    {
        _path = path;
        _maxItems = maxItems;
    }

    public string Name => $"pegast.ru [{Path.GetFileName(_path)}]";
    public string Location => Path.GetFullPath(_path);
    public int SkippedLines { get; private set; }

    public IEnumerable<LogEntry> Read()
    {
        SkippedLines = 0;
        if (!File.Exists(_path))
            throw new FileNotFoundException($"Ответ pegast.ru не найден: {_path}", _path);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(_path));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Файл не является корректным JSON: {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)) root = data;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("resultItems", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("В ответе pegast.ru нет списка resultItems");

            var count = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (count++ >= _maxItems) yield break;

                LogEntry entry;
                try
                {
                    entry = Convert(item);
                }
                catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
                {
                    SkippedLines++; // один «кривой» тур не должен ронять весь источник
                    continue;
                }
                yield return entry;
            }
        }
    }

    private LogEntry Convert(JsonElement item)
    {
        var date = item.TryGetProperty("date", out var d) && DateTime.TryParse(d.GetString(),
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed : DateTime.MinValue;

        var hotel = item.GetProperty("filtersInfo").GetProperty("mainHotelName").GetString() ?? "?";
        var nights = item.TryGetProperty("nights", out var n) ? n.GetInt32() : 0;
        var money = item.GetProperty("holidayPrice").GetProperty("paymentCurrency");
        var price = money.GetProperty("raw").GetDecimal();
        var symbol = money.TryGetProperty("parts", out var parts) && parts.TryGetProperty("currency", out var cur)
            && cur.TryGetProperty("symbol", out var s) ? s.GetString() : "?";
        var transit = item.TryGetProperty("transitNights", out var t) && t.ValueKind == JsonValueKind.Number
            ? t.GetInt32() : 0;

        var level = transit > 0 ? LogLevel.Warning : LogLevel.Info;
        var note = transit > 0 ? $", ночей в пути: {transit}" : string.Empty;
        return new LogEntry(date, level, $"Тур «{hotel}», {nights} н., {price:N0} {symbol}{note}", Name);
    }
}
