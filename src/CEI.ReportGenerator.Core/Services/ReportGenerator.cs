using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public static class ReportGenerator
{
    public static GenerationResult GenerateDraft(Project project, InspectionReport report)
    {
        var errors = Validation.ValidateProject(project);
        if (errors.Count > 0)
        {
            throw new GenerationException(GenerationStage.ValidateProject, errors);
        }

        errors = Validation.ValidateReportForGeneration(report);
        if (errors.Count > 0)
        {
            throw new GenerationException(GenerationStage.ValidateReport, errors);
        }

        errors = TemplateValidator.ValidateTemplate(project.TemplatePath);
        if (errors.Count > 0)
        {
            throw new GenerationException(GenerationStage.ValidateTemplate, errors);
        }

        var outputPath = Path.Combine(
            ProjectLayout.ReportFolder(project, report.Number),
            ProjectLayout.DefaultReportFileName(report.Number));

        return TemplateFiller.Generate(project, report, outputPath);
    }

    public static void SaveDraft(Project project, InspectionReport report)
    {
        ReportStore.SaveReport(project, report);
    }

    public static void FinalizeReport(Project project, InspectionReport report, string outputPath)
    {
        report.Status = ReportStatus.Final;
        report.OutputFileName = Path.GetFileName(outputPath);
        ReportStore.SaveReport(project, report);
        ProjectStore.IncrementReportNumber(project);
    }
}
