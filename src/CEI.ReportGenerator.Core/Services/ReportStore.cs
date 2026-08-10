using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public static class ReportStore
{
    private const int FinalPhotoCaptionMaxLength = 72;

    public enum DeleteReportStatus
    {
        Deleted,
        NotFound,
        InUse
    }

    public sealed record ReportLoadIssue(string Path, string Message);

    public sealed record ReportLoadResult(
        IReadOnlyList<InspectionReport> Reports,
        IReadOnlyList<ReportLoadIssue> Issues);

    public static Func<string, string, Exception?>? RenameFailureHookForTesting { get; set; }

    public static Func<string, Exception?>? DeleteFailureHookForTesting { get; set; }

    public static Action<string, string>? RenameObserverForTesting { get; set; }

    public static void SaveReport(Project project, InspectionReport report)
    {
        CleanupAbandonedDeleteFolders(project);

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
    {
        CleanupAbandonedDeleteFolders(project);
        return JsonStore.Load<InspectionReport>(ProjectLayout.ReportFilePath(project, reportNumber));
    }

    public static int GetNextReportNumber(Project project)
    {
        CleanupAbandonedDeleteFolders(project);
        var highest = GetHighestFinalReportNumber(project);
        return Math.Max(1, highest + 1);
    }

    public static int GetFirstAvailableReportNumber(Project project, int minimumNumber)
    {
        CleanupAbandonedDeleteFolders(project);
        var occupiedNumbers = GetOccupiedReportNumbers(project).ToHashSet();
        var candidate = Math.Max(1, minimumNumber);
        while (occupiedNumbers.Contains(candidate))
        {
            candidate++;
        }

        return candidate;
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
        CleanupAbandonedDeleteFolders(project);
        var folder = ProjectLayout.ReportFolder(project, reportNumber);
        if (!Directory.Exists(folder))
        {
            return false;
        }

        return Directory.EnumerateFileSystemEntries(folder).Any();
    }

    public static IReadOnlyList<int> GetOccupiedReportNumbers(Project project)
    {
        CleanupAbandonedDeleteFolders(project);
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
        CleanupAbandonedDeleteFolders(project);
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

    public static DeleteReportStatus DeleteReport(Project project, int reportNumber)
    {
        CleanupAbandonedDeleteFolders(project);

        var reportFolder = ProjectLayout.ReportFolder(project, reportNumber);
        if (!Directory.Exists(reportFolder))
        {
            return DeleteReportStatus.NotFound;
        }

        var reportsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ProjectLayout.ReportsFolder(project)));
        var fullReportFolder = Path.GetFullPath(reportFolder);
        if (!fullReportFolder.StartsWith(reportsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Report folder resolves outside the project reports folder.");
        }

        var deletingFolder = Path.Combine(
            reportsRoot,
            $".{Path.GetFileName(fullReportFolder)}.deleting.{Guid.NewGuid():N}");
        EnsureChildOfReportsRoot(reportsRoot, deletingFolder, "Delete staging folder resolves outside the project reports folder.");

        try
        {
            if (RenameFailureHookForTesting?.Invoke(fullReportFolder, deletingFolder) is { } renameFailure)
            {
                throw renameFailure;
            }

            RenameObserverForTesting?.Invoke(fullReportFolder, deletingFolder);
            Directory.Move(fullReportFolder, deletingFolder);
        }
        catch (IOException)
        {
            return DeleteReportStatus.InUse;
        }
        catch (UnauthorizedAccessException)
        {
            return DeleteReportStatus.InUse;
        }

        if (DeleteFailureHookForTesting?.Invoke(deletingFolder) is { } deleteFailure)
        {
            throw deleteFailure;
        }

        Directory.Delete(deletingFolder, recursive: true);
        ProjectStore.RefreshNextReportNumber(project);
        return DeleteReportStatus.Deleted;
    }

    public static void RenameStoredPhotosForFinalization(Project project, InspectionReport report)
    {
        var photosFolder = ProjectLayout.ReportPhotosFolder(project, report.Number);
        if (!Directory.Exists(photosFolder) || report.Photos.Count == 0)
        {
            return;
        }

        var plans = new List<PhotoRenamePlan>();
        var reservedNames = Directory.EnumerateFiles(photosFolder)
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < report.Photos.Count; i++)
        {
            var photo = report.Photos[i];
            if (string.IsNullOrWhiteSpace(photo.StoredFileName))
            {
                continue;
            }

            var currentFileName = ValidateStoredFileName(photo.StoredFileName);
            var currentPath = Path.Combine(photosFolder, currentFileName);
            EnsureWithinFolder(photosFolder, currentPath);
            if (!File.Exists(currentPath))
            {
                continue;
            }

            reservedNames.Remove(currentFileName);
            var targetFileName = BuildFinalizedPhotoFileName(photo, i + 1, reservedNames);
            reservedNames.Add(targetFileName);
            plans.Add(new PhotoRenamePlan(photo, currentPath, currentFileName, targetFileName));
        }

        if (plans.Count == 0)
        {
            return;
        }

        ExecuteTwoPhasePhotoRename(photosFolder, plans, rollbackOnFailure: true);
        foreach (var plan in plans)
        {
            plan.Photo.StoredFileName = plan.TargetFileName;
        }
    }

    public static void RestoreStoredPhotosAfterFailedFinalization(
        Project project,
        InspectionReport report,
        IReadOnlyList<string> originalStoredFileNames)
    {
        var photosFolder = ProjectLayout.ReportPhotosFolder(project, report.Number);
        if (!Directory.Exists(photosFolder) || report.Photos.Count == 0 || report.Photos.Count != originalStoredFileNames.Count)
        {
            return;
        }

        var plans = new List<PhotoRenamePlan>();
        for (var i = 0; i < report.Photos.Count; i++)
        {
            var photo = report.Photos[i];
            var originalStoredFileName = originalStoredFileNames[i];
            if (string.IsNullOrWhiteSpace(photo.StoredFileName) || string.IsNullOrWhiteSpace(originalStoredFileName))
            {
                continue;
            }

            var currentFileName = ValidateStoredFileName(photo.StoredFileName);
            var currentPath = Path.Combine(photosFolder, currentFileName);
            var targetFileName = ValidateStoredFileName(originalStoredFileName);
            var targetPath = Path.Combine(photosFolder, targetFileName);
            EnsureWithinFolder(photosFolder, currentPath);
            EnsureWithinFolder(photosFolder, targetPath);
            if (!File.Exists(currentPath))
            {
                photo.StoredFileName = targetFileName;
                continue;
            }

            plans.Add(new PhotoRenamePlan(photo, currentPath, currentFileName, targetFileName));
        }

        foreach (var group in plans.GroupBy(plan => plan.TargetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var targetFileName = group.Key;
            var targetPath = Path.Combine(photosFolder, targetFileName);
            EnsureWithinFolder(photosFolder, targetPath);

            var sourcePlan = group.FirstOrDefault(plan => File.Exists(plan.CurrentPath));
            if (sourcePlan is not null
                && !string.Equals(sourcePlan.CurrentFileName, targetFileName, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.Move(sourcePlan.CurrentPath, targetPath, overwrite: false);
            }

            foreach (var plan in group)
            {
                if (File.Exists(plan.CurrentPath)
                    && !string.Equals(plan.CurrentFileName, targetFileName, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(plan.CurrentPath);
                }

                plan.Photo.StoredFileName = targetFileName;
            }
        }
    }

    public static string StoredPhotoPath(Project project, InspectionReport report, Photo photo)
    {
        var photosFolder = ProjectLayout.ReportPhotosFolder(project, report.Number);
        var fileName = ValidateStoredFileName(photo.StoredFileName);
        var storedPath = Path.Combine(photosFolder, fileName);
        EnsureWithinFolder(photosFolder, storedPath);
        return storedPath;
    }

    public static void DeleteStoredPhotos(Project project, int reportNumber, IEnumerable<string> storedFileNames)
    {
        var photosFolder = ProjectLayout.ReportPhotosFolder(project, reportNumber);
        if (!Directory.Exists(photosFolder))
        {
            return;
        }

        foreach (var storedFileName in storedFileNames
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var validatedFileName = ValidateStoredFileName(storedFileName);
            var storedPath = Path.Combine(photosFolder, validatedFileName);
            EnsureWithinFolder(photosFolder, storedPath);
            if (File.Exists(storedPath))
            {
                File.Delete(storedPath);
            }
        }

        if (!Directory.EnumerateFileSystemEntries(photosFolder).Any())
        {
            Directory.Delete(photosFolder);
        }
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
        CleanupAbandonedDeleteFolders(project);

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

    private static void CleanupAbandonedDeleteFolders(Project project)
    {
        var reportsRoot = ProjectLayout.ReportsFolder(project);
        if (!Directory.Exists(reportsRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(reportsRoot))
        {
            var name = Path.GetFileName(directory);
            if (!name.Contains(".deleting.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup on project open/load.
            }
        }
    }

    private static void EnsureChildOfReportsRoot(string reportsRoot, string path, string errorMessage)
    {
        var normalizedReportsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(reportsRoot));
        var normalizedPath = Path.GetFullPath(path);
        if (!normalizedPath.StartsWith(normalizedReportsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(errorMessage);
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

    private static int GetHighestFinalReportNumber(Project project)
    {
        var reportsFolder = ProjectLayout.ReportsFolder(project);
        if (!Directory.Exists(reportsFolder))
        {
            return 0;
        }

        var highest = 0;
        foreach (var directory in Directory.EnumerateDirectories(reportsFolder))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var number))
            {
                continue;
            }

            if (IsFinalizedReportFolder(directory))
            {
                highest = Math.Max(highest, number);
            }
        }

        return highest;
    }

    private static bool IsFinalizedReportFolder(string reportFolder)
    {
        var reportJson = Path.Combine(reportFolder, ProjectLayout.ReportJsonFileName);
        if (File.Exists(reportJson))
        {
            if (JsonStore.TryLoad<InspectionReport>(reportJson, out var report, out _)
                && report?.Status == ReportStatus.Final)
            {
                return true;
            }
        }

        return Directory.EnumerateFiles(reportFolder, "*.docx", SearchOption.TopDirectoryOnly)
            .Any(path =>
            {
                var fileName = Path.GetFileName(path);
                return !fileName.StartsWith(".", StringComparison.Ordinal)
                    && !string.Equals(fileName, ProjectLayout.PreviewFileName, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static string BuildFinalizedPhotoFileName(Photo photo, int photoNumber, IReadOnlySet<string> reservedNames)
    {
        var sourceFileName = !string.IsNullOrWhiteSpace(photo.SourcePath)
            ? Path.GetFileName(photo.SourcePath)
            : photo.StoredFileName;
        var extension = Path.GetExtension(sourceFileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = Path.GetExtension(photo.StoredFileName);
        }

        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var originalStem = Path.GetFileNameWithoutExtension(sourceFileName);
        if (string.IsNullOrWhiteSpace(originalStem))
        {
            originalStem = "Photo";
        }

        var sanitizedCaption = SanitizeCaptionForFileName(photo.Caption);
        var baseName = string.IsNullOrWhiteSpace(sanitizedCaption)
            ? $"{originalStem}_Photo {photoNumber}"
            : $"{originalStem}_Photo {photoNumber} - {sanitizedCaption}";
        var candidate = $"{baseName}{extension}";
        var suffix = 2;
        while (reservedNames.Contains(candidate))
        {
            candidate = $"{baseName}_{suffix++}{extension}";
        }

        return candidate;
    }

    private static string SanitizeCaptionForFileName(string caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return string.Empty;
        }

        var invalidCharacters = new HashSet<char>(['\\', '/', ':', '*', '?', '"', '<', '>', '|']);
        var replaced = new string(caption
            .Select(character => invalidCharacters.Contains(character) ? ' ' : character)
            .ToArray());
        var collapsed = string.Join(" ", replaced.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (collapsed.Length <= FinalPhotoCaptionMaxLength)
        {
            return collapsed;
        }

        return collapsed[..FinalPhotoCaptionMaxLength].TrimEnd();
    }

    private static void RollbackPhotoRenamePlans(IEnumerable<PhotoRenamePlan> plans)
    {
        foreach (var plan in plans.Reverse())
        {
            try
            {
                if (plan.FinalPath is not null && File.Exists(plan.FinalPath))
                {
                    if (plan.RestoresOriginalSource)
                    {
                        File.Move(plan.FinalPath, plan.CurrentPath, overwrite: false);
                    }
                    else
                    {
                        File.Delete(plan.FinalPath);
                    }
                }
                else if (plan.RestoresOriginalSource && plan.StagingPath is not null && File.Exists(plan.StagingPath))
                {
                    File.Move(plan.StagingPath, plan.CurrentPath, overwrite: false);
                }

                plan.Photo.StoredFileName = plan.CurrentFileName;
            }
            catch
            {
                // best-effort rollback; finalize caller still restores report.json
            }
        }
    }

    private sealed class PhotoRenamePlan(Photo photo, string currentPath, string currentFileName, string targetFileName)
    {
        public Photo Photo { get; } = photo;

        public string CurrentPath { get; } = currentPath;

        public string CurrentFileName { get; } = currentFileName;

        public string TargetFileName { get; set; } = targetFileName;

        public string? StagingPath { get; set; }

        public string? FinalPath { get; set; }

        public bool RestoresOriginalSource { get; set; }
    }

    private static void ExecuteTwoPhasePhotoRename(string photosFolder, IReadOnlyList<PhotoRenamePlan> plans, bool rollbackOnFailure)
    {
        var renameGroups = plans
            .Where(plan => !string.Equals(plan.CurrentFileName, plan.TargetFileName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(plan => plan.CurrentFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in renameGroups)
        {
            var sourcePlan = group.First();
            sourcePlan.RestoresOriginalSource = true;
            sourcePlan.StagingPath = Path.Combine(
                photosFolder,
                $".rename.{Guid.NewGuid():N}{Path.GetExtension(sourcePlan.CurrentFileName)}");
            EnsureWithinFolder(photosFolder, sourcePlan.StagingPath);
            File.Move(sourcePlan.CurrentPath, sourcePlan.StagingPath, overwrite: false);

            foreach (var plan in group.Skip(1))
            {
                plan.StagingPath = sourcePlan.StagingPath;
            }
        }

        try
        {
            foreach (var group in renameGroups)
            {
                var groupPlans = group.ToList();
                for (var i = 0; i < groupPlans.Count; i++)
                {
                    var plan = groupPlans[i];
                    plan.TargetFileName = EnsureAvailableFinalizedPhotoName(photosFolder, plan.TargetFileName);
                    plan.FinalPath = Path.Combine(photosFolder, plan.TargetFileName);
                    EnsureWithinFolder(photosFolder, plan.FinalPath);

                    if (i == 0)
                    {
                        File.Move(plan.StagingPath!, plan.FinalPath, overwrite: false);
                    }
                    else
                    {
                        File.Copy(groupPlans[0].FinalPath!, plan.FinalPath, overwrite: false);
                    }
                }
            }
        }
        catch
        {
            if (rollbackOnFailure)
            {
                RollbackPhotoRenamePlans(plans);
            }

            throw;
        }
    }

    private static string EnsureAvailableFinalizedPhotoName(string photosFolder, string targetFileName)
    {
        var candidate = targetFileName;
        var extension = Path.GetExtension(targetFileName);
        var stem = Path.GetFileNameWithoutExtension(targetFileName);
        var suffix = 2;
        while (File.Exists(Path.Combine(photosFolder, candidate)))
        {
            candidate = $"{stem}_{suffix++}{extension}";
        }

        return candidate;
    }
}
