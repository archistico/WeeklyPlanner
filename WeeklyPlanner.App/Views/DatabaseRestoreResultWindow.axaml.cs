using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using WeeklyPlanner.Core.Data;

namespace WeeklyPlanner.App.Views;

public partial class DatabaseRestoreResultWindow : Window
{
    public DatabaseRestoreResultWindow()
    {
        InitializeComponent();
    }

    public DatabaseRestoreResultWindow(DatabaseRestoreStartupResult result)
        : this()
    {
        ArgumentNullException.ThrowIfNull(result);

        TitleText.Text = result.IsSuccess
            ? "Ripristino completato"
            : result.Status == DatabaseRestoreStartupStatus.Blocked
                ? "Ripristino rinviato"
                : "Ripristino non completato";
        MessageText.Text = result.Message;
        MessageBorder.Background = new SolidColorBrush(
            result.IsSuccess
                ? Color.FromRgb(46, 194, 126)
                : Color.FromRgb(192, 28, 40));
        MessageText.Foreground = Brushes.White;
        BackupText.Text = string.IsNullOrWhiteSpace(result.PreRestoreBackupPath)
            ? string.Empty
            : $"Backup preventivo: {result.PreRestoreBackupPath}";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
