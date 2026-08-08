using System.IO;

namespace CEI.ReportGenerator.App;

public sealed class ApplicationSettings
{
    public string DefaultProjectsFolder { get; set; } = DefaultProjectsFolderPath();

    public int RecentProjectLimit { get; set; } = 10;

    public bool ReopenLastProjectOnStartup { get; set; }

    public string? LastOpenedProjectPath { get; set; }

    public static ApplicationSettings CreateDefaults()
        => new();

    public ApplicationSettings Clone()
        => new()
        {
            DefaultProjectsFolder = DefaultProjectsFolder,
            RecentProjectLimit = RecentProjectLimit,
            ReopenLastProjectOnStartup = ReopenLastProjectOnStartup,
            LastOpenedProjectPath = LastOpenedProjectPath
        };

    private static string DefaultProjectsFolderPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "CEI Report Generator",
            "Projects");
}
