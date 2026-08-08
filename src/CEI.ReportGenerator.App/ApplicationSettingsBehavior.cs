using CEI.ReportGenerator.Core;

namespace CEI.ReportGenerator.App;

public static class ApplicationSettingsBehavior
{
    public static string? GetStartupReopenProjectPath(ApplicationSettings settings, ApplicationSettingsStore store)
    {
        if (!settings.ReopenLastProjectOnStartup || string.IsNullOrWhiteSpace(settings.LastOpenedProjectPath))
        {
            return null;
        }

        if (!ProjectLayout.IsValidProjectFolder(settings.LastOpenedProjectPath))
        {
            settings.LastOpenedProjectPath = null;
            store.Save(settings);
            return null;
        }

        return settings.LastOpenedProjectPath;
    }
}
