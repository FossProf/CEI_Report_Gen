using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;

namespace CEI.ReportGenerator.App.ViewModels;

public sealed class TemperatureAssistanceSession : ObservableObject, IDisposable
{
    private readonly Project _project;
    private readonly TemperatureAssistanceSettings _settings;
    private readonly IProjectTemperatureService _temperatureService;
    private readonly Func<DateTimeOffset> _utcNowProvider;
    private CancellationTokenSource? _lookupCancellation;
    private bool _autoEnabled;
    private bool _isApplyingLookupValue;
    private bool _isLookupInProgress;
    private string _statusMessage;
    private string _temperatureText;
    private DateTime _selectedDate;
    private int _lookupVersion;

    public TemperatureAssistanceSession(
        Project project,
        string currentTemperature,
        DateTime selectedDate,
        bool isNewReport,
        bool isFinalReport,
        TemperatureAssistanceSettings settings,
        IProjectTemperatureService temperatureService,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        _project = project;
        _settings = settings.Clone();
        _temperatureService = temperatureService;
        _utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
        _temperatureText = currentTemperature;
        _selectedDate = selectedDate.Date;
        _statusMessage = string.Empty;
        _autoEnabled = _settings.TemperatureLookupEnabled
            && isNewReport
            && !isFinalReport
            && _settings.TemperatureAutoEnabledForNewReports;
    }

    public bool IsFeatureEnabled => _settings.TemperatureLookupEnabled;

    public bool AutoEnabled
    {
        get => _autoEnabled;
        private set => SetProperty(ref _autoEnabled, value);
    }

    public bool IsLookupInProgress
    {
        get => _isLookupInProgress;
        private set => SetProperty(ref _isLookupInProgress, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string TemperatureText
    {
        get => _temperatureText;
        private set => SetProperty(ref _temperatureText, value);
    }

    public async Task InitializeAsync()
    {
        if (AutoEnabled)
        {
            await RefreshAsync();
        }
    }

    public async Task SetAutoEnabledAsync(bool enabled, DateTime selectedDate)
    {
        _selectedDate = selectedDate.Date;
        if (!IsFeatureEnabled)
        {
            AutoEnabled = false;
            return;
        }

        AutoEnabled = enabled;
        if (!enabled)
        {
            CancelLookup();
            StatusMessage = string.Empty;
            IsLookupInProgress = false;
            return;
        }

        await RefreshAsync();
    }

    public async Task UpdateDateAsync(DateTime selectedDate)
    {
        _selectedDate = selectedDate.Date;
        if (AutoEnabled)
        {
            await RefreshAsync();
        }
    }

    public void ApplyManualTemperatureOverride(string temperatureText)
    {
        TemperatureText = temperatureText;
        if (_isApplyingLookupValue)
        {
            return;
        }

        if (!AutoEnabled)
        {
            return;
        }

        CancelLookup();
        AutoEnabled = false;
        IsLookupInProgress = false;
        StatusMessage = string.Empty;
    }

    public void Dispose()
    {
        CancelLookup();
    }

    private async Task RefreshAsync()
    {
        CancelLookup();
        if (_project.Coordinates is not ProjectCoordinates coordinates)
        {
            StatusMessage = "Temperature lookup unavailable until the project location is resolved.";
            return;
        }

        var projectToday = ResolveProjectToday(coordinates.TimeZoneId);
        if (_selectedDate.Date > projectToday)
        {
            StatusMessage = "Automatic temperature is available for today and past dates.";
            return;
        }

        var requestVersion = ++_lookupVersion;
        _lookupCancellation = new CancellationTokenSource();
        IsLookupInProgress = true;
        StatusMessage = "Looking up temperature...";

        try
        {
            var result = _selectedDate.Date == projectToday
                ? await _temperatureService.GetCurrentTemperatureAsync(coordinates, _lookupCancellation.Token)
                : await _temperatureService.GetHistoricalDaytimeAverageAsync(
                    coordinates,
                    _selectedDate.Date,
                    _settings.HistoricalDayStartHour,
                    _settings.HistoricalDayEndHour,
                    _lookupCancellation.Token);

            if (requestVersion != _lookupVersion || !AutoEnabled)
            {
                return;
            }

            if (!result.IsSuccess || result.RoundedTemperatureFahrenheit is null)
            {
                StatusMessage = result.FailureMessage ?? "Temperature lookup unavailable. Enter temperature manually.";
                return;
            }

            _isApplyingLookupValue = true;
            TemperatureText = result.RoundedTemperatureFahrenheit.Value.ToString();
            _isApplyingLookupValue = false;
            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            if (requestVersion == _lookupVersion)
            {
                StatusMessage = string.Empty;
            }
        }
        finally
        {
            if (requestVersion == _lookupVersion)
            {
                IsLookupInProgress = false;
            }
        }
    }

    private DateTime ResolveProjectToday(string timeZoneId)
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTime(_utcNowProvider(), timeZone).Date;
        }
        catch
        {
            return _utcNowProvider().Date;
        }
    }

    private void CancelLookup()
    {
        _lookupCancellation?.Cancel();
        _lookupCancellation?.Dispose();
        _lookupCancellation = null;
        _lookupVersion++;
    }
}
