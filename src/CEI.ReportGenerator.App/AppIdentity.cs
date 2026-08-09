using System.IO;

namespace CEI.ReportGenerator.App;

internal static class AppIdentity
{
    public const string CurrentName = "SPINgen";

    public const string LegacyName = "CEI Report Generator";

    public static string DefaultProjectsFolderPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            CurrentName,
            "Projects");

    public static string DefaultSettingsFilePath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            CurrentName,
            "settings.json");

    public static string LegacySettingsFilePath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LegacyName,
            "settings.json");
}
