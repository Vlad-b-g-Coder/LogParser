using System.Collections.ObjectModel;
using System.IO;
using LogParser.Facade;
using LogParser.Models;
using LogParser.Parsers;
using LogParser.Sources;

namespace LogParser.Wpf.ViewModels;

public sealed record LevelOption(string Title, LogLevel? Level);

public sealed class MainViewModel : ObservableObject
{
    private readonly LogParserFacade _facade = new();

    private LevelOption _selectedLevel;
    private string _searchText = string.Empty;
    private DateTime? _dateFrom;
    private DateTime? _dateTo;
    private IReadOnlyList<LogEntry> _visible = Array.Empty<LogEntry>();
    private LogEntry? _selectedEntry;
    private int _total;
    private int _busy;
    private string _statusText = "Добавьте источник логов: файл, текст, сервис или pegast.ru";
    private IReadOnlyDictionary<LogLevel, int> _counts = LogParserFacade.CountByLevel(Array.Empty<LogEntry>());

    public MainViewModel()
    {
        Levels = new[]
        {
            new LevelOption("Все уровни", null),
            new LevelOption("Info и выше", LogLevel.Info),
            new LevelOption("Warning и выше", LogLevel.Warning),
            new LevelOption("Error и выше", LogLevel.Error),
            new LevelOption("Только Critical", LogLevel.Critical)
        };
        _selectedLevel = Levels[0];
    }

    public ObservableCollection<SourceItemViewModel> Sources { get; } = new();
    public IReadOnlyList<LevelOption> Levels { get; }

    // ---------- фильтры ----------

    public LevelOption SelectedLevel
    {
        get => _selectedLevel;
        set { if (Set(ref _selectedLevel, value)) ApplyFilter(); }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) ApplyFilter(); }
    }

    public DateTime? DateFrom
    {
        get => _dateFrom;
        set { if (Set(ref _dateFrom, value)) ApplyFilter(); }
    }

    public DateTime? DateTo
    {
        get => _dateTo;
        set { if (Set(ref _dateTo, value)) ApplyFilter(); }
    }

    public void SetMinLevel(LogLevel level) =>
        SelectedLevel = Levels.FirstOrDefault(l => l.Level == level) ?? Levels[0];

    public void ResetFilters()
    {
        _selectedLevel = Levels[0];
        _searchText = string.Empty;
        _dateFrom = _dateTo = null;
        Raise(nameof(SelectedLevel));
        Raise(nameof(SearchText));
        Raise(nameof(DateFrom));
        Raise(nameof(DateTo));
        ApplyFilter();
    }

    // ---------- результат ----------

    public IReadOnlyList<LogEntry> Visible
    {
        get => _visible;
        private set => Set(ref _visible, value);
    }

    public LogEntry? SelectedEntry
    {
        get => _selectedEntry;
        set => Set(ref _selectedEntry, value);
    }

    public int DebugCount => _counts[LogLevel.Debug];
    public int InfoCount => _counts[LogLevel.Info];
    public int WarningCount => _counts[LogLevel.Warning];
    public int ErrorCount => _counts[LogLevel.Error];
    public int CriticalCount => _counts[LogLevel.Critical];

    public string CountText => $"Показано {Visible.Count} из {_total}";
    public bool IsEmpty => Sources.Count == 0;
    public bool NoMatches => Sources.Count > 0 && Visible.Count == 0;

    // ---------- состояние ----------

    public bool IsBusy => _busy > 0;
    public bool IsIdle => _busy == 0;

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    /// <summary>Общий «светофор»: красный, если упал хоть один источник; жёлтый, если есть замечания.</summary>
    public SourceState OverallState
    {
        get
        {
            if (Sources.Count == 0) return SourceState.Pending;
            if (Sources.Any(s => s.State == SourceState.Loading)) return SourceState.Loading;
            if (Sources.Any(s => s.State == SourceState.Error)) return SourceState.Error;
            if (Sources.Any(s => s.State == SourceState.Warning)) return SourceState.Warning;
            return SourceState.Ok;
        }
    }

    public string OverallText
    {
        get
        {
            if (Sources.Count == 0) return "Источников нет";
            var ok = Sources.Count(s => s.State == SourceState.Ok);
            var warn = Sources.Count(s => s.State == SourceState.Warning);
            var err = Sources.Count(s => s.State == SourceState.Error);
            var text = $"Источников: {Sources.Count} · исправно {ok}";
            if (warn > 0) text += $" · с замечаниями {warn}";
            if (err > 0) text += $" · недоступно {err}";
            return text;
        }
    }

    // ---------- действия ----------

    public async Task AddSourceAsync(ILogSource source)
    {
        var existing = Sources.FirstOrDefault(s => s.Location == source.Location && source.Location != source.Name);
        if (existing is not null)
        {
            StatusText = $"«{existing.Name}» уже добавлен, перечитываю";
            await ReloadAsync(existing);
            return;
        }

        var item = new SourceItemViewModel(_facade.Add(source), OnEnabledChanged);
        Sources.Add(item);
        Raise(nameof(IsEmpty));
        await ReloadAsync(item);
    }

    public async Task AddFilesAsync(IEnumerable<string> paths, LogFormat format)
    {
        foreach (var path in paths)
        {
            var actual = format == LogFormat.Auto ? LogFormatFactory.Detect(path) : format;
            await AddSourceAsync(new FileLogSource(path, LogFormatFactory.Create(actual)));
        }
    }

    public Task AddTextAsync(string text, LogFormat format)
    {
        var actual = format == LogFormat.Auto
            ? LogFormatFactory.DetectText(text.Split('\n'))
            : format;
        return AddSourceAsync(new TextLogSource(text, LogFormatFactory.Create(actual)));
    }

    public Task AddVendorDemoAsync() =>
        AddSourceAsync(new VendorServiceAdapter(VendorMonitoringService.CreateDemo()));

    public Task AddPegastAsync(string path) => AddSourceAsync(new PegastSearchAdapter(path));

    public async Task LoadSamplesAsync(string dataDir)
    {
        await AddFilesAsync(new[] { "app.json", "app.csv", "app.kv" }.Select(f => Path.Combine(dataDir, f)), LogFormat.Auto);
        await AddSourceAsync(new PegastSearchAdapter(Path.Combine(dataDir, "pegast_search.json")));
        await AddVendorDemoAsync();
        await AddFilesAsync(new[] { Path.Combine(dataDir, "missing.json") }, LogFormat.Auto); // показывает красный кружок
    }

    public async Task ReloadAllAsync()
    {
        foreach (var item in Sources.ToList()) await ReloadAsync(item);
    }

    public async Task ReloadAsync(SourceItemViewModel item)
    {
        BeginBusy();
        try
        {
            item.SetLoading();
            RaiseOverall();
            await Task.Run(() => _facade.Reload(item.Status));
            item.Refresh();
            StatusText = item.State == SourceState.Error
                ? $"Не удалось загрузить «{item.Name}»"
                : $"Загружено: «{item.Name}»";
        }
        finally
        {
            EndBusy();
        }
        ApplyFilter();
    }

    public void Remove(SourceItemViewModel item)
    {
        _facade.RemoveSource(item.Status);
        Sources.Remove(item);
        StatusText = $"Источник «{item.Name}» удалён";
        ApplyFilter();
    }

    public void ClearAll()
    {
        _facade.Clear();
        Sources.Clear();
        StatusText = "Список источников очищен";
        ApplyFilter();
    }

    /// <summary>Сохраняет то, что сейчас видно в таблице (с учётом фильтров).</summary>
    public string Export(string path)
    {
        var saved = _facade.Export(path, Visible, title: $"Отчёт по логам, записей: {Visible.Count}");
        StatusText = $"Сохранено {Visible.Count} записей: {saved}";
        return saved;
    }

    // ---------- внутреннее ----------

    private void OnEnabledChanged(SourceItemViewModel item)
    {
        _facade.SetEnabled(item.Status, !item.Status.Enabled);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var to = DateTo?.Date.AddDays(1).AddTicks(-1); // «по» включает весь выбранный день
        var baseList = _facade.Query(new LogFilter(null, SearchText, DateFrom?.Date, to));
        _counts = LogParserFacade.CountByLevel(baseList);
        _total = _facade.Entries.Count;

        var min = SelectedLevel.Level;
        Visible = min is null ? baseList : baseList.Where(e => e.Level >= min).ToList();

        Raise(nameof(DebugCount));
        Raise(nameof(InfoCount));
        Raise(nameof(WarningCount));
        Raise(nameof(ErrorCount));
        Raise(nameof(CriticalCount));
        Raise(nameof(CountText));
        Raise(nameof(IsEmpty));
        Raise(nameof(NoMatches));
        RaiseOverall();
    }

    private void RaiseOverall()
    {
        Raise(nameof(OverallState));
        Raise(nameof(OverallText));
    }

    private void BeginBusy()
    {
        _busy++;
        Raise(nameof(IsBusy));
        Raise(nameof(IsIdle));
    }

    private void EndBusy()
    {
        _busy--;
        Raise(nameof(IsBusy));
        Raise(nameof(IsIdle));
    }
}
