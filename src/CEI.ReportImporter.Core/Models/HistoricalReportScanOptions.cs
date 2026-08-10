namespace CEI.ReportImporter.Core.Models;

public sealed class HistoricalReportScanOptions
{
    public string SourceFolder { get; init; } = string.Empty;

    public bool IncludeSubfolders { get; init; }

    public string DestinationProjectFolder { get; init; } = string.Empty;

    public string DestinationProjectName { get; init; } = string.Empty;

    public string DestinationProjectNumber { get; init; } = string.Empty;
}
