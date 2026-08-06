namespace CEI.ReportGenerator.Core.Services;

public enum SignatureResolveStatus
{
    Valid,
    Empty,
    OutsideProject,
    MissingFile,
    UnsupportedExtension
}

public readonly record struct SignatureResolveResult(SignatureResolveStatus Status, string? FullPath)
{
    public bool IsValid => Status == SignatureResolveStatus.Valid;
}

public static class SignatureStore
{
    public static readonly IReadOnlyList<string> SupportedExtensions = new[] { ".png", ".jpg", ".jpeg" };

    public static bool IsSupportedFileName(string path)
    {
        var extension = Path.GetExtension(path);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public static SignatureResolveResult Resolve(string projectRoot, string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return new SignatureResolveResult(SignatureResolveStatus.Empty, null);
        }

        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return new SignatureResolveResult(SignatureResolveStatus.OutsideProject, null);
        }

        string full;
        try
        {
            var root = Normalize(Path.GetFullPath(projectRoot));
            full = Path.IsPathRooted(storedPath)
                ? Normalize(Path.GetFullPath(storedPath))
                : Normalize(Path.GetFullPath(Path.Combine(root, storedPath.Replace('/', Path.DirectorySeparatorChar))));

            if (!IsWithin(root, full))
            {
                return new SignatureResolveResult(SignatureResolveStatus.OutsideProject, null);
            }
        }
        catch
        {
            return new SignatureResolveResult(SignatureResolveStatus.OutsideProject, null);
        }

        if (!IsSupportedFileName(full))
        {
            return new SignatureResolveResult(SignatureResolveStatus.UnsupportedExtension, null);
        }

        return File.Exists(full)
            ? new SignatureResolveResult(SignatureResolveStatus.Valid, full)
            : new SignatureResolveResult(SignatureResolveStatus.MissingFile, null);
    }

    public static string? RelativePath(string projectRoot, string fullPath)
    {
        try
        {
            var root = Normalize(Path.GetFullPath(projectRoot));
            var candidate = Path.IsPathRooted(fullPath)
                ? Path.GetFullPath(fullPath)
                : Path.GetFullPath(Path.Combine(root, fullPath.Replace('/', Path.DirectorySeparatorChar)));
            var full = Normalize(candidate);
            if (!IsWithin(root, full))
            {
                return null;
            }

            return Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');
        }
        catch
        {
            return null;
        }
    }

    public static List<string> ListSignatureFiles(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return new List<string>();
        }

        var directory = Path.Combine(projectRoot, ProjectLayout.SignaturesFolderName);
        if (!Directory.Exists(directory))
        {
            return new List<string>();
        }

        return Directory.EnumerateFiles(directory)
            .Where(IsSupportedFileName)
            .Select(f => RelativePath(projectRoot, f))
            .Where(p => p is not null)
            .Select(p => p!)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? Import(string projectRoot, string sourcePath, bool replaceIfExists)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        var fileName = Path.GetFileName(sourcePath);
        if (!IsSupportedFileName(fileName))
        {
            throw new ArgumentException("Only PNG, JPG, and JPEG signature images are supported.");
        }

        var directory = Path.Combine(projectRoot, ProjectLayout.SignaturesFolderName);
        Directory.CreateDirectory(directory);

        var target = Path.Combine(directory, fileName);
        if (File.Exists(target) && !replaceIfExists)
        {
            target = UniqueFileName(target);
        }

        File.Copy(sourcePath, target, overwrite: true);
        return RelativePath(projectRoot, target);
    }

    public static string SignatureRelativePath(string fileName)
        => $"{ProjectLayout.SignaturesFolderName}/{fileName}";

    private static string UniqueFileName(string target)
    {
        var directory = Path.GetDirectoryName(target)!;
        var name = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);
        var candidate = target;
        var counter = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{name} ({counter}){extension}");
            counter++;
        }

        return candidate;
    }

    private static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(path);

    private static bool IsWithin(string root, string candidate)
    {
        if (candidate.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
