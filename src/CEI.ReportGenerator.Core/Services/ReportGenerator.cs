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

        var outputPath = ProjectLayout.ReportPreviewPath(project, report.Number);

        return TemplateFiller.Generate(project, report, outputPath, overwriteExistingOutput: true);
    }

    public static void SaveDraft(Project project, InspectionReport report)
    {
        ReportStore.SaveReport(project, report);
    }

    public static void FinalizeReport(Project project, InspectionReport report, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
        {
            throw new InvalidOperationException("The preview document no longer exists. Generate the report again before finalizing.");
        }

        var finalPath = ProjectLayout.FinalReportPath(project, report.Number);
        var existing = ReportStore.LoadReport(project, report.Number);
        if (existing is not null && existing.CreatedUtc != report.CreatedUtc)
        {
            throw new InvalidOperationException($"Report number {report.Number} is already assigned to another saved report.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        File.Copy(outputPath, finalPath, overwrite: true);

        var savedReport = CloneReport(report);
        savedReport.Status = ReportStatus.Final;
        savedReport.OutputFileName = Path.GetFileName(finalPath);

        ReportStore.SaveReport(project, savedReport);
        ProjectStore.AdvanceReportNumber(project, report.Number);
        ReportStore.CleanupPreviewArtifacts(project, report.Number);

        CopyReportState(savedReport, report);
    }

    private static InspectionReport CloneReport(InspectionReport report)
    {
        return new InspectionReport
        {
            Number = report.Number,
            Status = report.Status,
            Date = report.Date,
            Temperature = report.Temperature,
            Weather = report.Weather,
            Locations = report.Locations,
            Inspectors = report.Inspectors,
            PersonnelOnSite = report.PersonnelOnSite,
            DescriptionOfWork = report.DescriptionOfWork,
            DrawingsReviewed = report.DrawingsReviewed,
            Observations = report.Observations,
            NewDiscrepancies = report.NewDiscrepancies,
            PreviousDiscrepancies = report.PreviousDiscrepancies,
            OutputFileName = report.OutputFileName,
            CreatedUtc = report.CreatedUtc,
            Photos = report.Photos.Select(photo => new Photo
            {
                SourcePath = photo.SourcePath,
                StoredFileName = photo.StoredFileName,
                Caption = photo.Caption
            }).ToList()
        };
    }

    private static void CopyReportState(InspectionReport source, InspectionReport destination)
    {
        destination.Status = source.Status;
        destination.OutputFileName = source.OutputFileName;
        destination.Photos = source.Photos.Select(photo => new Photo
        {
            SourcePath = photo.SourcePath,
            StoredFileName = photo.StoredFileName,
            Caption = photo.Caption
        }).ToList();
    }
}
