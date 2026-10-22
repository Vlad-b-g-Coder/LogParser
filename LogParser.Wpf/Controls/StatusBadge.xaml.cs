using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LogParser.Facade;

namespace LogParser.Wpf.Controls;

/// <summary>Круглый индикатор состояния: зелёная галочка, жёлтый «!», красный крест, серая точка, крутилка.</summary>
public partial class StatusBadge : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(SourceState), typeof(StatusBadge),
        new PropertyMetadata(SourceState.Pending, (d, _) => ((StatusBadge)d).Update()));

    public StatusBadge() => InitializeComponent();

    public SourceState State
    {
        get => (SourceState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Update();

    private void Update()
    {
        if (Circle is null) return; // вызов до InitializeComponent

        var (brushKey, tip) = State switch
        {
            SourceState.Ok => ("OkBrush", "Источник работает"),
            SourceState.Warning => ("WarningBrush", "Загружено с замечаниями"),
            SourceState.Error => ("ErrorBrush", "Источник недоступен"),
            SourceState.Loading => ("LoadingBrush", "Идёт загрузка…"),
            _ => ("PendingBrush", "Ещё не проверялся")
        };
        Circle.Fill = Application.Current?.TryFindResource(brushKey) as Brush ?? Brushes.Gray;
        ToolTip = tip;

        CheckMark.Visibility = Show(State == SourceState.Ok);
        CrossMark.Visibility = Show(State == SourceState.Error);
        BangMark.Visibility = BangDot.Visibility = Show(State == SourceState.Warning);
        PendingDot.Visibility = Show(State == SourceState.Pending);
        Spinner.Visibility = Show(State == SourceState.Loading);

        if (State == SourceState.Loading)
        {
            Spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
            {
                RepeatBehavior = RepeatBehavior.Forever
            });
        }
        else
        {
            Spin.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
