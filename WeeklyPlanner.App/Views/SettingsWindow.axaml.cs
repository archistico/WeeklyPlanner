using Avalonia.Controls;
using Avalonia.Interactivity;
using WeeklyPlanner.App.ViewModels;

namespace WeeklyPlanner.App.Views;

public partial class SettingsWindow : Window
{
    public BoardConfigurationViewModel? BoardConfigurationViewModel { get; init; }

    public SettingsWindow()
    {
        InitializeComponent();
    }

    private async void OnOpenBoardConfigurationClick(object? sender, RoutedEventArgs e)
    {
        if (BoardConfigurationViewModel is null)
        {
            return;
        }

        var window = new BoardConfigurationWindow
        {
            DataContext = BoardConfigurationViewModel,
        };
        await window.ShowDialog(this);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
