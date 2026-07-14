using CommunityToolkit.Mvvm.Input;
using WeeklyPlanner.Core.Configuration;

namespace WeeklyPlanner.App.ViewModels;

public sealed partial class OnboardingViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settingsService;
    private readonly AppSettings _existingSettings;

    private string _databasePath;
    private string _userName;
    private int _pollingIntervalSeconds;
    private string? _validationMessage;

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

    public event EventHandler<AppSettings>? Completed;

    public OnboardingViewModel(IAppSettingsService settingsService, AppSettings existingSettings)
    {
        _settingsService = settingsService;
        _existingSettings = existingSettings.Clone();
        _existingSettings.Normalize();
        _databasePath = string.IsNullOrWhiteSpace(existingSettings.DatabasePath)
            ? AppSettings.GetDefaultDatabasePath()
            : existingSettings.DatabasePath;
        _userName = existingSettings.UserName;
        _pollingIntervalSeconds = Math.Clamp(
            existingSettings.PollingIntervalSeconds,
            AppSettings.MinimumPollingIntervalSeconds,
            AppSettings.MaximumPollingIntervalSeconds);
    }

    [RelayCommand]
    private void Confirm()
    {
        ValidationMessage = null;

        if (string.IsNullOrWhiteSpace(DatabasePath) || string.IsNullOrWhiteSpace(UserName))
        {
            ValidationMessage = "Inserisci sia il percorso del database sia il tuo nome.";
            return;
        }

        if (!AppSettings.IsSupportedLocalDatabasePath(DatabasePath))
        {
            ValidationMessage =
                "Scegli un percorso locale assoluto. Le cartelle di rete UNC non sono supportate.";
            return;
        }

        var settings = _existingSettings.Clone();
        settings.DatabasePath = DatabasePath;
        settings.UserName = UserName;
        settings.PollingIntervalSeconds = PollingIntervalSeconds;
        settings.Normalize();

        try
        {
            _settingsService.Save(settings);
            Completed?.Invoke(this, settings);
        }
        catch (Exception ex)
        {
            ValidationMessage = $"Impossibile salvare la configurazione locale: {ex.Message}";
        }
    }
}
