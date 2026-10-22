using LogParser.Facade;
using LogParser.Models;
using LogParser.Parsers;
using LogParser.Sources;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");

static void PrintStatus(SourceStatus s)
{
    var mark = s.State switch { SourceState.Ok => "[OK]  ", SourceState.Warning => "[!]   ", SourceState.Error => "[ERR] ", _ => "[ .. ]" };
    var tail = s.Message is null ? "" : $" — {s.Message}";
    Console.WriteLine($"  {mark}{s.Name}: {s.Count} записей{tail}");
}


Console.WriteLine("Загрузка из двух источников: JSON-файл и сторонний сервис");
var parser = new LogParserFacade()
    .AddSource(new FileLogSource(Path.Combine(dataDir, "app.json"), new JsonLogParser()))
    .AddSource(new VendorServiceAdapter(VendorMonitoringService.CreateDemo()));

var total = parser.Load(new InlineProgress<SourceStatus>(PrintStatus));
Console.WriteLine($"Всего записей: {total}");
foreach (var entry in parser.Entries) Console.WriteLine("  " + entry);

Console.WriteLine("\nТолько Error и выше:");
foreach (var entry in parser.FilterByLevel(LogLevel.Error)) Console.WriteLine("  " + entry);

Console.WriteLine("\nДобавляем CSV, key=value, stdin и pegast.ru:");
var stdinText = new StringReader(
    "time=2026-10-22T11:05:00 level=WARN msg=\"stdin: диск заполнен на 91%\"\n" +
    "time=2026-10-22T11:06:30 level=FATAL msg=\"stdin: процесс завершён аварийно\"\n");

parser
    .AddSource(new FileLogSource(Path.Combine(dataDir, "app.csv"), new CsvLogParser()))
    .AddSource(new FileLogSource(Path.Combine(dataDir, "app.kv"), new KeyValueLogParser()))
    .AddSource(new StdinLogSource(new KeyValueLogParser(), stdinText))
    .AddSource(new PegastSearchAdapter(Path.Combine(dataDir, "pegast_search.json"), maxItems: 5))
    .AddSource(new FileLogSource(Path.Combine(dataDir, "missing.json"), new JsonLogParser()));

total = parser.Load(new InlineProgress<SourceStatus>(PrintStatus));
Console.WriteLine($"Всего записей: {total}");

Console.WriteLine("\nПоиск «БД» среди Warning и выше:");
foreach (var entry in parser.Query(new LogFilter(LogLevel.Warning, "БД"))) Console.WriteLine("  " + entry);

Console.WriteLine("\nСохраняем отчёт (Warning и выше):");
var reportPath = parser.SaveReport(Path.Combine("reports", "report.txt"), LogLevel.Warning);
Console.WriteLine($"Отчёт: {reportPath}");
Console.WriteLine(File.ReadAllText(reportPath));

file sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
