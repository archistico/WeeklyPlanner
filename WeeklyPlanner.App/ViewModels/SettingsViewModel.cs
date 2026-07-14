using CommunityToolkit.Mvvm.Input;
using WeeklyPlanner.App.Services;
using WeeklyPlanner.Core.Configuration;

namespace WeeklyPlanner.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settingsService;
    private readonly IFolderLauncher _folderLauncher;
    private readonly AppSettings _originalSettings;

    private string _databasePath;
    private string _userName;
    private int _pollingIntervalSeconds;
    private ThemePreferenceOption _selectedThemeOption;
    private string? _validationMessage;
    private string? _informationMessage;

    public IReadOnlyList<ThemePreferenceOption> ThemeOptions { get; } =
    [
        new(AppThemePreference.System, "Sistema"),
        new(AppThemePreference.Light, "Chiaro"),
        new(AppThemePreference.Dark, "Scuro"),
    ];

    public string DatabasePath
    {
        get => _databasePath;
        set => SetProperty(ref _databasePath, value);
    }

    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }

    public int PollingIntervalSeconds
    {
        get => _pollingIntervalSeconds;
        set => SetProperty(ref _pollingIntervalSeconds, value);
    }

    public ThemePreferenceOption SelectedThemeOption
    {
        get => _selectedThemeOption;
        set => SetProperty(ref _selectedThemeOption, value);
    }

    public bool CanEditIdentityAndDatabase { get; }

    public bool IsIdentityAndDatabaseBlocked => !CanEditIdentityAndDatabase;

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationMessage));
            }
        }
    }

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public string? InformationMessage
    {
        get => _informationMessage;
        private set
        {
            if (SetProperty(ref _informationMessage, value))
            {
                OnPropertyChanged(nameof(HasInformationMessage));
            }
        }
    }

    public bool HasInformationMessage => !string.IsNullOrWhiteSpace(InformationMessage);

    public event EventHandler<SettingsSaveResult>? Completed;

    public SettingsViewModel(
        IAppSettingsService settingsService,
        AppSettings existingSettings,
        bool canEditIdentityAndDatabase,
        IFolderLauncher? folderLauncher = null)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(existingSettings);

        _settingsService = settingsService;
        _folderLauncher = folderLauncher ?? new ShellFolderLauncher();
        _originalSettings = existingSettings.Clone();
        _originalSettings.Normalize();

        _databasePath = _originalSettings.DatabasePath;
        _userName = _originalSettings.UserName;
        _pollingIntervalSeconds = _originalSettings.PollingIntervalSeconds;
        _selectedThemeOption = ThemeOptions.First(option =>
            option.Value == _originalSettings.ThemePreference);
        CanEditIdentityAndDatabase = canEditIdentityAndDatabase;
    }

    [RelayCommand]
    private void Save()
    {
        ValidationMessage = null;
        InformationMessage = null;

        var normalizedDatabasePath = AppSettings.NormalizeDatabasePath(DatabasePath);
        var normalizedUserName = UserName?.Trim() ?? string.Empty;
        var userNameChanged = !string.Equals(
            normalizedUserName,
            _originalSettings.UserName,
            StringComparison.Ordinal);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var databasePathChanged = !string.Equals(
            normalizedDatabasePath,
            _originalSettings.DatabasePath,
            pathComparison);

        if (!CanEditIdentityAndDatabase && (userNameChanged || databasePathChanged))
        {
            ValidationMessage =
                "Termina la modifica delle card e attendi la conclusione delle operazioni prima di cambiare nome o database.";
            return;
        }

        if (string.IsNullOrWhiteSpace(normalizedUserName))
        {
            ValidationMessage = "Inserisci il nome da registrare sulle modifiche.";
            return;
        }

        if (!AppSettings.IsSupportedLocalDatabasePath(normalizedDatabasePath))
        {
            ValidationMessage =
                "Scegli un percorso locale assoluto. Le cartelle di rete UNC non sono supportate.";
            return;
        }

        if (PollingIntervalSeconds is < AppSettings.MinimumPollingIntervalSeconds or
            > AppSettings.MaximumPollingIntervalSeconds)
        {
            ValidationMessage =
                $"L'intervallo di polling deve essere compreso tra {AppSettings.MinimumPollingIntervalSeconds} e {AppSettings.MaximumPollingIntervalSeconds} secondi.";
            return;
        }

        var settings = _originalSettings.Clone();
        settings.DatabasePath = normalizedDatabasePath;
        settings.UserName = normalizedUserName;
        settings.PollingIntervalSeconds = PollingIntervalSeconds;
        settings.ThemePreference = SelectedThemeOption.Value;
        settings.Normalize();

        try
        {
            _settingsService.Save(settings);
        }
        catch (Exception ex)
        {
            ValidationMessage = $"Impossibile salvare le impostazioni locali: {ex.Message}";
            return;
        }

        Completed?.Invoke(
            this,
            new SettingsSaveResult(settings, userNameChanged, databasePathChanged));
    }

    [RelayCommand]
    private void OpenDatabaseFolder()
    {
        ValidationMessage = null;
        InformationMessage = null;

        var normalizedPath = AppSettings.NormalizeDatabasePath(DatabasePath);
        if (!AppSettings.IsSupportedLocalDatabasePath(normalizedPath))
        {
            ValidationMessage = "Il percorso del database non è valido.";
            return;
        }

        var folderPath = Path.GetDirectoryName(normalizedPath);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            ValidationMessage = "Non è possibile determinare la cartella del database.";
            return;
        }

        OpenFolder(folderPath, "cartella del database");
    }

    [RelayCommand]
    private void OpenApplicationDataFolder() =>
        OpenFolder(_settingsService.SettingsDirectoryPath, "cartella dei dati applicativi");

    private void OpenFolder(string folderPath, string description)
    {
        ValidationMessage = null;
        InformationMessage = null;

        try
        {
            _folderLauncher.OpenFolder(folderPath);
            InformationMessage = $"Aperta la {description}.";
        }
        catch (Exception ex)
        {
            ValidationMessage = $"Impossibile aprire la {description}: {ex.Message}";
        }
    }
}
