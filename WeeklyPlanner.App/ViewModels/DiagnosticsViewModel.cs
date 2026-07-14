using CommunityToolkit.Mvvm.Input;
using WeeklyPlanner.App.Diagnostics;
using WeeklyPlanner.App.Services;
using WeeklyPlanner.Core.Configuration;

namespace WeeklyPlanner.App.ViewModels;

public sealed partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly BoardRuntimeDiagnostics _boardRuntime;
    private readonly IApplicationDiagnosticsProvider _provider;
    private readonly IFolderLauncher _folderLauncher;
    private bool _isLoading;
    private string? _errorMessage;
    private ApplicationDiagnosticsSnapshot? _snapshot;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ApplicationDiagnosticsSnapshot? Snapshot
    {
        get => _snapshot;
        private set
        {
            if (SetProperty(ref _snapshot, value))
            {
                OnPropertyChanged(nameof(HasSnapshot));
                OnPropertyChanged(nameof(DiagnosticsText));
            }
        }
    }

    public bool HasSnapshot => Snapshot is not null;

    public string DiagnosticsText => Snapshot?.ToPlainText() ?? string.Empty;

    public DiagnosticsViewModel(
        AppSettings settings,
        BoardRuntimeDiagnostics boardRuntime,
        IApplicationDiagnosticsProvider provider,
        IFolderLauncher folderLauncher)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings.Clone();
        _settings.Normalize();
        _boardRuntime = boardRuntime ?? throw new ArgumentNullException(nameof(boardRuntime));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _folderLauncher = folderLauncher ?? throw new ArgumentNullException(nameof(folderLauncher));
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            Snapshot = await _provider.CollectAsync(_settings, _boardRuntime, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Impossibile raccogliere la diagnostica: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        if (Snapshot is not null)
        {
            _folderLauncher.OpenFolder(Snapshot.LogDirectoryPath);
        }
    }

    [RelayCommand]
    private void OpenDatabaseFolder()
    {
        var directory = Path.GetDirectoryName(_settings.DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _folderLauncher.OpenFolder(directory);
        }
    }

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        var directory = Snapshot is null
            ? null
            : Path.GetDirectoryName(Snapshot.SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _folderLauncher.OpenFolder(directory);
        }
    }
}
