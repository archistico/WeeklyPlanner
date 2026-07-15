using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WeeklyPlanner.App.Views;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class DatabaseSafetyWindowLayoutTests
{

    [AvaloniaFact]
    public void Safety_window_loads_real_xaml_headlessly()
    {
        var window = new DatabaseSafetyWindow
        {
            DataContext = new object(),
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("WeeklyPlanner - Backup e ripristino", window.Title);
            Assert.NotNull(window.Content);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public void Restore_result_window_exposes_a_public_parameterless_constructor_for_xaml_loader()
    {
        Assert.NotNull(typeof(DatabaseRestoreResultWindow).GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public void Main_window_exposes_backup_and_restore_entry_point()
    {
        var document = XDocument.Load(GetProjectFile("WeeklyPlanner.App", "Views", "MainWindow.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";

        var button = Assert.Single(
            document.Descendants(avalonia + "Button"),
            element => string.Equals(
                (string?)element.Attribute("Click"),
                "OnOpenDatabaseSafetyClick",
                StringComparison.Ordinal));

        Assert.Contains(
            "Backup",
            (string?)button.Attribute("ToolTip.Tip") ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Safety_window_contains_backup_list_integrity_and_guided_restore()
    {
        var document = XDocument.Load(GetProjectFile("WeeklyPlanner.App", "Views", "DatabaseSafetyWindow.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";

        Assert.Contains(
            document.Descendants(avalonia + "Button"),
            element => string.Equals((string?)element.Attribute("Command"), "{Binding CreateBackupCommand}", StringComparison.Ordinal));
        Assert.Contains(
            document.Descendants(avalonia + "ListBox"),
            element => string.Equals((string?)element.Attribute("ItemsSource"), "{Binding Backups}", StringComparison.Ordinal));
        Assert.Contains(
            document.Descendants(avalonia + "TextBlock"),
            element => string.Equals((string?)element.Attribute("Text"), "{Binding IntegrityText}", StringComparison.Ordinal));
        Assert.Contains(
            document.Descendants(avalonia + "Button"),
            element => string.Equals((string?)element.Attribute("Command"), "{Binding ConfirmRestoreCommand}", StringComparison.Ordinal));
        Assert.Contains(
            document.Descendants(avalonia + "ScrollViewer"),
            element => string.Equals((string?)element.Attribute("IsEnabled"), "{Binding IsNotBusy}", StringComparison.Ordinal));
    }


    [Fact]
    public void Blocked_restore_startup_does_not_open_the_board()
    {
        var source = File.ReadAllText(GetProjectFile("WeeklyPlanner.App", "App.axaml.cs"));

        Assert.Contains(
            "restoreResult.Status == DatabaseRestoreStartupStatus.Blocked",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "desktop.MainWindow = new DatabaseRestoreResultWindow(restoreResult)",
            source,
            StringComparison.Ordinal);
    }

    private static string GetProjectFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(segments.Prepend(current.FullName).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"File non trovato: {Path.Combine(segments)}");
    }
}
