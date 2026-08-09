using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public static class ReportStore
{
    public sealed record ReportLoadIssue(string Path, string Message);

    public sealed record ReportLoadResult(
        IReadOnlyList<InspectionReport> Reports,
        IReadOnlyList<ReportLoadIssue> Issues);

    public static void SaveReport(Project project, InspectionReport report)
    {
        var folder = ProjectLayout.ReportFolder(project, report.Number);
        Directory.CreateDirectory(folder);

        var photosFolder = ProjectLayout.ReportPhotosFolder(project, report.Number);
        Directory.CreateDirectory(photosFolder);

        var storedIndex = BuildStoredIndex(photosFolder);

        foreach (var photo in report.Photos)
        {
            if (!string.IsNullOrWhiteSpace(photo.StoredFileName))
            {
                photo.StoredFileName = ValidateStoredFileName(photo.StoredFileName);
            }

            if (!File.Exists(photo.SourcePath))
            {
                continue;
            }

            _ = ImagePartManager.GetContentType(photo.SourcePath);
            var bytes = ImageNormalizer.GetNormalizedBytes(photo.SourcePath);
            var hash = ContentHash(bytes);
            if (string.IsNullOrEmpty(photo.StoredFileName))
            {
                photo.StoredFileName = ResolveStoredName(photosFolder, photo.SourcePath, bytes, storedIndex);
            }
            else if (!storedIndex.CanUseName(photo.StoredFileName, hash))
            {
                photo.StoredFileName = ResolveStoredName(photosFolder, photo.SourcePath, bytes, storedIndex);
            }

            var storedPath = Path.Combine(photosFolder, photo.StoredFileName);
            EnsureWithinFolder(photosFolder, storedPath);
            if (!File.Exists(storedPath))
            {
                File.WriteAllBytes(storedPath, bytes);
            }

            storedIndex.Add(hash, photo.StoredFileName);
        }

        JsonStore.Save(ProjectLayout.ReportFilePath(project, report.Number), report);
    }

    private sealed class StoredPhotoIndex
    {
        private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _hashToName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _nameToHash = new(StringComparer.OrdinalIgnoreCase);

        public bool IsNameAvailable(string name)
            => !_usedNames.Contains(name) && !File.Exists(Path.Combine(StoredPhotoFolder, name));

        public string StoredPhotoFolder { get; init; } = string.Empty;

        public void Add(string hash, string name)
        {
            _usedNames.Add(name);
            _hashToName[hash] = name;
            _nameToHash[name] = hash;
        }

        public string? FindByHash(string hash)
            => _hashToName.TryGetValue(hash, out var name) ? name : null;

        public bool CanUseName(string name, string hash)
        {
            if (_nameToHash.TryGetValue(name, out var existingHash))
            {
                return string.Equals(existingHash, hash, StringComparison.Ordinal);
            }

            return !File.Exists(Path.Combine(StoredPhotoFolder, name));
        }
    }

    private static StoredPhotoIndex BuildStoredIndex(string photosFolder)
    {
        var index = new StoredPhotoIndex { StoredPhotoFolder = photosFolder };
        foreach (var file in Directory.EnumerateFiles(photosFolder))
        {
            index.Add(ContentHash(File.ReadAllBytes(file)), Path.GetFileName(file));
        }

        return index;
    }

    private static string ResolveStoredName(string photosFolder, string sourcePath, byte[] normalizedBytes, StoredPhotoIndex index)
    {
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".jpg";
        }

        var hash = ContentHash(normalizedBytes);
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

    private static string ContentHash(byte[] data)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

    public static InspectionReport? LoadReport(Project project, int reportNumber)
        => JsonStore.Load<InspectionReport>(ProjectLayout.ReportFilePath(project, reportNumber));

    public static int GetNextReportNumber(Project project)
    {
        var highest = GetOccupiedReportNumbers(project).DefaultIfEmpty(0).Max();
        return Math.Max(project.NextReportNumber, highest + 1);
    }

    public static int SynchronizeNextReportNumber(Project project)
    {
        var nextNumber = GetNextReportNumber(project);
        if (project.NextReportNumber != nextNumber)
        {
            project.NextReportNumber = nextNumber;
            ProjectStore.Save(project);
        }

        return nextNumber;
    }

    public static bool ReportNumberExists(Project project, int reportNumber)
    {
        var folder = ProjectLayout.ReportFolder(project, reportNumber);
        if (!Directory.Exists(folder))
        {
            return false;
        }

        return Directory.EnumerateFileSystemEntries(folder).Any();
    }

    public static IReadOnlyList<int> GetOccupiedReportNumbers(Project project)
    {
        var reportsFolder = ProjectLayout.ReportsFolder(project);
        if (!Directory.Exists(reportsFolder))
        {
            return Array.Empty<int>();
        }

        var occupied = new List<int>();
        foreach (var dir in Directory.EnumerateDirectories(reportsFolder))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var number))
            {
                continue;
            }

            var reportJson = Path.Combine(dir, "report.json");
            var hasFinalDocx = Directory.EnumerateFiles(dir, "*.docx", SearchOption.TopDirectoryOnly)
                .Any(path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal));
            if (File.Exists(reportJson) || hasFinalDocx)
            {
                occupied.Add(number);
            }
        }

        return occupied;
    }

    public static ReportLoadResult LoadAllReports(Project project)
    {
        var reportsFolder = ProjectLayout.ReportsFolder(project);
        if (!Directory.Exists(reportsFolder))
        {
            return new ReportLoadResult(Array.Empty<InspectionReport>(), Array.Empty<ReportLoadIssue>());
        }

        var reports = new List<InspectionReport>();
        var issues = new List<ReportLoadIssue>();
        foreach (var dir in Directory.EnumerateDirectories(reportsFolder).OrderBy(d => d))
        {
            var json = Path.Combine(dir, "report.json");
            if (File.Exists(json))
            {
                if (!JsonStore.TryLoad<InspectionReport>(json, out var report, out var error))
                {
                    issues.Add(new ReportLoadIssue(json, error ?? "Unknown load error."));
                    continue;
                }

                if (report is not null)
                {
                    reports.Add(report);
                }
            }
        }

        return new ReportLoadResult(reports, issues);
    }

    public static bool DeleteReport(Project project, int reportNumber)
    {
        var reportFolder = ProjectLayout.ReportFolder(project, reportNumber);
        if (!Directory.Exists(reportFolder))
        {
            return false;
        }

        var reportsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ProjectLayout.ReportsFolder(project)));
        var fullReportFolder = Path.GetFullPath(reportFolder);
        if (!fullReportFolder.StartsWith(reportsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Report folder resolves outside the project reports folder.");
        }

        Directory.Delete(fullReportFolder, recursive: true);
        return true;
    }

    public static string StoredPhotoPath(Project project, InspectionReport report, Photo photo)
    {
        var photosFolder = ProjectLayout.ReportPhotosFolder(project, report.Number);
        var fileName = ValidateStoredFileName(photo.StoredFileName);
        var storedPath = Path.Combine(photosFolder, fileName);
        EnsureWithinFolder(photosFolder, storedPath);
        return storedPath;
    }

    public static string ResolvePhotoSourcePath(Project project, InspectionReport report, Photo photo)
    {
        if (!string.IsNullOrEmpty(photo.SourcePath) && File.Exists(photo.SourcePath))
        {
            return photo.SourcePath;
        }

        try
        {
            var stored = StoredPhotoPath(project, report, photo);
            return File.Exists(stored) ? stored : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static void CleanupPreviewArtifacts(Project project, int reportNumber, bool removePreview = true)
    {
        var previewPath = ProjectLayout.ReportPreviewPath(project, reportNumber);
        if (removePreview && File.Exists(previewPath))
        {
            File.Delete(previewPath);
        }

        var workingFolder = ProjectLayout.ReportWorkingFolder(project, reportNumber);
        DeleteMatchingFiles(workingFolder, "*.tmp.docx");
        DeleteMatchingFiles(workingFolder, "*.finalizing.docx");
        DeleteMatchingFiles(ProjectLayout.ReportFolder(project, reportNumber), "*.finalizing.docx");

        if (Directory.Exists(workingFolder) && !Directory.EnumerateFileSystemEntries(workingFolder).Any())
        {
            Directory.Delete(workingFolder);
        }
    }

    private static string ValidateStoredFileName(string storedFileName)
    {
        if (string.IsNullOrWhiteSpace(storedFileName))
        {
            throw new InvalidOperationException("Stored photo file name cannot be empty.");
        }

        if (!string.Equals(Path.GetFileName(storedFileName), storedFileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stored photo file names must be file names only.");
        }

        return storedFileName;
    }

    private static void EnsureWithinFolder(string folder, string path)
    {
        var normalizedFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var normalizedPath = Path.GetFullPath(path);
        if (!normalizedPath.StartsWith(normalizedFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Stored photo path escapes the report photos folder.");
        }
    }

    private static void DeleteMatchingFiles(string folder, string pattern)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
        }
    }
}
