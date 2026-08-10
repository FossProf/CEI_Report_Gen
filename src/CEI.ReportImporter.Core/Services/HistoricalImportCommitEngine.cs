using CEI.ReportGenerator.Core;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;
using CEI.ReportImporter.Core.Models;

namespace CEI.ReportImporter.Core.Services;

public sealed class HistoricalImportCommitEngine
{
    private readonly HistoricalReviewValidator _reviewValidator = new();

    public HistoricalReviewSession CreateSession(HistoricalScanSession scanSession, Project destinationProject)
    {
        ArgumentNullException.ThrowIfNull(scanSession);
        ArgumentNullException.ThrowIfNull(destinationProject);
        EnsureValidDestinationProject(destinationProject);

        var session = new HistoricalReviewSession(scanSession);
        RefreshSession(session, destinationProject);
        return session;
    }

    public void RefreshSession(HistoricalReviewSession session, Project destinationProject)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(destinationProject);
        EnsureValidDestinationProject(destinationProject);

        session.ScanSession.DestinationProjectFolder = destinationProject.FolderPath;
        session.ScanSession.DestinationProjectName = destinationProject.Name;
        session.ScanSession.DestinationProjectNumber = destinationProject.Number;
        session.ScanSession.DestinationProjectReportCount = ReportStore.LoadAllReports(destinationProject).Reports.Count;

        foreach (var item in session.Items)
        {
            if (item.ImportStatus is HistoricalImportItemStatus.Imported or HistoricalImportItemStatus.Skipped)
            {
                continue;
            }

            var evaluation = Evaluate(destinationProject, item);
            item.ApplyImportStatus(evaluation.Status, evaluation.Reason);
        }

        session.RefreshSessionCounts();
    }

    public HistoricalImportBatchResult ImportSelected(
        HistoricalReviewSession session,
        Project destinationProject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(destinationProject);
        EnsureValidDestinationProject(destinationProject);

        RefreshSession(session, destinationProject);

        var startedUtc = DateTime.UtcNow;
        var selectedItems = session.Items.Where(item => item.IsSelected).ToList();
        var logEntries = new List<HistoricalImportLogEntry>(selectedItems.Count);
        var importedCount = 0;
        var skippedCount = 0;
        var duplicateCount = 0;
        var errorCount = 0;
        var cancelled = false;

        foreach (var item in selectedItems)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                item.MarkSkipped("Import cancelled before this report was written.");
                logEntries.Add(BuildLogEntry(item, HistoricalImportItemStatus.Skipped, "Import cancelled before this report was written."));
                skippedCount++;
                continue;
            }

            var evaluation = Evaluate(destinationProject, item);
            item.ApplyImportStatus(evaluation.Status, evaluation.Reason);
            if (item.ImportStatus != HistoricalImportItemStatus.Ready || item.WorkingRequest is null)
            {
                var reason = item.Reason == "-" ? "Only Ready reports can be imported." : item.Reason;
                if (item.ImportStatus == HistoricalImportItemStatus.Duplicate)
                {
                    duplicateCount++;
                }
                else
                {
                    skippedCount++;
                }

                logEntries.Add(BuildLogEntry(item, item.ImportStatus, reason));
                item.IsSelected = false;
                continue;
            }

            try
            {
                var importResult = HistoricalReportImportService.Import(destinationProject, item.WorkingRequest);
                switch (importResult.Status)
                {
                    case HistoricalReportImportStatus.Imported:
                        item.MarkImported("Imported.");
                        importedCount++;
                        logEntries.Add(BuildLogEntry(item, HistoricalImportItemStatus.Imported, "Imported."));
                        break;
                    case HistoricalReportImportStatus.ReportAlreadyExists:
                        item.ApplyImportStatus(HistoricalImportItemStatus.Duplicate, importResult.Message);
                        duplicateCount++;
                        logEntries.Add(BuildLogEntry(item, HistoricalImportItemStatus.Duplicate, importResult.Message));
                        break;
                    case HistoricalReportImportStatus.FolderConflict:
                        item.ApplyImportStatus(HistoricalImportItemStatus.Conflict, importResult.Message);
                        errorCount++;
                        logEntries.Add(BuildLogEntry(item, HistoricalImportItemStatus.Conflict, importResult.Message));
                        break;
                    default:
                        item.ApplyImportStatus(HistoricalImportItemStatus.Conflict, importResult.Message);
                        errorCount++;
                        logEntries.Add(BuildLogEntry(item, HistoricalImportItemStatus.Conflict, importResult.Message));
                        break;
                }
            }
            catch (Exception ex)
            {
                item.ApplyImportStatus(HistoricalImportItemStatus.Conflict, ex.Message);
                errorCount++;
                logEntries.Add(BuildLogEntry(item, HistoricalImportItemStatus.Conflict, ex.Message));
            }
        }

        ProjectStore.RefreshNextReportNumber(destinationProject);
        session.ScanSession.DestinationProjectReportCount = ReportStore.LoadAllReports(destinationProject).Reports.Count;
        session.RefreshSessionCounts();
        session.RecordImportLog(logEntries);

        return new HistoricalImportBatchResult
        {
            SessionId = session.ScanSession.SessionId,
            SelectedCount = selectedItems.Count,
            ImportedCount = importedCount,
            SkippedCount = skippedCount,
            DuplicateCount = duplicateCount,
            ErrorCount = errorCount,
            Elapsed = DateTime.UtcNow - startedUtc,
            Cancelled = cancelled,
            LogEntries = logEntries
        };
    }

    private (HistoricalImportItemStatus Status, string Reason) Evaluate(Project destinationProject, HistoricalReviewItem item)
    {
        if (!item.ParseSucceeded)
        {
            return (HistoricalImportItemStatus.ParseError, item.Reason);
        }

        var validationResult = _reviewValidator.Validate(item.WorkingRequest);
        if (!validationResult.CanMarkReady)
        {
            return (HistoricalImportItemStatus.MissingData, string.Join(" ", validationResult.Messages));
        }

        var request = item.WorkingRequest!;
        var reportFolder = ProjectLayout.ReportFolder(destinationProject, request.Number);
        var reportJsonPath = ProjectLayout.ReportFilePath(destinationProject, request.Number);
        if (File.Exists(reportJsonPath))
        {
            return (HistoricalImportItemStatus.Duplicate, $"Report #{request.Number} already exists in the destination project.");
        }

        if (Directory.Exists(reportFolder))
        {
            return Directory.EnumerateFileSystemEntries(reportFolder).Any()
                ? (HistoricalImportItemStatus.Duplicate, $"Report #{request.Number} already exists in the destination project.")
                : (HistoricalImportItemStatus.Conflict, $"Report #{request.Number} cannot be imported because the canonical report folder already exists.");
        }

        if (ReportStore.ReportNumberExists(destinationProject, request.Number))
        {
            return (HistoricalImportItemStatus.Duplicate, $"Report #{request.Number} already exists in the destination project.");
        }

        var report = BuildCandidateReport(request);
        var validationErrors = Validation.ValidateReport(report);
        if (validationErrors.Count > 0)
        {
            return (HistoricalImportItemStatus.MissingData, string.Join(" ", validationErrors));
        }

        return (HistoricalImportItemStatus.Ready, string.Empty);
    }

    private static InspectionReport BuildCandidateReport(HistoricalReportImportRequest request)
        => new()
        {
            Number = request.Number,
            Status = ReportStatus.Final,
            Date = request.Date,
            Temperature = request.Temperature ?? string.Empty,
            Weather = request.Weather ?? string.Empty,
            Locations = request.Locations ?? string.Empty,
            Inspectors = request.Inspectors ?? string.Empty,
            PersonnelOnSite = request.PersonnelOnSite ?? string.Empty,
            DescriptionOfWork = request.DescriptionOfWork ?? string.Empty,
            DrawingsReviewed = request.DrawingsReviewed ?? string.Empty,
            Observations = request.Observations ?? string.Empty,
            NewDiscrepancies = request.NewDiscrepancies ?? string.Empty,
            PreviousDiscrepancies = request.PreviousDiscrepancies ?? string.Empty,
            Photos = []
        };

    private static HistoricalImportLogEntry BuildLogEntry(HistoricalReviewItem item, HistoricalImportItemStatus result, string reason)
        => new()
        {
            TimestampUtc = DateTime.UtcNow,
            SourceFileName = item.SourceFileName,
            ReportNumber = item.WorkingRequest?.Number ?? 0,
            Result = result,
            Reason = reason
        };

    private static void EnsureValidDestinationProject(Project destinationProject)
    {
        if (!ProjectLayout.IsValidProjectFolder(destinationProject.FolderPath))
        {
            throw new InvalidOperationException("The selected destination is not a valid SPINgen project.");
        }
    }
}
