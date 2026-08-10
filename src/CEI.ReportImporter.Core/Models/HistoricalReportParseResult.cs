using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportImporter.Core.Models;

public sealed class HistoricalReportParseResult
{
    public required HistoricalReportParseStatus Status { get; init; }

    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public required string ParserProfile { get; init; }

    public required HistoricalConfidenceLevel OverallConfidence { get; init; }

    public required HistoricalFieldConfidence FieldConfidence { get; init; }

    public HistoricalFieldExtractions FieldExtractions { get; init; } = new();

    public HistoricalReportImportRequest? Request { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public int? ReportNumber { get; init; }

    public DateTime? Date { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string FailureMessage { get; init; } = string.Empty;

    public bool Success => Request is not null;

    public string StatusText => Status switch
    {
        HistoricalReportParseStatus.Parsed => "Parsed",
        HistoricalReportParseStatus.ParsedWithWarnings => "Parsed With Warnings",
        _ => "Failed"
    };

    public string ReportNumberText => ReportNumber?.ToString() ?? string.Empty;

    public string DateText => Date?.ToString("yyyy-MM-dd") ?? string.Empty;

    public string ConfidenceText => OverallConfidence.ToString();

    public string WarningsText => Warnings.Count switch
    {
        0 => string.Empty,
        1 => Warnings[0],
        _ => $"{Warnings.Count} warnings"
    };
}
