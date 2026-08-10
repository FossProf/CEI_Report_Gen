using System.IO;

namespace CEI.ReportGenerator.App;

public static class ApplicationSettingsValidator
{
    public static List<string> Validate(ApplicationSettings settings)
    {
        var errors = new List<string>();
        ValidateDefaultProjectsFolder(settings.DefaultProjectsFolder, errors);

        if (settings.RecentProjectLimit < 1 || settings.RecentProjectLimit > 25)
        {
            errors.Add("Recent projects shown must be between 1 and 25.");
        }

        ValidateTemperatureAssistance(settings.TemperatureAssistance, errors);

        return errors;
    }

    public static void ValidateAndEnsureFolder(string folderPath)
    {
        var errors = new List<string>();
        ValidateDefaultProjectsFolder(folderPath, errors);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(errors[0]);
        }

        Directory.CreateDirectory(folderPath);
    }

    public static int NormalizeRecentProjectLimit(int recentProjectLimit)
        => Math.Clamp(recentProjectLimit, 1, 25);

    public static int NormalizeHour(int hour)
        => Math.Clamp(hour, 0, 23);

    private static void ValidateDefaultProjectsFolder(string folderPath, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            errors.Add("Default projects folder is required.");
            return;
        }

        if (!Path.IsPathRooted(folderPath))
        {
            errors.Add("Default projects folder must be an absolute path.");
            return;
        }

        if (folderPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            errors.Add("Default projects folder contains invalid path characters.");
            return;
        }

        if (File.Exists(folderPath))
        {
            errors.Add("Default projects folder points to a file.");
            return;
        }
    }

    private static void ValidateTemperatureAssistance(TemperatureAssistanceSettings settings, List<string> errors)
    {
        if (settings.HistoricalDayStartHour < 0 || settings.HistoricalDayStartHour > 23)
        {
            errors.Add("Temperature assistance start hour must be between 0 and 23.");
        }

        if (settings.HistoricalDayEndHour < 0 || settings.HistoricalDayEndHour > 23)
        {
            errors.Add("Temperature assistance end hour must be between 0 and 23.");
        }

        if (settings.HistoricalDayStartHour >= settings.HistoricalDayEndHour)
        {
            errors.Add("Temperature assistance start hour must be earlier than end hour.");
        }
    }
}
