using Avalonia.Controls;
using Avalonia.Interactivity;
using WeeklyPlanner.App.ViewModels;

namespace WeeklyPlanner.App.Views;

public partial class BoardConfigurationWindow : Window
{
    private bool _loaded;

    public BoardConfigurationWindow()
    {
        InitializeComponent();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_loaded || DataContext is not BoardConfigurationViewModel viewModel)
        {
            return;
        }

        _loaded = true;
        await viewModel.LoadAsync();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
