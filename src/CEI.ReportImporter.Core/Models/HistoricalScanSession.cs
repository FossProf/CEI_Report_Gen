namespace CEI.ReportImporter.Core.Models;

public sealed class HistoricalScanSession
{
    public required Guid SessionId { get; init; }

    public required string SourceFolder { get; init; }

    public required bool IncludeSubfolders { get; init; }

    public required DateTime StartedUtc { get; init; }

    public required DateTime CompletedUtc { get; init; }

    public required string ParserProfile { get; init; }

    public required IReadOnlyList<HistoricalScanResult> Results { get; init; }

    public string DestinationProjectFolder { get; set; } = string.Empty;

    public string DestinationProjectName { get; set; } = string.Empty;

    public string DestinationProjectNumber { get; set; } = string.Empty;

    public int DestinationProjectReportCount { get; set; }

    public int ReadyCount { get; set; }

    public int ImportedCount { get; set; }

    public int SkippedCount { get; set; }

    public int DuplicateCount { get; set; }

    public int ErrorCount { get; set; }

    public int SelectedCount { get; set; }

    public int FilesDiscovered => Results.Count;

    public int ParsedCount => Results.Count(result => result.ParseSucceeded);

    public int FailedCount => Results.Count(result => !result.ParseSucceeded);

    public int WarningCount => Results.Sum(result => result.WarningCount);
}
