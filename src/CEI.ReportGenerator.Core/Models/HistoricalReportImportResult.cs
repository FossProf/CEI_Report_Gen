namespace CEI.ReportGenerator.Core.Models;

public enum HistoricalReportImportStatus
{
    Imported,
    InvalidProject,
    ReportAlreadyExists,
    FolderConflict
}

public sealed class HistoricalReportImportResult
{
    public required HistoricalReportImportStatus Status { get; init; }

    public required string Message { get; init; }

    public required string ReportFolder { get; init; }

    public required string ReportJsonPath { get; init; }

    public required string ImportMetadataPath { get; init; }

    public InspectionReport? Report { get; init; }

    public bool Succeeded => Status == HistoricalReportImportStatus.Imported;
}
