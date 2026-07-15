using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using WeeklyPlanner.App.Services;
using WeeklyPlanner.Core.Configuration;
using WeeklyPlanner.Core.Data;

namespace WeeklyPlanner.App.ViewModels;

public sealed partial class DatabaseSafetyViewModel : ViewModelBase
{
    private readonly IDatabaseSafetyService _databaseSafetyService;
    private readonly IFolderLauncher _folderLauncher;
    private readonly string _currentSessionId;
    private bool _isBusy;
    private string? _operationMessage;
    private string? _errorMessage;
    private DatabaseBackupItemViewModel? _selectedBackup;
    private bool _isRestoreConfirmationVisible;

    public ObservableCollection<DatabaseBackupItemViewModel> Backups { get; } = new();

    public string DatabasePath { get; }

    public string BackupDirectory => _databaseSafetyService.BackupDirectory;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                OnPropertyChanged(nameof(HasNoBackups));
                OnPropertyChanged(nameof(CanCreateBackup));
                OnPropertyChanged(nameof(CanPrepareRestore));
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public bool CanCreateBackup => !IsBusy;

    public DatabaseBackupItemViewModel? SelectedBackup
    {
        get => _selectedBackup;
        set
        {
            if (SetProperty(ref _selectedBackup, value))
            {
                IsRestoreConfirmationVisible = false;
                OnPropertyChanged(nameof(HasSelectedBackup));
                OnPropertyChanged(nameof(CanPrepareRestore));
                OnPropertyChanged(nameof(RestoreConfirmationText));
            }
        }
    }

    public bool HasSelectedBackup => SelectedBackup is not null;

    public bool CanPrepareRestore =>
        !IsBusy && SelectedBackup is { CanRestore: true };

    public bool IsRestoreConfirmationVisible
    {
        get => _isRestoreConfirmationVisible;
        private set => SetProperty(ref _isRestoreConfirmationVisible, value);
    }

    public string RestoreConfirmationText => SelectedBackup is null
        ? string.Empty
        : $"Ripristinare '{SelectedBackup.FileName}'? WeeklyPlanner preparerà l'operazione e si riavvierà. Prima di sostituire il database, la nuova istanza creerà un backup preventivo della versione corrente.";

    public string? OperationMessage
    {
        get => _operationMessage;
        private set
        {
            if (SetProperty(ref _operationMessage, value))
            {
                OnPropertyChanged(nameof(HasOperationMessage));
            }
        }
    }

    public bool HasOperationMessage => !string.IsNullOrWhiteSpace(OperationMessage);

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

    public bool HasBackups => Backups.Count > 0;

    public bool HasNoBackups => !HasBackups && !IsBusy;

    public event EventHandler<DatabaseRestorePreparation>? RestorePrepared;

    public DatabaseSafetyViewModel(
        AppSettings settings,
        string currentSessionId,
        IDatabaseSafetyService databaseSafetyService,
        IFolderLauncher folderLauncher)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSessionId);

        var normalizedSettings = settings.Clone();
        normalizedSettings.Normalize();
        DatabasePath = normalizedSettings.DatabasePath;
        _currentSessionId = currentSessionId;
        _databaseSafetyService = databaseSafetyService ?? throw new ArgumentNullException(nameof(databaseSafetyService));
        _folderLauncher = folderLauncher ?? throw new ArgumentNullException(nameof(folderLauncher));
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RefreshBackupsOperationAsync(cancellationToken);

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        if (!CanCreateBackup)
        {
            return;
        }

        await RunOperationAsync(
            "Creazione del backup in corso...",
            async cancellationToken =>
            {
                var backup = await _databaseSafetyService.CreateBackupAsync(
                    DatabasePath,
                    DatabaseBackupKind.Manual,
                    cancellationToken);
                await RefreshBackupsCoreAsync(cancellationToken);
                SelectedBackup = Backups.FirstOrDefault(item =>
                    string.Equals(item.FilePath, backup.FilePath, PathComparison()));
                OperationMessage = $"Backup creato: {backup.FileName}";
            });
    }

    [RelayCommand]
    private Task RefreshBackupsAsync() =>
        RefreshBackupsOperationAsync(CancellationToken.None);

    private async Task RefreshBackupsOperationAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        await RunOperationAsync(
            "Verifica dei backup in corso...",
            RefreshBackupsCoreAsync,
            cancellationToken);
    }

    private async Task RefreshBackupsCoreAsync(CancellationToken cancellationToken)
    {
        var selectedPath = SelectedBackup?.FilePath;
        var backups = await _databaseSafetyService.ListBackupsAsync(cancellationToken);

        Backups.Clear();
        foreach (var backup in backups)
        {
            Backups.Add(new DatabaseBackupItemViewModel(backup));
        }

        SelectedBackup = string.IsNullOrWhiteSpace(selectedPath)
            ? Backups.FirstOrDefault()
            : Backups.FirstOrDefault(item =>
                string.Equals(item.FilePath, selectedPath, PathComparison()))
                ?? Backups.FirstOrDefault();
        OperationMessage = Backups.Count switch
        {
            0 => "Nessun backup disponibile.",
            1 => "1 backup verificato.",
            _ => $"{Backups.Count} backup verificati.",
        };
        RaiseBackupCollectionStateChanged();
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        ErrorMessage = null;
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            _folderLauncher.OpenFolder(BackupDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorMessage = $"Impossibile aprire la cartella dei backup: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BeginRestore()
    {
        ErrorMessage = null;
        OperationMessage = null;
        if (!CanPrepareRestore)
        {
            return;
        }

        IsRestoreConfirmationVisible = true;
    }

    [RelayCommand]
    private void CancelRestore() => IsRestoreConfirmationVisible = false;

    [RelayCommand]
    private async Task ConfirmRestoreAsync()
    {
        var selectedBackup = SelectedBackup;
        if (!CanPrepareRestore || selectedBackup is null)
        {
            return;
        }

        await RunOperationAsync(
            "Preparazione del ripristino e backup preventivo...",
            async cancellationToken =>
            {
                var preparation = await _databaseSafetyService.PrepareRestoreAsync(
                    DatabasePath,
                    selectedBackup.FilePath,
                    _currentSessionId,
                    cancellationToken);
                IsRestoreConfirmationVisible = false;
                RestorePrepared?.Invoke(this, preparation);
            });
    }

    public Task CancelPreparedRestoreAsync(
        DatabaseRestorePreparation preparation,
        CancellationToken cancellationToken = default) =>
        _databaseSafetyService.CancelPreparedRestoreAsync(preparation, cancellationToken);

    private async Task RunOperationAsync(
        string activityMessage,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        OperationMessage = activityMessage;
        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            OperationMessage = null;
        }
        catch (DatabaseRestoreBlockedException ex)
        {
            OperationMessage = null;
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            OperationMessage = null;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RaiseBackupCollectionStateChanged();
        }
    }

    private void RaiseBackupCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(HasNoBackups));
        OnPropertyChanged(nameof(CanCreateBackup));
        OnPropertyChanged(nameof(CanPrepareRestore));
    }

    private static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
