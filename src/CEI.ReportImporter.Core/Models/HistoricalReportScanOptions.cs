namespace CEI.ReportImporter.Core.Models;

public sealed class HistoricalReportScanOptions
{
    public string SourceFolder { get; init; } = string.Empty;

    public bool IncludeSubfolders { get; init; }
}
