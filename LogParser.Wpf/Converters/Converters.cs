using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LogParser.Models;

namespace LogParser.Wpf.Converters;

/// <summary>Уровень записи → цвет кружка (кисти лежат в App.xaml).</summary>
public sealed class LevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is LogLevel level ? $"{level}Brush" : "PendingBrush";
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Не null → Visible, null → Collapsed.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
