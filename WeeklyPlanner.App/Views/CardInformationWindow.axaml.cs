using Avalonia.Controls;
using Avalonia.Interactivity;
using WeeklyPlanner.App.ViewModels;

namespace WeeklyPlanner.App.Views;

public partial class CardInformationWindow : Window
{
    private readonly CancellationTokenSource _loadCancellation = new();

    public CardInformationWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is CardInformationViewModel viewModel)
        {
            await viewModel.LoadAsync(_loadCancellation.Token);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _loadCancellation.Cancel();
        _loadCancellation.Dispose();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
