using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportImporter.Core.Models;

public sealed record HistoricalScanResult
{
    public required string SourceFilePath { get; init; }

    public required string SourceFileName { get; init; }

    public required HistoricalReportParseResult ParseResult { get; init; }

    public HistoricalReportImportRequest? ImportRequest => ParseResult.Request;

    public bool ParseSucceeded => ParseResult.Success;

    public bool HasWarnings => ParseResult.Warnings.Count > 0;

    public int WarningCount => ParseResult.Warnings.Count;
}
