using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using LogParser.Models;
using LogParser.Parsers;
using LogParser.Wpf.ViewModels;

namespace LogParser.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    private LogFormat SelectedFormat => (LogFormat)Math.Max(0, FormatBox.SelectedIndex);

    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "Data");

    private async void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите файлы логов",
            Multiselect = true,
            Filter = "Логи (*.json;*.jsonl;*.csv;*.kv;*.log;*.txt)|*.json;*.jsonl;*.csv;*.kv;*.log;*.txt|Все файлы (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
            await _vm.AddFilesAsync(dialog.FileNames, SelectedFormat);
    }

    private async void PasteText_Click(object sender, RoutedEventArgs e)
    {
        var initial = string.Empty;
        try
        {
            if (Clipboard.ContainsText()) initial = Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }

        var dialog = new PasteTextWindow(initial) { Owner = this };
        if (dialog.ShowDialog() == true)
            await _vm.AddTextAsync(dialog.Text, dialog.Format);
    }

    private async void Vendor_Click(object sender, RoutedEventArgs e) => await _vm.AddVendorDemoAsync();

    private async void Pegast_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "JSON-ответ поиска pegast.ru",
            Filter = "JSON (*.json)|*.json|Все файлы (*.*)|*.*",
            InitialDirectory = Directory.Exists(DataDir) ? DataDir : string.Empty
        };
        if (dialog.ShowDialog(this) == true)
            await _vm.AddPegastAsync(dialog.FileName);
    }

    private async void Sample_Click(object sender, RoutedEventArgs e) => await _vm.LoadSamplesAsync(DataDir);

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            await _vm.AddFilesAsync(files.Where(File.Exists), SelectedFormat);
    }


    private async void RefreshAll_Click(object sender, RoutedEventArgs e) => await _vm.ReloadAllAsync();

    private async void ReloadSource_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SourceItemViewModel item)
            await _vm.ReloadAsync(item);
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SourceItemViewModel item)
            _vm.Remove(item);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Sources.Count == 0) return;
        var answer = MessageBox.Show(this, "Убрать все источники из списка?", "Очистить",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) _vm.ClearAll();
    }


    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<LogLevel>(tag, out var level))
            _vm.SetMinLevel(level);
    }

    private void ResetFilters_Click(object sender, RoutedEventArgs e) => _vm.ResetFilters();

    private void CopyEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedEntry is null) return;
        try
        {
            Clipboard.SetText(_vm.SelectedEntry.ToString());
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show(this, "Буфер обмена занят, попробуйте ещё раз.", "Копирование",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Visible.Count == 0)
        {
            MessageBox.Show(this, "Нет записей для экспорта.", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить отчёт",
            FileName = $"report-{DateTime.Now:yyyyMMdd-HHmm}",
            Filter = "Текстовый отчёт (*.txt)|*.txt|CSV (*.csv)|*.csv|JSON (*.json)|*.json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            _vm.Export(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Не удалось сохранить файл", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
