using System.Windows;
using System.Windows.Threading;

namespace LogParser.Wpf;

public partial class App : Application
{
    public App() => DispatcherUnhandledException += OnUnhandled;

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Непредвиденная ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
