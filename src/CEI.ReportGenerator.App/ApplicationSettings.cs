using System.IO;

namespace CEI.ReportGenerator.App;

public sealed class ApplicationSettings
{
    public string DefaultProjectsFolder { get; set; } = DefaultProjectsFolderPath();

    public int RecentProjectLimit { get; set; } = 10;

    public bool ReopenLastProjectOnStartup { get; set; }

    public string? LastOpenedProjectPath { get; set; }

    public TemperatureAssistanceSettings TemperatureAssistance { get; set; } = new();

    public static ApplicationSettings CreateDefaults()
        => new();

    public ApplicationSettings Clone()
        => new()
        {
            DefaultProjectsFolder = DefaultProjectsFolder,
            RecentProjectLimit = RecentProjectLimit,
            ReopenLastProjectOnStartup = ReopenLastProjectOnStartup,
            LastOpenedProjectPath = LastOpenedProjectPath,
            TemperatureAssistance = TemperatureAssistance.Clone()
        };

    private static string DefaultProjectsFolderPath()
        => AppIdentity.DefaultProjectsFolderPath();
}

public sealed class TemperatureAssistanceSettings
{
    public bool TemperatureLookupEnabled { get; set; } = true;

    public bool TemperatureAutoEnabledForNewReports { get; set; } = true;

    public int HistoricalDayStartHour { get; set; } = 7;

    public int HistoricalDayEndHour { get; set; } = 17;

    public TemperatureAssistanceSettings Clone()
        => new()
        {
            TemperatureLookupEnabled = TemperatureLookupEnabled,
            TemperatureAutoEnabledForNewReports = TemperatureAutoEnabledForNewReports,
            HistoricalDayStartHour = HistoricalDayStartHour,
            HistoricalDayEndHour = HistoricalDayEndHour
        };
}
