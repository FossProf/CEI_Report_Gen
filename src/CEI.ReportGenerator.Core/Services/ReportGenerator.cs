using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public static class ReportGenerator
{
    public static Func<string, Exception?>? SaveFailureHookForTesting { get; set; }

    public static GenerationResult GenerateDraft(Project project, InspectionReport report)
    {
        if (report.Status == ReportStatus.Final)
        {
            throw new InvalidOperationException("Final reports can only update SPINgen metadata. Generate Report is disabled for finalized reports.");
        }

        ReportStore.CleanupPreviewArtifacts(project, report.Number, removePreview: false);

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
        if (report.Status == ReportStatus.Final)
        {
            throw new InvalidOperationException("This report is already final. Final report revision is not supported yet.");
        }

        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
        {
            throw new InvalidOperationException("The preview document no longer exists. Generate the report again before finalizing.");
        }

        var finalPath = ProjectLayout.FinalReportPath(project, report);
        if (File.Exists(finalPath))
        {
            throw new InvalidOperationException($"Final report {report.Number} already exists and will not be overwritten.");
        }

        var existing = ReportStore.LoadReport(project, report.Number);
        if (existing is not null && existing.CreatedUtc != report.CreatedUtc)
        {
            throw new InvalidOperationException($"Report number {report.Number} is already assigned to another saved report.");
        }

        var savedReport = CloneReport(report);
        savedReport.Status = ReportStatus.Final;
        savedReport.OutputFileName = Path.GetFileName(finalPath);
        var previousProjectNextNumber = project.NextReportNumber;
        var previousReportJsonPath = ProjectLayout.ReportFilePath(project, report.Number);
        var previousProjectJsonPath = project.FilePath;
        var previousReportJson = File.Exists(previousReportJsonPath) ? File.ReadAllBytes(previousReportJsonPath) : null;
        var previousProjectJson = File.Exists(previousProjectJsonPath) ? File.ReadAllBytes(previousProjectJsonPath) : null;
        var previouslyPersistedStoredPhotoFileNames = (existing?.Photos ?? [])
            .Select(photo => photo.StoredFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        List<string>? preRenameStoredPhotoFileNames = null;
        var stagedFinalPath = ProjectLayout.FinalizingReportPath(project, report);
        var rollbackFailures = new List<string>();
        var reportPersisted = false;
        var projectPersisted = false;

        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        ReportStore.CleanupPreviewArtifacts(project, report.Number, removePreview: false);
        File.Copy(outputPath, stagedFinalPath, overwrite: false);

        try
        {
            using (DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stagedFinalPath, false))
            {
            }

            MaybeFail(previousReportJsonPath);
            ReportStore.SaveReport(project, savedReport);
            reportPersisted = true;
            preRenameStoredPhotoFileNames = savedReport.Photos.Select(photo => photo.StoredFileName).ToList();

            ReportStore.RenameStoredPhotosForFinalization(project, savedReport);
            MaybeFail(previousReportJsonPath);
            ReportStore.SaveReport(project, savedReport);

            MaybeFail(previousProjectJsonPath);
            ProjectStore.AdvanceReportNumber(project, report.Number);
            projectPersisted = true;

            File.Move(stagedFinalPath, finalPath, overwrite: false);
            ReportStore.CleanupPreviewArtifacts(project, report.Number);
            CopyReportState(savedReport, report);
        }
        catch (Exception ex)
        {
            TryDeleteFile(stagedFinalPath, rollbackFailures);
            if (preRenameStoredPhotoFileNames is not null)
            {
                TryRestoreStoredPhotos(project, savedReport, preRenameStoredPhotoFileNames, rollbackFailures);
                TryCleanupUnpersistedStoredPhotos(
                    project,
                    report.Number,
                    preRenameStoredPhotoFileNames,
                    previouslyPersistedStoredPhotoFileNames,
                    rollbackFailures);
            }

            if (projectPersisted)
            {
                TryRestoreFile(previousProjectJsonPath, previousProjectJson, rollbackFailures);
            }

            if (reportPersisted)
            {
                TryRestoreFile(previousReportJsonPath, previousReportJson, rollbackFailures);
            }

            project.NextReportNumber = previousProjectNextNumber;

            if (rollbackFailures.Count > 0)
            {
                throw new InvalidOperationException(
                    ex.Message + Environment.NewLine + "Rollback failures: " + string.Join(" | ", rollbackFailures),
                    ex);
            }

            throw;
        }
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

    private static void TryRestoreFile(string path, byte[]? previousBytes, List<string> rollbackFailures)
    {
        try
        {
            if (previousBytes is null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, previousBytes);
            }
        }
        catch (Exception ex)
        {
            rollbackFailures.Add($"{path}: {ex.Message}");
        }
    }

    private static void TryDeleteFile(string path, List<string> rollbackFailures)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            rollbackFailures.Add($"{path}: {ex.Message}");
        }
    }

    private static void TryRestoreStoredPhotos(
        Project project,
        InspectionReport report,
        IReadOnlyList<string> previousStoredPhotoFileNames,
        List<string> rollbackFailures)
    {
        try
        {
            ReportStore.RestoreStoredPhotosAfterFailedFinalization(project, report, previousStoredPhotoFileNames);
        }
        catch (Exception ex)
        {
            rollbackFailures.Add($"stored photos: {ex.Message}");
        }
    }

    private static void TryCleanupUnpersistedStoredPhotos(
        Project project,
        int reportNumber,
        IReadOnlyList<string> attemptedStoredPhotoFileNames,
        IReadOnlyCollection<string> previouslyPersistedStoredPhotoFileNames,
        List<string> rollbackFailures)
    {
        try
        {
            var previouslyPersisted = previouslyPersistedStoredPhotoFileNames
                .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var createdOnlyForThisAttempt = attemptedStoredPhotoFileNames
                .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(fileName => !previouslyPersisted.Contains(fileName))
                .ToList();

            if (createdOnlyForThisAttempt.Count == 0)
            {
                return;
            }

            ReportStore.DeleteStoredPhotos(project, reportNumber, createdOnlyForThisAttempt);
        }
        catch (Exception ex)
        {
            rollbackFailures.Add($"stored photo cleanup: {ex.Message}");
        }
    }

    private static void MaybeFail(string path)
    {
        var failure = SaveFailureHookForTesting?.Invoke(path);
        if (failure is not null)
        {
            throw failure;
        }
    }
}
