namespace CEI.ReportGenerator.Core.Services;

public sealed class RecentProjectEntry
{
    public string Name { get; set; } = string.Empty;

    public string FolderPath { get; set; } = string.Empty;

    public DateTime LastOpenedUtc { get; set; } = DateTime.UtcNow;
}

public static class RecentProjectStore
{
    private const int MaxEntries = 10;

    private static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CEIReportGenerator",
        "recent-projects.json");

    public static void Record(string name, string folderPath)
    {
        var entries = Load().Where(e => !string.Equals(e.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)).ToList();
        entries.Insert(0, new RecentProjectEntry { Name = name, FolderPath = folderPath, LastOpenedUtc = DateTime.UtcNow });
        entries = entries.Take(MaxEntries).ToList();
        JsonStore.Save(FilePath, entries);
    }

    public static List<RecentProjectEntry> Load()
    {
        var entries = JsonStore.Load<List<RecentProjectEntry>>(FilePath) ?? new List<RecentProjectEntry>();
        var valid = entries
            .Where(e => ProjectLayout.IsValidProjectFolder(e.FolderPath))
            .OrderByDescending(e => e.LastOpenedUtc)
            .ToList();

        if (valid.Count != entries.Count)
        {
            JsonStore.Save(FilePath, valid);
        }

        return valid;
    }

    public static void Remove(string folderPath)
    {
        var entries = Load().Where(e => !string.Equals(e.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)).ToList();
        JsonStore.Save(FilePath, entries);
    }
}
