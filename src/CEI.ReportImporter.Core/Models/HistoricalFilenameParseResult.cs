namespace CEI.ReportImporter.Core.Models;

public sealed class HistoricalFilenameParseResult
{
    public string FileName { get; init; } = string.Empty;

    public int? ReportNumber { get; init; }

    public DateTime? Date { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
