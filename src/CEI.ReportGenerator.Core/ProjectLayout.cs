using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core;

public static class ProjectLayout
{
    public const string ProjectFileName = "project.json";

    public const string ReportsFolderName = "Reports";

    public const string SignaturesFolderName = "Signatures";

    public const string PhotosFolderName = "photos";

    public const string TemplateFileName = "Template.docx";

    public const string InspectorSignatureFileName = "inspector_signature.png";

    public const string ProjectManagerSignatureFileName = "pm_signature.png";

    public static string ReportsFolder(Project project)
        => Path.Combine(project.FolderPath, ReportsFolderName);

    public static string ReportFolder(Project project, int reportNumber)
        => Path.Combine(ReportsFolder(project), FormatReportNumber(reportNumber));

    public static string ReportFilePath(Project project, int reportNumber)
        => Path.Combine(ReportFolder(project, reportNumber), "report.json");

    public static string ReportPhotosFolder(Project project, int reportNumber)
        => Path.Combine(ReportFolder(project, reportNumber), PhotosFolderName);

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
