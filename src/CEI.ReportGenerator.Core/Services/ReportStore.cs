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

        foreach (var photo in report.Photos)
        {
            if (string.IsNullOrEmpty(photo.StoredFileName) && File.Exists(photo.SourcePath))
            {
                var extension = Path.GetExtension(photo.SourcePath);
                if (string.IsNullOrEmpty(extension))
                {
                    extension = ".jpg";
                }

                photo.StoredFileName = $"{report.Number:D4}_photo{report.Photos.IndexOf(photo) + 1}{extension}";
            }

            var storedPath = Path.Combine(photosFolder, photo.StoredFileName);
            if (File.Exists(photo.SourcePath) && !File.Exists(storedPath))
            {
                File.Copy(photo.SourcePath, storedPath);
            }
        }

        JsonStore.Save(ProjectLayout.ReportFilePath(project, report.Number), report);
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
