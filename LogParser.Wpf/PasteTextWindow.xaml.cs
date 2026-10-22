using System.Windows;
using LogParser.Parsers;

namespace LogParser.Wpf;

public partial class PasteTextWindow : Window
{
    public PasteTextWindow(string initialText)
    {
        InitializeComponent();
        Input.Text = initialText;
        Input.Focus();
    }

    public string Text => Input.Text;
    public LogFormat Format => (LogFormat)Math.Max(0, FormatBox.SelectedIndex);

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Input.Text))
        {
            MessageBox.Show(this, "Вставьте хотя бы одну строку лога.", "Пустой текст",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
