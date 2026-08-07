using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public static class ReportStore
{
    public static void SaveReport(Project project, InspectionReport report)
    {
        var folder = ProjectLayout.ReportFolder(project, report.Number);
        Directory.CreateDirectory(folder);

        var photosFolder = ProjectLayout.ReportPhotosFolder(project, report.Number);
        Directory.CreateDirectory(photosFolder);

        var storedIndex = BuildStoredIndex(photosFolder);

        foreach (var photo in report.Photos)
        {
            if (string.IsNullOrEmpty(photo.StoredFileName) && File.Exists(photo.SourcePath))
            {
                photo.StoredFileName = ResolveStoredName(photosFolder, photo.SourcePath, storedIndex);
            }

            var storedPath = Path.Combine(photosFolder, photo.StoredFileName);
            if (File.Exists(photo.SourcePath) && !File.Exists(storedPath))
            {
                File.Copy(photo.SourcePath, storedPath);
            }
        }

        JsonStore.Save(ProjectLayout.ReportFilePath(project, report.Number), report);
    }

    private sealed class StoredPhotoIndex
    {
        private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _hashToName = new(StringComparer.Ordinal);

        public bool IsNameAvailable(string name)
            => !_usedNames.Contains(name) && !File.Exists(Path.Combine(StoredPhotoFolder, name));

        public string StoredPhotoFolder { get; init; } = string.Empty;

        public void Add(string hash, string name)
        {
            _usedNames.Add(name);
            _hashToName.TryAdd(hash, name);
        }

        public string? FindByHash(string hash)
            => _hashToName.TryGetValue(hash, out var name) ? name : null;
    }

    private static StoredPhotoIndex BuildStoredIndex(string photosFolder)
    {
        var index = new StoredPhotoIndex { StoredPhotoFolder = photosFolder };
        foreach (var file in Directory.EnumerateFiles(photosFolder))
        {
            index.Add(ContentHash(file), Path.GetFileName(file));
        }

        return index;
    }

    private static string ResolveStoredName(string photosFolder, string sourcePath, StoredPhotoIndex index)
    {
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".jpg";
        }

        var hash = ContentHash(sourcePath);
        var existing = index.FindByHash(hash);
        if (existing is not null)
        {
            return existing;
        }

        var fileName = Path.GetFileName(sourcePath);
        if (index.IsNameAvailable(fileName))
        {
            index.Add(hash, fileName);
            return fileName;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var suffix = hash.Length > 6 ? hash.Substring(0, 6) : hash;
        var candidate = $"{stem}_{suffix}{extension}";
        var attempt = 1;
        while (!index.IsNameAvailable(candidate))
        {
            candidate = $"{stem}_{suffix}_{++attempt}{extension}";
        }

        index.Add(hash, candidate);
        return candidate;
    }

    private static string ContentHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    public static InspectionReport? LoadReport(Project project, int reportNumber)
        => JsonStore.Load<InspectionReport>(ProjectLayout.ReportFilePath(project, reportNumber));

    public static List<InspectionReport> LoadAllReports(Project project)
    {
        var reportsFolder = ProjectLayout.ReportsFolder(project);
        if (!Directory.Exists(reportsFolder))
        {
            return new List<InspectionReport>();
        }

        var reports = new List<InspectionReport>();
        foreach (var dir in Directory.EnumerateDirectories(reportsFolder).OrderBy(d => d))
        {
            var json = Path.Combine(dir, "report.json");
            if (File.Exists(json))
            {
                var report = JsonStore.Load<InspectionReport>(json);
                if (report is not null)
                {
                    reports.Add(report);
                }
            }
        }

        return reports;
    }

    public static string StoredPhotoPath(Project project, InspectionReport report, Photo photo)
        => Path.Combine(ProjectLayout.ReportPhotosFolder(project, report.Number), photo.StoredFileName);

    public static string ResolvePhotoSourcePath(Project project, InspectionReport report, Photo photo)
    {
        if (!string.IsNullOrEmpty(photo.SourcePath) && File.Exists(photo.SourcePath))
        {
            return photo.SourcePath;
        }

        var stored = StoredPhotoPath(project, report, photo);
        return File.Exists(stored) ? stored : string.Empty;
    }
}
