using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core;

public static class ProjectLayout
{
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

    public static string FinalReportPath(Project project, int reportNumber)
        => Path.Combine(ReportFolder(project, reportNumber), DefaultReportFileName(reportNumber));

    public static string SignaturesFolder(Project project)
        => Path.Combine(project.FolderPath, SignaturesFolderName);

    public static string DefaultReportFileName(int reportNumber)
        => $"{FormatReportNumber(reportNumber)}_SpecialInspectionReport.docx";

    public static string FormatReportNumber(int reportNumber)
        => reportNumber.ToString("D4");

    public static bool IsValidProjectFolder(string path)
        => Directory.Exists(path)
           && File.Exists(Path.Combine(path, ProjectFileName));
}
