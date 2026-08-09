using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core;

public static class ProjectLayout
{
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public const string ProjectFileName = "project.json";

    public const string ReportsFolderName = "Reports";

    public const string SignaturesFolderName = "Signatures";

    public const string PhotosFolderName = "photos";

    public const string WorkingFolderName = "working";

    public const string PreviewFileName = "preview.docx";

    public const string TemplateFileName = "Template.docx";

    public static string ReportsFolder(Project project)
        => Path.Combine(project.FolderPath, ReportsFolderName);

    public static string ReportFolder(Project project, int reportNumber)
        => Path.Combine(ReportsFolder(project), FormatReportNumber(reportNumber));

    public static string ReportFilePath(Project project, int reportNumber)
        => Path.Combine(ReportFolder(project, reportNumber), "report.json");

    public static string ReportPhotosFolder(Project project, int reportNumber)
        => Path.Combine(ReportFolder(project, reportNumber), PhotosFolderName);

    public static string ReportWorkingFolder(Project project, int reportNumber)
        => Path.Combine(ReportFolder(project, reportNumber), WorkingFolderName);

    public static string ReportPreviewPath(Project project, int reportNumber)
        => Path.Combine(ReportWorkingFolder(project, reportNumber), PreviewFileName);

    public static string FinalReportPath(Project project, InspectionReport report)
        => Path.Combine(ReportFolder(project, report.Number), DefaultReportFileName(project, report));

    public static string DefaultNewProjectFolderPath(string projectsRoot, string projectName)
        => Path.Combine(projectsRoot, SanitizeProjectFolderName(projectName));

    public static string FinalizingReportPath(Project project, InspectionReport report)
        => Path.Combine(
            ReportFolder(project, report.Number),
            $".{DefaultReportFileName(project, report)}.{Guid.NewGuid():N}.finalizing.docx");

    public static string SignaturesFolder(Project project)
        => Path.Combine(project.FolderPath, SignaturesFolderName);

    public static string DefaultReportFileName(Project project, InspectionReport report)
        => BuildFinalReportFileNameInfo(project, report).FileName;

    public static FinalReportFileNameInfo BuildFinalReportFileNameInfo(Project project, InspectionReport report)
    {
        var datePart = report.Date == default ? DateTime.Today.ToString("yyyy-MM-dd") : report.Date.ToString("yyyy-MM-dd");
        var projectNamePart = SanitizeFileNameSegment(project.Name, "Project");
        var reportNumberPart = report.Number.ToString();
        var reportLabel = HasTrailingSpinWord(projectNamePart)
            ? "Report"
            : "SPIN Report";
        var fileName = $"{datePart} {projectNamePart} {reportLabel} #{reportNumberPart}.docx";
        return new FinalReportFileNameInfo(datePart, projectNamePart, reportNumberPart, fileName);
    }

    public static string FormatReportNumber(int reportNumber)
        => reportNumber.ToString("D4");

    public static bool IsValidProjectFolder(string path)
        => Directory.Exists(path)
           && File.Exists(Path.Combine(path, ProjectFileName));

    public static string SanitizeProjectFolderName(string projectName)
        => SanitizeWindowsNameSegment(projectName, "New Project");

    private static bool HasTrailingSpinWord(string projectName)
        => string.Equals(projectName, "SPIN", StringComparison.OrdinalIgnoreCase)
           || projectName.EndsWith(" SPIN", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeFileNameSegment(string value, string fallback)
        => SanitizeWindowsNameSegment(value, fallback);

    private static string SanitizeWindowsNameSegment(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Trim()
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray());
        sanitized = string.Join(" ", sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        sanitized = sanitized.TrimEnd('.', ' ');
        if (ReservedWindowsNames.Contains(sanitized))
        {
            sanitized += "_";
        }
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
