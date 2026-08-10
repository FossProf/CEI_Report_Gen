using System.IO;
using System.Text;
using System.Text.Json;

namespace CEI.ReportGenerator.App;

public sealed class ApplicationSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string? _legacyFilePath;

    public ApplicationSettingsStore(string? filePath = null, string? legacyFilePath = null)
    {
        FilePath = filePath ?? AppIdentity.DefaultSettingsFilePath();
        _legacyFilePath = legacyFilePath ?? (filePath is null ? AppIdentity.LegacySettingsFilePath() : null);
    }

    public string FilePath { get; }

    public string? LastLoadError { get; private set; }

    public ApplicationSettings Load()
    {
        LastLoadError = null;

        var loadPath = FilePath;
        if (!File.Exists(loadPath))
        {
            if (!string.IsNullOrWhiteSpace(_legacyFilePath) && File.Exists(_legacyFilePath))
            {
                loadPath = _legacyFilePath;
            }
            else
            {
                return ApplicationSettings.CreateDefaults();
            }
        }

        try
        {
            var json = File.ReadAllText(loadPath);
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(json, Options) ?? ApplicationSettings.CreateDefaults();
            settings.RecentProjectLimit = ApplicationSettingsValidator.NormalizeRecentProjectLimit(settings.RecentProjectLimit);
            if (string.IsNullOrWhiteSpace(settings.DefaultProjectsFolder))
            {
                settings.DefaultProjectsFolder = ApplicationSettings.CreateDefaults().DefaultProjectsFolder;
            }

            settings.TemperatureAssistance ??= new TemperatureAssistanceSettings();
            settings.TemperatureAssistance.HistoricalDayStartHour =
                ApplicationSettingsValidator.NormalizeHour(settings.TemperatureAssistance.HistoricalDayStartHour);
            settings.TemperatureAssistance.HistoricalDayEndHour =
                ApplicationSettingsValidator.NormalizeHour(settings.TemperatureAssistance.HistoricalDayEndHour);
            if (settings.TemperatureAssistance.HistoricalDayStartHour >= settings.TemperatureAssistance.HistoricalDayEndHour)
            {
                var defaults = ApplicationSettings.CreateDefaults().TemperatureAssistance;
                settings.TemperatureAssistance.HistoricalDayStartHour = defaults.HistoricalDayStartHour;
                settings.TemperatureAssistance.HistoricalDayEndHour = defaults.HistoricalDayEndHour;
            }

            return settings;
        }
        catch (Exception ex)
        {
            LastLoadError = ex.Message;
            return ApplicationSettings.CreateDefaults();
        }
    }

    public void Save(ApplicationSettings settings)
    {
        var errors = ApplicationSettingsValidator.Validate(settings);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(errors[0]);
        }

        Directory.CreateDirectory(settings.DefaultProjectsFolder);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory ?? Directory.GetCurrentDirectory(), $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    public ApplicationSettings ResetToDefaults()
        => ApplicationSettings.CreateDefaults();
}
