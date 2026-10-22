using LogParser.Facade;

namespace LogParser.Wpf.ViewModels;

/// <summary>Одна карточка источника в левой панели: кружок состояния, имя, итог загрузки.</summary>
public sealed class SourceItemViewModel : ObservableObject
{
    private readonly Action<SourceItemViewModel> _enabledChanged;
    private SourceState _state;

    public SourceItemViewModel(SourceStatus status, Action<SourceItemViewModel> enabledChanged)
    {
        Status = status;
        _enabledChanged = enabledChanged;
        Refresh();
    }

    public SourceStatus Status { get; }
    public string Name => Status.Name;
    public string Location => Status.Location;

    public SourceState State
    {
        get => _state;
        private set { if (Set(ref _state, value)) Raise(nameof(Summary)); }
    }

    public int Count => Status.Count;

    public bool Enabled
    {
        get => Status.Enabled;
        set
        {
            if (Status.Enabled == value) return;
            _enabledChanged(this);   // фасад переключит источник и пересчитает общий список
            Raise();
        }
    }

    public string Summary => State switch
    {
        SourceState.Pending => "Ещё не загружен",
        SourceState.Loading => "Загрузка…",
        SourceState.Ok => $"{Plural(Status.Count)} · {Status.Elapsed.TotalMilliseconds:0} мс",
        SourceState.Warning => $"{Plural(Status.Count)} · {Status.Message}",
        _ => Status.Message ?? "Ошибка"
    };

    public void SetLoading() => State = SourceState.Loading;

    public void Refresh()
    {
        State = Status.State;
        Raise(nameof(Summary));
        Raise(nameof(Count));
        Raise(nameof(Enabled));
    }

    private static string Plural(int n)
    {
        var last = n % 100 is >= 11 and <= 14 ? 0 : n % 10;
        var word = last == 1 ? "запись" : last is >= 2 and <= 4 ? "записи" : "записей";
        return $"{n} {word}";
    }
}
