using Avalonia.Controls;
using Avalonia.Interactivity;
using WeeklyPlanner.App.ViewModels;

namespace WeeklyPlanner.App.Views;

public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is DiagnosticsViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }

    private async void OnCopyDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DiagnosticsViewModel viewModel ||
            string.IsNullOrWhiteSpace(viewModel.DiagnosticsText))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            CopyStatusText.Text = "Clipboard non disponibile";
            return;
        }

        await clipboard.SetTextAsync(viewModel.DiagnosticsText);
        CopyStatusText.Text = "Diagnostica copiata";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
