using Avalonia.Interactivity;
using Avalonia.Controls;

namespace WeeklyPlanner.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
