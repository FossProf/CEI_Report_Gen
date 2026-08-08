namespace CEI.ReportGenerator.Core.Services;

public sealed class RecentProjectEntry
{
    public string Name { get; set; } = string.Empty;

    public string FolderPath { get; set; } = string.Empty;

    public DateTime LastOpenedUtc { get; set; } = DateTime.UtcNow;
}

public static class RecentProjectStore
{
    private static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CEIReportGenerator",
        "recent-projects.json");

    public static void Record(string name, string folderPath, int maxEntries = 10)
    {
        var entries = Load().Where(e => !string.Equals(e.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)).ToList();
        entries.Insert(0, new RecentProjectEntry { Name = name, FolderPath = folderPath, LastOpenedUtc = DateTime.UtcNow });
        entries = entries.Take(Math.Clamp(maxEntries, 1, 25)).ToList();
        JsonStore.Save(FilePath, entries);
    }

    public static List<RecentProjectEntry> Load(int? maxEntries = null)
    {
        if (!JsonStore.TryLoad<List<RecentProjectEntry>>(FilePath, out var loaded, out _))
        {
            return new List<RecentProjectEntry>();
        }

        var entries = loaded ?? new List<RecentProjectEntry>();
        var valid = entries
            .Where(e => ProjectLayout.IsValidProjectFolder(e.FolderPath))
            .OrderByDescending(e => e.LastOpenedUtc)
            .ToList();

        if (valid.Count != entries.Count)
        {
            JsonStore.Save(FilePath, valid);
        }

        if (maxEntries is not null)
        {
            valid = valid.Take(Math.Clamp(maxEntries.Value, 1, 25)).ToList();
        }

        return valid;
    }

    public static void Remove(string folderPath)
    {
        var entries = Load().Where(e => !string.Equals(e.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)).ToList();
        JsonStore.Save(FilePath, entries);
    }
}
